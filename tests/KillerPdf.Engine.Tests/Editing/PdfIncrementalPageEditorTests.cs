using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;
using KillerPdf.Engine.Security;
using KillerPdf.Engine.Filters;
using Xunit;

namespace KillerPdf.Engine.Tests.Editing;

public sealed class PdfIncrementalPageEditorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Build_RejectsPageTreeChangesProhibitedByCertification(int permission)
    {
        var editor = new PdfIncrementalPageEditor(
            PdfDocument.Open(CertifiedSource(permission))).RotateClockwise(0);

        Assert.Throws<InvalidOperationException>(() => editor.Build());
    }

    [Fact]
    public void Build_FailsClosedOnDirectCertificationSignatureDictionary()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            document.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(document, catalogReference);
        var update = new PdfIncrementalUpdateBuilder(document);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Perms"), new PdfDictionary([
                new(Name("DocMDP"), new PdfDictionary([
                    new(Name("Type"), Name("Sig"))
                ]))
            ])))));
        var editor = new PdfIncrementalPageEditor(PdfDocument.Open(update.Build()))
            .RotateClockwise(0);

        Assert.Throws<InvalidOperationException>(() => editor.Build());
    }

    [Fact]
    public void Constructor_RejectsPageTreeNodeReferencedMoreThanOnce()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        (PdfIndirectReference rootReference, PdfIndirectReference[] pages, _) = FlatPages(document);
        PdfDictionary root = ResolveDictionary(document, rootReference);
        var duplicateKids = new PdfArray([pages[0], pages[0]]);
        var replacement = new PdfDictionary(root.Select(entry =>
            entry.Key.Equals(Name("Kids"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, duplicateKids)
                : entry));
        var update = new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(rootReference.ObjectNumber, replacement);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(update.Build())));

        Assert.Contains("same node", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_DoesNotMisclassifyStalePageTreeGenerationAsCycle()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        (PdfIndirectReference rootReference, _, _) = FlatPages(document);
        PdfDictionary root = ResolveDictionary(document, rootReference);
        var staleSelf = new PdfIndirectReference(
            rootReference.ObjectNumber, rootReference.Generation + 1);
        var replacement = new PdfDictionary(root.Select(entry =>
            entry.Key.Equals(Name("Kids"))
                ? new KeyValuePair<PdfName, PdfObject>(
                    entry.Key, new PdfArray([staleSelf]))
                : entry));
        var update = new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(rootReference.ObjectNumber, replacement);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(update.Build())));

        Assert.Contains("not a dictionary", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cycle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ValidatesPageTreeTypesCountsAndIndirectStructuralValues()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            document.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(document, catalogReference);
        (PdfIndirectReference rootReference, PdfIndirectReference[] pages, _) = FlatPages(document);
        PdfDictionary root = ResolveDictionary(document, rootReference);
        var indirectUpdate = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference indirectType = indirectUpdate.AddObject(Name("Pages"));
        PdfIndirectReference indirectKids = indirectUpdate.AddObject(root[Name("Kids")]);
        PdfIndirectReference indirectCount = indirectUpdate.AddObject(root[Name("Count")]);
        PdfIndirectReference indirectTypeAlias = indirectUpdate.AddObject(indirectType);
        PdfIndirectReference indirectKidsAlias = indirectUpdate.AddObject(indirectKids);
        PdfIndirectReference indirectCountAlias = indirectUpdate.AddObject(indirectCount);
        indirectUpdate.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Select(entry =>
            entry.Key.Equals(Name("Type"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, indirectTypeAlias)
                : entry.Key.Equals(Name("Kids"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, indirectKidsAlias)
                    : entry.Key.Equals(Name("Count"))
                        ? new KeyValuePair<PdfName, PdfObject>(entry.Key, indirectCountAlias)
                        : entry)));

        var indirectEditor = new PdfIncrementalPageEditor(
            PdfDocument.Open(indirectUpdate.Build()));

        Assert.Equal(1, indirectEditor.PageCount);

        var aliasedNodeUpdate = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference pageAlias = aliasedNodeUpdate.AddObject(pages[0]);
        PdfIndirectReference pageOuterAlias = aliasedNodeUpdate.AddObject(pageAlias);
        PdfIndirectReference parentAlias = aliasedNodeUpdate.AddObject(rootReference);
        PdfIndirectReference parentOuterAlias = aliasedNodeUpdate.AddObject(parentAlias);
        PdfDictionary aliasedPage = new(ResolveDictionary(document, pages[0]).Select(entry =>
            entry.Key.Equals(Name("Parent"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, parentOuterAlias)
                : entry));
        aliasedNodeUpdate.ReplaceObject(pages[0].ObjectNumber, aliasedPage);
        aliasedNodeUpdate.ReplaceObject(rootReference.ObjectNumber,
            new PdfDictionary(root.Select(entry => entry.Key.Equals(Name("Kids"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                    new PdfArray([pageOuterAlias]))
                : entry)));
        var aliasedNodeEditor = new PdfIncrementalPageEditor(
            PdfDocument.Open(aliasedNodeUpdate.Build()));

        Assert.Equal(1, aliasedNodeEditor.PageCount);

        var indirectCatalogUpdate = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference indirectCatalogType = indirectCatalogUpdate.AddObject(Name("Catalog"));
        PdfIndirectReference indirectCatalogTypeAlias =
            indirectCatalogUpdate.AddObject(indirectCatalogType);
        indirectCatalogUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Select(entry => entry.Key.Equals(Name("Type"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, indirectCatalogTypeAlias)
                : entry)));
        PdfDocument indirectCatalogDocument = PdfDocument.Open(
            indirectCatalogUpdate.Build());
        Assert.Equal(1, new PdfIncrementalPageEditor(
            indirectCatalogDocument).PageCount);
        Assert.Equal(1, new PdfIncrementalPageEditor(PdfDocument.Open(
            PdfDocumentWriter.Write(indirectCatalogDocument))).PageCount);

        var catalogCycleUpdate = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference catalogCycleA = catalogCycleUpdate.ReserveObject();
        PdfIndirectReference catalogCycleB = catalogCycleUpdate.ReserveObject();
        catalogCycleUpdate.SetObject(catalogCycleA, catalogCycleB);
        catalogCycleUpdate.SetObject(catalogCycleB, catalogCycleA);
        catalogCycleUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Select(entry => entry.Key.Equals(Name("Type"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, catalogCycleA)
                : entry)));
        InvalidOperationException catalogCycleError =
            Assert.Throws<InvalidOperationException>(() => catalogCycleUpdate.Build());
        Assert.Contains("indirect-reference cycle", catalogCycleError.Message,
            StringComparison.Ordinal);

        var pageTreeCycleUpdate = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference pageTreeCycleA = pageTreeCycleUpdate.ReserveObject();
        PdfIndirectReference pageTreeCycleB = pageTreeCycleUpdate.ReserveObject();
        pageTreeCycleUpdate.SetObject(pageTreeCycleA, pageTreeCycleB);
        pageTreeCycleUpdate.SetObject(pageTreeCycleB, pageTreeCycleA);
        pageTreeCycleUpdate.ReplaceObject(rootReference.ObjectNumber,
            new PdfDictionary(root.Select(entry => entry.Key.Equals(Name("Type"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, pageTreeCycleA)
                : entry)));
        InvalidOperationException pageTreeCycleError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(
                    PdfDocument.Open(pageTreeCycleUpdate.Build())));
        Assert.Contains("indirect-reference cycle", pageTreeCycleError.Message,
            StringComparison.Ordinal);

        PdfDocument wrongCount = PdfDocument.Open(new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Select(entry =>
                entry.Key.Equals(Name("Count"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, new PdfInteger(2))
                    : entry)))
            .Build());
        InvalidOperationException countError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(wrongCount));
        Assert.Contains("does not match its descendant page count",
            countError.Message, StringComparison.Ordinal);

        PdfDictionary page = ResolveDictionary(document, pages[0]);
        PdfDocument branchedLeaf = PdfDocument.Open(new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(pages[0].ObjectNumber, new PdfDictionary(page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Count"), new PdfInteger(0)))))
            .Build());
        InvalidOperationException leafError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(branchedLeaf));
        Assert.Contains("/Type /Page leaf contains", leafError.Message, StringComparison.Ordinal);

        PdfDocument wrongParent = PdfDocument.Open(new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(pages[0].ObjectNumber, new PdfDictionary(page.Select(entry =>
                entry.Key.Equals(Name("Parent"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, pages[0])
                    : entry)))
            .Build());
        InvalidOperationException parentError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(wrongParent));
        Assert.Contains("/Parent does not identify", parentError.Message, StringComparison.Ordinal);

        PdfDocument missingParent = PdfDocument.Open(new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(pages[0].ObjectNumber, new PdfDictionary(page
                .Where(entry => !entry.Key.Equals(Name("Parent")))))
            .Build());
        InvalidOperationException missingParentError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(missingParent));
        Assert.Contains("has no /Parent reference",
            missingParentError.Message, StringComparison.Ordinal);

        PdfDocument parentedRoot = PdfDocument.Open(new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Parent"), pages[0]))))
            .Build());
        InvalidOperationException rootParentError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(parentedRoot));
        Assert.Contains("root page-tree node contains a /Parent",
            rootParentError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ResolvesCatalogAndPageRootAliasChains()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        PdfDocument document = PdfDocument.Open(source);
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            document.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(document, catalogReference);
        PdfIndirectReference pageRootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("Pages")]);
        var pagesUpdate = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference pageRootAlias = pagesUpdate.AddObject(pageRootReference);
        PdfIndirectReference pageRootSecondAlias = pagesUpdate.AddObject(pageRootAlias);
        pagesUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Select(entry => entry.Key.Equals(Name("Pages"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, pageRootSecondAlias)
                : entry)));

        Assert.Equal(1, new PdfIncrementalPageEditor(
            PdfDocument.Open(pagesUpdate.Build())).PageCount);

        byte[] aliasedRoot = AppendCatalogRootAliases(source, catalogReference);
        PdfDocument aliasedDocument = PdfDocument.Open(aliasedRoot);
        var editor = new PdfIncrementalPageEditor(aliasedDocument);
        Assert.Equal(1, editor.PageCount);
        byte[] updated = editor.AddBlankPage().Build();
        Assert.Equal(2, new PdfIncrementalPageEditor(
            PdfDocument.Open(updated)).PageCount);

        byte[] cyclicRoot = AppendCatalogRootAliases(
            source, catalogReference, cycle: true);
        InvalidOperationException catalogCycle = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(cyclicRoot)));
        Assert.Contains("indirect-reference cycle", catalogCycle.Message,
            StringComparison.Ordinal);

        var pageCycleUpdate = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference pageCycleA = pageCycleUpdate.ReserveObject();
        PdfIndirectReference pageCycleB = pageCycleUpdate.ReserveObject();
        pageCycleUpdate.SetObject(pageCycleA, pageCycleB).SetObject(pageCycleB, pageCycleA);
        pageCycleUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Select(entry => entry.Key.Equals(Name("Pages"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, pageCycleA)
                : entry)));
        InvalidOperationException pageCycle = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(pageCycleUpdate.Build())));
        Assert.Contains("indirect-reference cycle", pageCycle.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotMisclassifyStaleNameTreeGenerationAsCycle()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddNamedDestination("chapter", 0)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary names = Assert.IsType<PdfDictionary>(catalog[Name("Names")]);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference staleTree = update.ReserveObject();
        update.SetObject(staleTree, new PdfDictionary([
            new(Name("Kids"), new PdfArray([
                new PdfIndirectReference(staleTree.ObjectNumber, staleTree.Generation + 1)
            ]))
        ]));
        PdfDictionary rewrittenNames = new(names.Select(entry =>
            entry.Key.Equals(Name("Dests"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, staleTree)
                : entry));
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Select(entry =>
            entry.Key.Equals(Name("Names"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, rewrittenNames)
                : entry)));
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source)
                .Build());

        Assert.Contains("name-tree node is not a dictionary", error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("cycle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotMisclassifyStaleNumberTreeGenerationAsCycle()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference staleTree = update.ReserveObject();
        update.SetObject(staleTree, new PdfDictionary([
            new(Name("Kids"), new PdfArray([
                new PdfIndirectReference(staleTree.ObjectNumber, staleTree.Generation + 1)
            ]))
        ]));
        update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Select(entry =>
            entry.Key.Equals(Name("ParentTree"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, staleTree)
                : entry)));
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source)
                .Build());

        Assert.Contains("number-tree node is not a dictionary", error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("cycle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_CanEmitCompressedStructuralRevision()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();

        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(source))
            .RotateClockwise(0)
            .Build(StructuralOptions()));

        Assert.True(reopened.CrossReferences.Sections[0].IsStream);
        Assert.Contains(reopened.CrossReferences.Sections[0].Values,
            entry => entry.Type == PdfCrossReferenceEntryType.Compressed);
        Assert.Equal(90, Assert.IsType<PdfInteger>(
            FlatPages(reopened).Pages[0][Name("Rotate")]).Value);
    }

    private static PdfIncrementalUpdateWriteOptions StructuralOptions() => new()
    {
        CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
        UseObjectStreams = true,
        CompressObjectStreams = true,
        CompressCrossReferenceStream = true
    };

    [Fact]
    public void Build_ReordersPagesAndKeepsRotationWithTheMovedPage()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .AddBlankPage(400, 600)
            .Build();
        PdfDocument original = PdfDocument.Open(source);
        PdfIndirectReference[] originalPages = FlatPages(original).References;

        byte[] result = new PdfIncrementalPageEditor(original)
            .RotateClockwise(0)
            .MovePage(0, 1)
            .Build();
        PdfDocument reopened = PdfDocument.Open(result);
        (PdfIndirectReference root, PdfIndirectReference[] references, PdfDictionary[] pages) =
            FlatPages(reopened);

        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
        Assert.Equal(originalPages[1].ObjectNumber, references[0].ObjectNumber);
        Assert.Equal(originalPages[0].ObjectNumber, references[1].ObjectNumber);
        Assert.All(pages, page => Assert.Equal(root.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(page[Name("Parent")]).ObjectNumber));
        Assert.Equal(400, BoxWidth(pages[0]));
        Assert.Equal(200, BoxWidth(pages[1]));
        Assert.Equal(90, Assert.IsType<PdfInteger>(pages[1][Name("Rotate")]).Value);
    }

    [Fact]
    public void Build_MaterializesInheritedPagePropertiesWhenFlatteningNestedTrees()
    {
        byte[] source = BuildNestedPageTree();
        PdfDocument original = PdfDocument.Open(source);

        byte[] result = new PdfIncrementalPageEditor(original).MovePage(0, 1).Build();
        PdfDocument reopened = PdfDocument.Open(result);
        (PdfIndirectReference root, PdfIndirectReference[] references, PdfDictionary[] pages) =
            FlatPages(reopened);

        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
        Assert.Equal([5, 4], references.Select(reference => reference.ObjectNumber));
        Assert.All(pages, page =>
        {
            Assert.Equal(root.ObjectNumber,
                Assert.IsType<PdfIndirectReference>(page[Name("Parent")]).ObjectNumber);
            Assert.True(page.ContainsKey(Name("Resources")));
            Assert.True(page.ContainsKey(Name("MediaBox")));
            Assert.True(page.ContainsKey(Name("CropBox")));
            Assert.Equal(90, Assert.IsType<PdfInteger>(page[Name("Rotate")]).Value);
        });
    }

    [Fact]
    public void Build_RotatesFromAnInheritedValueWithoutRebuildingTheTree()
    {
        PdfDocument direct = PdfDocument.Open(BuildNestedPageTree());
        PdfDictionary parent = ResolveDictionary(
            direct, new PdfIndirectReference(3, 0));
        var indirectUpdate = new PdfIncrementalUpdateBuilder(direct);
        PdfIndirectReference rotation = indirectUpdate.AddObject(new PdfInteger(90));
        indirectUpdate.ReplaceObject(3, new PdfDictionary(parent.Select(entry =>
            entry.Key.Equals(Name("Rotate"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, rotation)
                : entry)));
        byte[] source = indirectUpdate.Build();
        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalPageEditor(PdfDocument.Open(source))
            .RotateClockwise(0)
            .RotateCounterClockwise(1)
            .Build());
        PdfDictionary first = ResolveDictionary(reopened, new PdfIndirectReference(4, 0));
        PdfDictionary second = ResolveDictionary(reopened, new PdfIndirectReference(5, 0));

        Assert.Equal(180, Assert.IsType<PdfInteger>(first[Name("Rotate")]).Value);
        Assert.Equal(0, Assert.IsType<PdfInteger>(second[Name("Rotate")]).Value);
        Assert.Equal(3, Assert.IsType<PdfIndirectReference>(first[Name("Parent")]).ObjectNumber);
    }

    [Fact]
    public void Build_RemovesPagesAndUpdatesTheFlattenedTreeCount()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage(100, 200)
            .AddBlankPage(200, 300)
            .AddBlankPage(300, 400)
            .Build();
        PdfDocument original = PdfDocument.Open(source);
        PdfIndirectReference[] originalPages = FlatPages(original).References;
        var editor = new PdfIncrementalPageEditor(original);

        byte[] result = editor.RemovePage(1).Build();
        PdfDocument reopened = PdfDocument.Open(result);
        (PdfIndirectReference rootReference, PdfIndirectReference[] references, PdfDictionary[] pages) =
            FlatPages(reopened);
        PdfDictionary root = ResolveDictionary(reopened, rootReference);

        Assert.Equal(2, editor.PageCount);
        Assert.Equal(2, Assert.IsType<PdfInteger>(root[Name("Count")]).Value);
        Assert.Equal([originalPages[0].ObjectNumber, originalPages[2].ObjectNumber],
            references.Select(reference => reference.ObjectNumber));
        Assert.Equal([100d, 300d], pages.Select(BoxWidth));
        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
    }

    [Fact]
    public void Build_CanCreateAnEmptyPageTree()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        var editor = new PdfIncrementalPageEditor(PdfDocument.Open(source));

        PdfDocument reopened = PdfDocument.Open(editor.RemovePage(0).Build());
        (_, PdfIndirectReference[] references, _) = FlatPages(reopened);

        Assert.Empty(references);
        Assert.Equal(0, editor.PageCount);
    }

    [Fact]
    public void Build_ChangesMediaAndCropBoxesWithoutRebuildingThePageTree()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage(612, 792).Build();
        PdfDocument original = PdfDocument.Open(source);
        (PdfIndirectReference root, PdfIndirectReference[] references, _) = FlatPages(original);

        byte[] result = new PdfIncrementalPageEditor(original)
            .SetMediaBox(0, -10, -20, 500, 700)
            .SetCropBox(0, 20, 30, 400, 600)
            .Build();
        PdfDocument reopened = PdfDocument.Open(result);
        (PdfIndirectReference reopenedRoot, PdfIndirectReference[] reopenedReferences,
            PdfDictionary[] pages) = FlatPages(reopened);

        Assert.Equal(root.ObjectNumber, reopenedRoot.ObjectNumber);
        Assert.Equal(references[0].ObjectNumber, reopenedReferences[0].ObjectNumber);
        Assert.Equal([-10d, -20d, 490d, 680d], Box(pages[0], "MediaBox"));
        Assert.Equal([20d, 30d, 420d, 630d], Box(pages[0], "CropBox"));
        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
    }

    [Fact]
    public void Build_InsertsBlankPagesWithoutRenumberingExistingPages()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage(100, 200)
            .AddBlankPage(300, 400)
            .Build();
        PdfDocument original = PdfDocument.Open(source);
        PdfIndirectReference[] originalPages = FlatPages(original).References;
        var editor = new PdfIncrementalPageEditor(original);

        byte[] result = editor.InsertBlankPage(1, 500, 600).Build();
        PdfDocument reopened = PdfDocument.Open(result);
        (PdfIndirectReference rootReference, PdfIndirectReference[] references, PdfDictionary[] pages) =
            FlatPages(reopened);
        PdfDictionary root = ResolveDictionary(reopened, rootReference);

        Assert.Equal(3, editor.PageCount);
        Assert.Equal(3, Assert.IsType<PdfInteger>(root[Name("Count")]).Value);
        Assert.Equal(originalPages[0].ObjectNumber, references[0].ObjectNumber);
        Assert.Equal(originalPages[1].ObjectNumber, references[2].ObjectNumber);
        Assert.DoesNotContain(references[1].ObjectNumber,
            originalPages.Select(reference => reference.ObjectNumber));
        Assert.Equal(500, BoxWidth(pages[1]));
        Assert.Equal(rootReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(pages[1][Name("Parent")]).ObjectNumber);
        Assert.Equal("Page", Assert.IsType<PdfName>(pages[1][Name("Type")]).ValueAsLatin1());
        Assert.IsType<PdfDictionary>(pages[1][Name("Resources")]);
        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
    }

    [Fact]
    public void Build_CanAppendToAnInitiallyEmptyPageTree()
    {
        byte[] source = new PdfDocumentBuilder().Build();
        var editor = new PdfIncrementalPageEditor(PdfDocument.Open(source));

        PdfDocument reopened = PdfDocument.Open(editor.AddBlankPage(320, 240).Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(reopened);

        Assert.Single(references);
        Assert.Equal(320, BoxWidth(pages[0]));
    }

    [Fact]
    public void Build_ImportsPageContentResourcesAndAnnotationsWithRemappedReferences()
    {
        PdfImage image = PdfImage.FromRgba(1, 1, new byte[] { 20, 80, 220, 180 });
        byte[] sourceBytes = new PdfDocumentBuilder()
            .AddPage(200, 300, new PdfContentStreamBuilder().DrawImage(image, 20, 30, 160, 240))
            .AddTextNote(0, 150, 250, "Imported note")
            .Build();
        PdfDocument source = PdfDocument.Open(sourceBytes);
        byte[] targetBytes = new PdfDocumentBuilder().AddBlankPage(100, 100).Build();
        PdfDocument target = PdfDocument.Open(targetBytes);
        PdfIndirectReference targetPage = FlatPages(target).References[0];

        byte[] result = new PdfIncrementalPageEditor(target).AddImportedPage(source, 0).Build();
        PdfDocument reopened = PdfDocument.Open(result);
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(reopened);
        PdfDictionary imported = pages[1];
        PdfStream content = ResolveStream(reopened, imported[Name("Contents")]);
        PdfDictionary resources = Assert.IsType<PdfDictionary>(imported[Name("Resources")]);
        PdfDictionary xObjects = Assert.IsType<PdfDictionary>(resources[Name("XObject")]);
        PdfStream importedImage = ResolveStream(reopened, xObjects[Name("Im1")]);
        PdfArray annotations = Assert.IsType<PdfArray>(imported[Name("Annots")]);
        PdfDictionary note = ResolveDictionary(reopened, annotations[0]);

        Assert.Equal(targetPage.ObjectNumber, references[0].ObjectNumber);
        Assert.NotEqual(targetPage.ObjectNumber, references[1].ObjectNumber);
        Assert.True(content.EncodedData.Length > 0);
        Assert.True(importedImage.EncodedData.Length > 0);
        Assert.Equal(references[1].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(note[Name("P")]).ObjectNumber);
        Assert.Equal("Text", Assert.IsType<PdfName>(note[Name("Subtype")]).ValueAsLatin1());
        Assert.IsType<PdfString>(note[Name("Contents")]);
        Assert.True(result.AsSpan(0, targetBytes.Length).SequenceEqual(targetBytes));
    }

    [Fact]
    public void Build_RejectsStaleImportedPageResources()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary stalePage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"),
                new PdfIndirectReference(
                    references[0].ObjectNumber, references[0].Generation + 1))));
        var update = new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, stalePage);
        source = PdfDocument.Open(update.Build());
        PdfDocument target = PdfDocument.Open(new PdfDocumentBuilder().Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources value has an invalid type or resolves to null",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsStaleImportedPageContents()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(200, 300, "q Q"u8.ToArray())
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary stalePage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Contents")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Contents"),
                new PdfIndirectReference(
                    references[0].ObjectNumber, references[0].Generation + 1))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, stalePage)
            .Build());
        PdfDocument target = PdfDocument.Open(new PdfDocumentBuilder().Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Contents value is not a stream or stream array",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsStaleImportedPageResourceEntries()
    {
        PdfImage image = PdfImage.FromRgba(1, 1, new byte[] { 20, 80, 220, 180 });
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(200, 300, new PdfContentStreamBuilder()
                .DrawImage(image, 20, 30, 160, 240))
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary resources = Assert.IsType<PdfDictionary>(pages[0][Name("Resources")]);
        PdfDictionary xObjects = Assert.IsType<PdfDictionary>(resources[Name("XObject")]);
        PdfName imageName = Assert.Single(xObjects.Keys);
        PdfDictionary staleXObjects = new(xObjects
            .Where(entry => !entry.Key.Equals(imageName))
            .Append(new KeyValuePair<PdfName, PdfObject>(imageName,
                new PdfIndirectReference(
                    references[0].ObjectNumber, references[0].Generation + 1))));
        PdfDictionary staleResources = new(resources
            .Where(entry => !entry.Key.Equals(Name("XObject")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("XObject"), staleXObjects)));
        PdfDictionary stalePage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), staleResources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, stalePage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /XObject /Im1 entry resource resolves to null",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMistypedImportedPageResourceEntries()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Subtype"), Name("Image"))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /XObject /Bad entry has an invalid object type",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageXObjects()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference malformedXObject = update.AddObject(new PdfStream(
            new PdfDictionary([new(Name("Type"), Name("XObject"))]), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Bad"), malformedXObject)
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /XObject /Bad entry has no /Subtype value",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsImportedImageColorKeyWithWrongComponentCount()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference image = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Image")),
                new(Name("Width"), new PdfInteger(1)),
                new(Name("Height"), new PdfInteger(1)),
                new(Name("ColorSpace"), Name("DeviceRGB")),
                new(Name("BitsPerComponent"), new PdfInteger(8)),
                new(Name("Mask"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(255)
                ]))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([new(Name("Bad"), image)]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Mask color-key count does not match its color components",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ImportsRichMediaRegistrationsAcrossAliases()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference asset = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Filespec")),
            new(Name("F"), new PdfString("movie.mp4"u8, PdfStringForm.Literal))
        ]));
        PdfIndirectReference assetAlias = update.AddObject(asset);
        PdfIndirectReference instance = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("RichMediaInstance")),
            new(Name("Subtype"), Name("Video")),
            new(Name("Asset"), asset)
        ]));
        PdfIndirectReference configuration = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("RichMediaConfiguration")),
            new(Name("Subtype"), Name("Video")),
            new(Name("Instances"), new PdfArray([instance]))
        ]));
        PdfIndirectReference configurationAlias = update.AddObject(configuration);
        PdfDictionary annotation = new([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("RichMedia")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(100), new PdfInteger(100)
            ])),
            new(Name("RichMediaContent"), new PdfDictionary([
                new(Name("Assets"), new PdfDictionary([
                    new(Name("Names"), new PdfArray([
                        new PdfString("movie.mp4"u8, PdfStringForm.Literal), assetAlias
                    ]))
                ])),
                new(Name("Configurations"), new PdfArray([configurationAlias]))
            ])),
            new(Name("RichMediaSettings"), new PdfDictionary([
                new(Name("Activation"), new PdfDictionary([
                    new(Name("Configuration"), configuration),
                    new(Name("Scripts"), new PdfArray([asset]))
                ]))
            ]))
        ]);
        PdfDocument aliased = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([annotation])))))
            .Build());

        PdfDocument imported = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(aliased, 0)
            .Build());

        (_, _, PdfDictionary[] importedPages) = FlatPages(imported);
        Assert.Single(Assert.IsType<PdfArray>(importedPages[0][Name("Annots")]));
    }

    [Theory]
    [InlineData("1.3", 2.0, false, false, "has no matching numeric /Version")]
    [InlineData("1.3", 1.3, false, false, "has no /F file specification")]
    [InlineData("1.3", 1.3, true, true, "has no eight-number /Position array")]
    [InlineData("2.0", 2.0, true, true, "must define /Size and /CropRect together")]
    [InlineData("1.3", 1.3, true, true, null)]
    [InlineData("2.0", 2.0, true, true, null)]
    public void Build_ValidatesImportedOpiDictionary(
        string versionName, double declaredVersion, bool addFile,
        bool addSize, string? expectedMessage)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        var opiEntries = new List<KeyValuePair<PdfName, PdfObject>>
        {
            new(Name("Type"), Name("OPI")),
            new(Name("Version"), declaredVersion == Math.Truncate(declaredVersion)
                ? new PdfInteger((long)declaredVersion)
                : new PdfReal(declaredVersion))
        };
        if (addFile)
            opiEntries.Add(new(Name("F"),
                new PdfString("original.tif"u8, PdfStringForm.Literal)));
        if (addSize)
        {
            opiEntries.Add(new(Name("Size"), new PdfArray([
                new PdfInteger(10), new PdfInteger(10)
            ])));
            if (versionName == "1.3")
                opiEntries.Add(new(Name("CropRect"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])));
            if (expectedMessage is null && versionName == "1.3")
                opiEntries.Add(new(Name("Position"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(0), new PdfInteger(10),
                    new PdfInteger(10), new PdfInteger(10),
                    new PdfInteger(10), new PdfInteger(0)
                ])));
            if (expectedMessage is null && versionName == "2.0")
                opiEntries.Add(new(Name("CropRect"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])));
        }
        PdfIndirectReference image = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Image")),
                new(Name("Width"), new PdfInteger(10)),
                new(Name("Height"), new PdfInteger(10)),
                new(Name("ColorSpace"), Name("DeviceRGB")),
                new(Name("BitsPerComponent"), new PdfInteger(8)),
                new(Name("OPI"), new PdfDictionary([
                    new(Name(versionName), new PdfDictionary(opiEntries))
                ]))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Bad"), image)
            ]))
        ]);
        update.ReplaceObject(references[0].ObjectNumber,
            new PdfDictionary(pages[0]
                .Where(entry => !entry.Key.Equals(Name("Resources")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources))));
        source = PdfDocument.Open(update.Build());

        var editor = new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0);
        if (expectedMessage is null)
        {
            _ = editor.Build();
            return;
        }
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => editor.Build());
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_ValidatesImportedReferenceXObject(bool valid)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var referenceEntries = new List<KeyValuePair<PdfName, PdfObject>>
        {
            new(Name("Page"), new PdfInteger(0))
        };
        if (valid)
        {
            referenceEntries.Add(new(Name("F"),
                new PdfString("external.pdf"u8, PdfStringForm.Literal)));
            referenceEntries.Add(new(Name("ID"), new PdfArray([
                new PdfString("permanent"u8, PdfStringForm.Hexadecimal),
                new PdfString("revision"u8, PdfStringForm.Hexadecimal)
            ])));
        }
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference form = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Form")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])),
                new(Name("Ref"), new PdfDictionary(referenceEntries))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Reference"), form)
            ]))
        ]);
        update.ReplaceObject(references[0].ObjectNumber,
            new PdfDictionary(pages[0]
                .Where(entry => !entry.Key.Equals(Name("Resources")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources))));
        source = PdfDocument.Open(update.Build());
        var editor = new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0);

        if (valid)
        {
            _ = editor.Build();
            return;
        }
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => editor.Build());
        Assert.Contains("/Ref dictionary has no /F file specification",
            error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Metadata", "/Metadata value is not an indirect stream reference")]
    [InlineData("PtData", "/PtData requires a geospatial /Measure dictionary")]
    [InlineData("Alternates", "alternate image has no /Image stream")]
    [InlineData("EmptyAlternates", "/Alternates array is empty")]
    [InlineData("Name", "/Name value is not a name")]
    [InlineData("FormType", "/FormType value is not 1")]
    public void Build_RejectsMalformedImportedXObjectAuxiliaryGraph(
        string key, string expectedMessage)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        string actualKey = key == "EmptyAlternates" ? "Alternates" : key;
        PdfObject malformedValue = key switch
        {
            "Metadata" => new PdfInteger(1),
            "Name" => new PdfInteger(1),
            "FormType" => new PdfInteger(2),
            "PtData" => new PdfDictionary([
                new(Name("Type"), Name("PtData")),
                new(Name("Subtype"), Name("Cloud")),
                new(Name("Names"), new PdfArray([Name("LAT")])),
                new(Name("XPTS"), new PdfArray([
                    new PdfArray([new PdfInteger(1)])
                ]))
            ]),
            "EmptyAlternates" => new PdfArray([]),
            _ => new PdfArray([new PdfDictionary([])])
        };
        var update = new PdfIncrementalUpdateBuilder(source);
        var xObjectEntries = new List<KeyValuePair<PdfName, PdfObject>>
        {
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name(key == "FormType" ? "Form" : "Image")),
                new(Name(actualKey), malformedValue)
        };
        if (key == "FormType")
            xObjectEntries.Add(new(Name("BBox"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])));
        else
            xObjectEntries.AddRange([
                new(Name("Width"), new PdfInteger(10)),
                new(Name("Height"), new PdfInteger(10)),
                new(Name("ColorSpace"), Name("DeviceRGB")),
                new(Name("BitsPerComponent"), new PdfInteger(8))
            ]);
        PdfIndirectReference image = update.AddObject(new PdfStream(
            new PdfDictionary(xObjectEntries), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Bad"), image)
            ]))
        ]);
        update.ReplaceObject(references[0].ObjectNumber,
            new PdfDictionary(pages[0]
                .Where(entry => !entry.Key.Equals(Name("Resources")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources))));
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0).Build());
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Build_ValidatesImportedPostScriptXObjectFallback(
        bool valid, bool legacySubtype)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfObject fallback = valid
            ? update.AddObject(new PdfStream(new PdfDictionary([]), []))
            : new PdfInteger(1);
        PdfIndirectReference postScript = update.AddObject(new PdfStream(
            new PdfDictionary(new[]
            {
                new KeyValuePair<PdfName, PdfObject>(Name("Type"), Name("XObject")),
                new KeyValuePair<PdfName, PdfObject>(
                    Name("Subtype"), Name(legacySubtype ? "Form" : "PS")),
                new KeyValuePair<PdfName, PdfObject>(Name("Level1"), fallback)
            }.Concat(legacySubtype
                ? [new KeyValuePair<PdfName, PdfObject>(Name("Subtype2"), Name("PS"))]
                : [])), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("PostScript"), postScript)
            ]))
        ]);
        update.ReplaceObject(references[0].ObjectNumber,
            new PdfDictionary(pages[0]
                .Where(entry => !entry.Key.Equals(Name("Resources")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources))));
        source = PdfDocument.Open(update.Build());
        var editor = new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0);

        if (valid)
        {
            _ = editor.Build();
            return;
        }
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => editor.Build());
        Assert.Contains("/Level1 value is not an indirect stream reference",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedImageOptions()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference image = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Image")),
                new(Name("Width"), new PdfInteger(10)),
                new(Name("Height"), new PdfInteger(10)),
                new(Name("ColorSpace"), Name("DeviceRGB")),
                new(Name("BitsPerComponent"), new PdfInteger(8)),
                new(Name("Decode"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(1)
                ]))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Bad"), image)
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /XObject /Bad entry /Decode count does not match its color components",
            error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, 9, "/SMask dimensions do not match the image")]
    [InlineData(true, 10, "/SMask has no /DeviceGray color space")]
    public void Build_RejectsMalformedImportedImageSoftMasks(
        bool omitColorSpace, int maskWidth, string expectedMessage)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        var softMaskEntries = new List<KeyValuePair<PdfName, PdfObject>>([
            new(Name("Type"), Name("XObject")),
            new(Name("Subtype"), Name("Image")),
            new(Name("Width"), new PdfInteger(maskWidth)),
            new(Name("Height"), new PdfInteger(10)),
            new(Name("BitsPerComponent"), new PdfInteger(8))
        ]);
        if (!omitColorSpace)
            softMaskEntries.Add(new(Name("ColorSpace"), Name("DeviceGray")));
        PdfIndirectReference softMask = update.AddObject(new PdfStream(
            new PdfDictionary(softMaskEntries), []));
        PdfIndirectReference image = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Image")),
                new(Name("Width"), new PdfInteger(10)),
                new(Name("Height"), new PdfInteger(10)),
                new(Name("ColorSpace"), Name("DeviceRGB")),
                new(Name("BitsPerComponent"), new PdfInteger(8)),
                new(Name("SMask"), softMask)
            ]), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Bad"), image)
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedFormGroups()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference form = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Form")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])),
                new(Name("Group"), new PdfDictionary([
                    new(Name("Type"), Name("Group"))
                ]))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Bad"), form)
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /XObject /Bad entry /Group has no /S /Transparency value",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedNestedFormResources()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference form = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Form")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])),
                new(Name("Resources"), new PdfDictionary([
                    new(Name("Font"), new PdfDictionary([
                        new(Name("Broken"), new PdfInteger(7))
                    ]))
                ]))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("XObject"), new PdfDictionary([
                new(Name("Form"), form)
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /XObject /Form entry /Resources /Font /Broken entry has an invalid object type",
            error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "/Resources /Font /Bad entry has no /Subtype value")]
    [InlineData(true, "/Resources /Font /Bad entry has no /BaseFont name")]
    public void Build_RejectsMalformedImportedPageFonts(
        bool includeSubtype, string expectedMessage)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Font"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary(includeSubtype
                    ? [new(Name("Type"), Name("Font")),
                        new(Name("Subtype"), Name("Type1"))]
                    : [new(Name("Type"), Name("Font"))]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedFontWidths()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Font"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("Font")),
                    new(Name("Subtype"), Name("Type1")),
                    new(Name("BaseFont"), Name("Helvetica")),
                    new(Name("FirstChar"), new PdfInteger(32)),
                    new(Name("LastChar"), new PdfInteger(33)),
                    new(Name("Widths"), new PdfArray([new PdfInteger(500)]))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("has inconsistent /FirstChar, /LastChar, or /Widths values",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedFontEncodingDifferences()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Font"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("Font")),
                    new(Name("Subtype"), Name("Type1")),
                    new(Name("BaseFont"), Name("Helvetica")),
                    new(Name("Encoding"), new PdfDictionary([
                        new(Name("Type"), Name("Encoding")),
                        new(Name("Differences"), new PdfArray([Name("A")]))
                    ]))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Encoding /Differences glyph name has no valid preceding code",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsIncompleteImportedFontDescriptors()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Font"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("Font")),
                    new(Name("Subtype"), Name("Type1")),
                    new(Name("BaseFont"), Name("Example")),
                    new(Name("FontDescriptor"), new PdfDictionary([
                        new(Name("Type"), Name("FontDescriptor")),
                        new(Name("FontName"), Name("Example")),
                        new(Name("Flags"), new PdfInteger(32))
                    ]))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/FontDescriptor value has no four-number /FontBBox array",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsIncompleteImportedType3Fonts()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Font"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("Font")),
                    new(Name("Subtype"), Name("Type3")),
                    new(Name("Encoding"), Name("WinAnsiEncoding")),
                    new(Name("FontBBox"), new PdfArray([
                        new PdfInteger(0), new PdfInteger(0),
                        new PdfInteger(500), new PdfInteger(700)
                    ])),
                    new(Name("FontMatrix"), new PdfArray([
                        new PdfReal(0.001), new PdfInteger(0), new PdfInteger(0),
                        new PdfReal(0.001), new PdfInteger(0), new PdfInteger(0)
                    ])),
                    new(Name("CharProcs"), new PdfDictionary([]))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("has no complete character-width range",
            error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("W", "range or width")]
    [InlineData("W2", "vertical-metrics array")]
    [InlineData("CIDToGIDMap", "name is not /Identity")]
    public void Build_RejectsMalformedImportedCidFontMetrics(
        string key, string expectedMessage)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Font"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("Font")),
                    new(Name("Subtype"), Name("CIDFontType2")),
                    new(Name("BaseFont"), Name("Example")),
                    new(Name("CIDSystemInfo"), new PdfDictionary([
                        new(Name("Registry"), new PdfString("Adobe"u8, PdfStringForm.Literal)),
                        new(Name("Ordering"), new PdfString("Identity"u8, PdfStringForm.Literal)),
                        new(Name("Supplement"), new PdfInteger(0))
                    ])),
                    new(Name(key), key switch
                    {
                        "W" => new PdfArray([
                            new PdfInteger(1), new PdfInteger(3)
                        ]),
                        "W2" => new PdfArray([
                            new PdfInteger(1), new PdfArray([
                                new PdfInteger(100), new PdfInteger(0)
                            ])
                        ]),
                        _ => Name("Unexpected")
                    })
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageGraphicsStates()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("ExtGState"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("ExtGState")),
                    new(Name("CA"), new PdfReal(1.5))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /ExtGState /Bad entry /CA value is outside 0 through 1",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsImportedHalftoneWithoutDefinedType()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("ExtGState"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("ExtGState")),
                    new(Name("HT"), new PdfDictionary([]))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/HT dictionary has no defined /HalftoneType integer",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AllowsSharedImportedTypeFiveSubHalftone()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference componentHalftone = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Halftone")),
            new(Name("HalftoneType"), new PdfInteger(1)),
            new(Name("Frequency"), new PdfInteger(60)),
            new(Name("Angle"), new PdfInteger(45)),
            new(Name("SpotFunction"), Name("SimpleDot"))
        ]));
        var resources = new PdfDictionary([
            new(Name("ExtGState"), new PdfDictionary([
                new(Name("Good"), new PdfDictionary([
                    new(Name("Type"), Name("ExtGState")),
                    new(Name("HT"), new PdfDictionary([
                        new(Name("Type"), Name("Halftone")),
                        new(Name("HalftoneType"), new PdfInteger(5)),
                        new(Name("Default"), componentHalftone),
                        new(Name("Cyan"), componentHalftone)
                    ]))
                ]))
            ]))
        ]);
        PdfDictionary page = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, page);
        source = PdfDocument.Open(update.Build());

        _ = new PdfIncrementalPageEditor(PdfDocument.Open(
                new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build();
    }

    [Fact]
    public void Build_RejectsMalformedImportedGraphicsStateTransferFunctions()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("ExtGState"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("ExtGState")),
                    new(Name("TR2"), Name("Unexpected"))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /ExtGState /Bad entry /TR2 name /Unexpected is not defined",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageShadings()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Shading"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("ShadingType"), new PdfInteger(9)),
                    new(Name("ColorSpace"), Name("DeviceRGB"))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /Shading /Bad entry has no defined /ShadingType integer",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsUndefinedNameValuedColorSpaceResource()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), Name("Unexpected"))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/ColorSpace /Bad entry name /Unexpected is not a direct color space",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsImportedShadingBackgroundWithWrongComponentCount()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Shading"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("ShadingType"), new PdfInteger(2)),
                    new(Name("ColorSpace"), Name("DeviceRGB")),
                    new(Name("Background"), new PdfArray([new PdfInteger(1)]))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Background count does not match its color space",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedMeshShadings()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference shading = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("ShadingType"), new PdfInteger(4)),
                new(Name("ColorSpace"), Name("DeviceRGB"))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("Shading"), new PdfDictionary([
                new(Name("Bad"), shading)
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /Shading /Bad entry has no supported /BitsPerCoordinate value",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPagePatterns()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Pattern"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("Pattern")),
                    new(Name("PatternType"), new PdfInteger(9))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /Pattern /Bad entry has no defined /PatternType integer",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedNestedPatternResources()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference pattern = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("Pattern")),
                new(Name("PatternType"), new PdfInteger(1)),
                new(Name("PaintType"), new PdfInteger(1)),
                new(Name("TilingType"), new PdfInteger(1)),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])),
                new(Name("XStep"), new PdfInteger(10)),
                new(Name("YStep"), new PdfInteger(10)),
                new(Name("Resources"), new PdfDictionary([
                    new(Name("ColorSpace"), new PdfDictionary([
                        new(Name("Broken"), new PdfInteger(1))
                    ]))
                ]))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("Pattern"), new PdfDictionary([
                new(Name("Tile"), pattern)
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /Pattern /Tile entry /Resources /ColorSpace /Broken entry has an invalid object type",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageColorSpaces()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), new PdfArray([Name("CalRGB")]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /ColorSpace /Bad entry /CalRGB color space has an invalid element count",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsSpecialColorSpaceAsUncoloredPatternBase()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), new PdfArray([
                    Name("Pattern"), new PdfArray([Name("Pattern")])
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Pattern base is not a device or CIE-based color space",
            error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "/Separation tint function has no positive /N exponent")]
    [InlineData(true, "/Separation tint function input dimension does not match its caller")]
    public void Build_RejectsMalformedImportedPageColorFunctions(
        bool mismatchedDomain, string expectedMessage)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var tintFunction = new PdfDictionary([
            new(Name("FunctionType"), new PdfInteger(2)),
            new(Name("Domain"), mismatchedDomain
                ? new PdfArray([
                    new PdfInteger(0), new PdfInteger(1),
                    new PdfInteger(0), new PdfInteger(1)
                ])
                : new PdfArray([new PdfInteger(0), new PdfInteger(1)])),
            new(Name("N"), new PdfInteger(0))
        ]);
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), new PdfArray([
                    Name("Separation"), Name("Spot"), Name("DeviceCMYK"), tintFunction
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedIccColorSpaces()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference profile = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("N"), new PdfInteger(3)),
                new(Name("Range"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(1)
                ]))
            ]), []));
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), new PdfArray([Name("ICCBased"), profile]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        update.ReplaceObject(references[0].ObjectNumber, invalidPage);
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/ICCBased profile /Range has an invalid component count or value",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedCalibratedColorSpaces()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), new PdfArray([
                    Name("CalRGB"), new PdfDictionary([])
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/CalRGB has no /WhitePoint array",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedIndexedLookupLengths()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), new PdfArray([
                    Name("Indexed"), Name("DeviceRGB"), new PdfInteger(1),
                    new PdfString([0, 0, 0], PdfStringForm.Hexadecimal)
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Indexed lookup length does not match its palette size",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedDeviceNAttributes()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var tint = new PdfDictionary([
            new(Name("FunctionType"), new PdfInteger(2)),
            new(Name("Domain"), new PdfArray([
                new PdfInteger(0), new PdfInteger(1)
            ])),
            new(Name("C0"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(0), new PdfInteger(0)
            ])),
            new(Name("C1"), new PdfArray([
                new PdfInteger(1), new PdfInteger(0),
                new PdfInteger(0), new PdfInteger(0)
            ])),
            new(Name("N"), new PdfInteger(1))
        ]);
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), new PdfArray([
                    Name("DeviceN"), new PdfArray([Name("Cyan")]),
                    Name("DeviceCMYK"), tint,
                    new PdfDictionary([
                        new(Name("Subtype"), Name("Wrong"))
                    ])
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/DeviceN attributes /Subtype /Wrong is not defined",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDuplicateImportedDeviceNColorants()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var tint = new PdfDictionary([
            new(Name("FunctionType"), new PdfInteger(2)),
            new(Name("Domain"), new PdfArray([
                new PdfInteger(0), new PdfInteger(1)
            ])),
            new(Name("C0"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(0), new PdfInteger(0)
            ])),
            new(Name("C1"), new PdfArray([
                new PdfInteger(1), new PdfInteger(0),
                new PdfInteger(0), new PdfInteger(0)
            ])),
            new(Name("N"), new PdfInteger(1))
        ]);
        var resources = new PdfDictionary([
            new(Name("ColorSpace"), new PdfDictionary([
                new(Name("Bad"), new PdfArray([
                    Name("DeviceN"), new PdfArray([Name("Cyan"), Name("Cyan")]),
                    Name("DeviceCMYK"), tint
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/DeviceN colorants contain duplicate names",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageProperties()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Properties"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("OCMD")),
                    new(Name("P"), Name("Sometimes"))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Resources /Properties /Bad entry OCMD /P value /Sometimes is not defined",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedOptionalContentUsage()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var resources = new PdfDictionary([
            new(Name("Properties"), new PdfDictionary([
                new(Name("Bad"), new PdfDictionary([
                    new(Name("Type"), Name("OCG")),
                    new(Name("Name"), new PdfString("Layer"u8, PdfStringForm.Literal)),
                    new(Name("Usage"), new PdfDictionary([
                        new(Name("View"), new PdfDictionary([
                            new(Name("ViewState"), Name("Maybe"))
                        ]))
                    ]))
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), resources)));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Usage /View /ViewState value /Maybe is not defined",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageBoxes()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidPage = new(pages[0]
            .Where(entry => !entry.Key.Equals(Name("MediaBox")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("MediaBox"),
                new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfInteger(200)]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/MediaBox value is not a four-number rectangle",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageProductionBoxes()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("ArtBox"),
                new PdfArray([new PdfInteger(0), new PdfInteger(0)]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/ArtBox value is not a four-number rectangle",
            error.Message, StringComparison.Ordinal);

        PdfDocument collapsedSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] collapsedReferences, PdfDictionary[] collapsedPages) =
            FlatPages(collapsedSource);
        PdfDictionary collapsedPage = new(collapsedPages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("ArtBox"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(0), new PdfInteger(10)
            ]))));
        collapsedSource = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(collapsedSource)
                .ReplaceObject(collapsedReferences[0].ObjectNumber, collapsedPage)
                .Build());
        InvalidOperationException collapsedError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(collapsedSource, 0)
                .Build());
        Assert.Contains("/ArtBox value is a collapsed rectangle",
            collapsedError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageBeads()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("B"),
                new PdfArray([new PdfInteger(1)]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/B value entry is not an indirect bead reference",
            error.Message, StringComparison.Ordinal);

        PdfDocument emptyBeadsSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] emptyBeadPages, PdfDictionary[] emptyBeadDictionaries) =
            FlatPages(emptyBeadsSource);
        PdfDictionary emptyBeadPage = new(emptyBeadDictionaries[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("B"), new PdfArray([]))));
        emptyBeadsSource = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(emptyBeadsSource)
                .ReplaceObject(emptyBeadPages[0].ObjectNumber, emptyBeadPage)
                .Build());
        InvalidOperationException emptyBeadsError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(emptyBeadsSource, 0)
                    .Build());
        Assert.Contains("/B value array is empty",
            emptyBeadsError.Message, StringComparison.Ordinal);

        PdfDocument linkedSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] linkedPages, PdfDictionary[] linkedPageDictionaries) =
            FlatPages(linkedSource);
        var linkedUpdate = new PdfIncrementalUpdateBuilder(linkedSource);
        int nextObjectNumber = checked((int)Assert.IsType<PdfInteger>(
            linkedSource.Trailer[Name("Size")]).Value);
        var thread = new PdfIndirectReference(nextObjectNumber, 0);
        var first = new PdfIndirectReference(nextObjectNumber + 1, 0);
        var second = new PdfIndirectReference(nextObjectNumber + 2, 0);
        PdfArray rectangle = new([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(10), new PdfInteger(10)
        ]);
        linkedUpdate.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Thread")),
            new(Name("F"), first)
        ]));
        linkedUpdate.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Bead")),
            new(Name("T"), thread), new(Name("N"), second),
            new(Name("V"), second), new(Name("P"), linkedPages[0]),
            new(Name("R"), rectangle)
        ]));
        linkedUpdate.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Bead")),
            new(Name("T"), thread), new(Name("N"), first),
            new(Name("V"), second), new(Name("P"), linkedPages[0]),
            new(Name("R"), rectangle)
        ]));
        linkedUpdate.ReplaceObject(linkedPages[0].ObjectNumber,
            new PdfDictionary(linkedPageDictionaries[0].Append(
                new KeyValuePair<PdfName, PdfObject>(Name("B"),
                    new PdfArray([first])))));
        linkedSource = PdfDocument.Open(linkedUpdate.Build());

        InvalidOperationException linkageError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(linkedSource, 0)
                .Build());
        Assert.Contains("bead ring has inconsistent /N and /V links",
            linkageError.Message, StringComparison.Ordinal);

        var validRingUpdate = new PdfIncrementalUpdateBuilder(linkedSource);
        PdfIndirectReference threadAlias = validRingUpdate.AddObject(thread);
        PdfIndirectReference firstAlias = validRingUpdate.AddObject(first);
        PdfIndirectReference secondAlias = validRingUpdate.AddObject(second);
        validRingUpdate.ReplaceObject(first.ObjectNumber, new PdfDictionary([
            new(Name("Type"), Name("Bead")),
            new(Name("T"), threadAlias), new(Name("N"), secondAlias),
            new(Name("V"), secondAlias), new(Name("P"), linkedPages[0]),
            new(Name("R"), rectangle)
        ]));
        validRingUpdate.ReplaceObject(second.ObjectNumber, new PdfDictionary([
            new(Name("Type"), Name("Bead")),
            new(Name("T"), threadAlias), new(Name("N"), firstAlias),
            new(Name("V"), firstAlias), new(Name("P"), linkedPages[0]),
            new(Name("R"), rectangle)
        ]));
        validRingUpdate.ReplaceObject(linkedPages[0].ObjectNumber,
            new PdfDictionary(linkedPageDictionaries[0].Append(
                new KeyValuePair<PdfName, PdfObject>(Name("B"),
                    new PdfArray([firstAlias, secondAlias])))));
        PdfDocument validRingSource = PdfDocument.Open(validRingUpdate.Build());
        _ = new PdfIncrementalPageEditor(PdfDocument.Open(
                new PdfDocumentBuilder().Build()))
            .AddImportedPage(validRingSource, 0)
            .Build();
    }

    [Fact]
    public void Build_RejectsMalformedImportedPagePieceInfo()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("PieceInfo"),
                new PdfDictionary([
                    new(Name("App"), new PdfDictionary([]))
                ]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/PieceInfo value /App entry has no string /LastModified value",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageOutputIntents()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("OutputIntents"),
                new PdfArray([new PdfDictionary([
                    new(Name("Type"), Name("OutputIntent"))
                ])]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("page /OutputIntents entry has no valid /S name",
            error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("D:20251301000000Z")]
    [InlineData("D:2026Z")]
    [InlineData("D:20260824120000+0700")]
    public void Build_RejectsMalformedImportedPdfDates(string date)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("LastModified"),
                new PdfString(Encoding.ASCII.GetBytes(date), PdfStringForm.Literal))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/LastModified value is not a valid PDF date string",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageSeparationInfo()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfDocument original = source;
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("SeparationInfo"),
                new PdfDictionary([]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/SeparationInfo value has no nonempty /Pages array",
            error.Message, StringComparison.Ordinal);

        PdfDictionary directPageSeparation = new([
            new(Name("Pages"), new PdfArray([pages[0]])),
            new(Name("DeviceColorant"), Name("Cyan")),
            new(Name("ColorSpace"), Name("DeviceCMYK"))
        ]);
        PdfDictionary directPageValue = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(
                Name("SeparationInfo"), directPageSeparation)));
        PdfDocument directPageSource = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(original)
                .ReplaceObject(references[0].ObjectNumber, directPageValue)
                .Build());
        InvalidOperationException directPageError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(directPageSource, 0)
                    .Build());
        Assert.Contains("/SeparationInfo value /Pages entry is not an indirect page dictionary",
            directPageError.Message, StringComparison.Ordinal);

        PdfDictionary deviceColorSpaceSeparation = new([
            new(Name("Pages"), new PdfArray([references[0]])),
            new(Name("DeviceColorant"), Name("Cyan")),
            new(Name("ColorSpace"), Name("DeviceCMYK"))
        ]);
        PdfDictionary deviceColorSpacePage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(
                Name("SeparationInfo"), deviceColorSpaceSeparation)));
        PdfDocument deviceColorSpaceSource = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(original)
                .ReplaceObject(references[0].ObjectNumber, deviceColorSpacePage)
                .Build());
        InvalidOperationException deviceColorSpaceError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(deviceColorSpaceSource, 0)
                    .Build());
        Assert.Contains("/ColorSpace is not a Separation or DeviceN color space",
            deviceColorSpaceError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsStaleImportedPageDictionaryValues()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary stalePage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Metadata"),
                new PdfIndirectReference(
                    references[0].ObjectNumber, references[0].Generation + 1))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, stalePage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/Metadata value is not a stream or resolves to null",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsInvalidImportedPageMetadataAndThumbnails()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(original);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        PdfDocument invalidMetadata = WithStream("Metadata", new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("Wrong")),
                new(Name("Subtype"), Name("XML"))
            ]), "<x:xmpmeta/>"u8));
        InvalidOperationException metadataError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidMetadata, 0)
                .Build());
        Assert.Contains("page /Metadata value has an invalid /Type value",
            metadataError.Message, StringComparison.Ordinal);

        PdfDocument invalidThumbnail = WithStream("Thumb", new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Width"), new PdfInteger(10)),
                new(Name("Height"), new PdfInteger(10))
            ]), []));
        InvalidOperationException thumbnailError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidThumbnail, 0)
                .Build());
        Assert.Contains("/Thumb value has no valid /Subtype /Image value",
            thumbnailError.Message, StringComparison.Ordinal);
        return;

        PdfDocument WithStream(string key, PdfStream stream)
        {
            var update = new PdfIncrementalUpdateBuilder(original);
            PdfIndirectReference streamReference = update.AddObject(stream);
            PdfName name = Name(key);
            PdfDictionary page = new(pages[0]
                .Where(entry => !entry.Key.Equals(name))
                .Append(new KeyValuePair<PdfName, PdfObject>(name, streamReference)));
            update.ReplaceObject(references[0].ObjectNumber, page);
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void Build_RejectsInvalidImportedPageAdditionalActions()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("AA"), new PdfDictionary([
                new(Name("O"), new PdfDictionary([
                    new(Name("Type"), Name("Action"))
                ]))
            ]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("imported page /AA value /O entry has no valid /S name",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDuplicateImportedPageAnnotationReferences()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference annotation = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[0])
        ]));
        PdfDocument malformed = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([annotation, annotation])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(malformed, 0)
                .Build());

        Assert.Contains("/Annots array contains a duplicate annotation reference",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsImportedAnnotationOwnedByAnotherPage()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference annotation = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[1])
        ]));
        PdfDocument malformed = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([annotation])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(malformed, 0)
                .Build());

        Assert.Contains("/P value identifies a different page",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsImportedPopupWithMismatchedParent()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference otherMarkup = update.ReserveObject();
        PdfIndirectReference popup = update.ReserveObject();
        PdfIndirectReference markup = update.ReserveObject();
        PdfDictionary Rectangle() => new([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[0])
        ]);
        update.SetObject(otherMarkup, Rectangle());
        update.SetObject(popup, new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Popup")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[0]),
            new(Name("Parent"), otherMarkup)
        ]));
        update.SetObject(markup, new PdfDictionary(Rectangle().Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Popup"), popup))));
        PdfDocument malformed = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([markup, popup, otherMarkup])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(malformed, 0)
                .Build());

        Assert.Contains("/Popup target does not link back through /Parent",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ImportsAliasedAnnotationOwnershipRepliesAndPopupLinks()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference pageAlias = update.AddObject(references[0]);
        PdfIndirectReference popup = update.ReserveObject();
        PdfIndirectReference markup = update.ReserveObject();
        PdfIndirectReference popupAlias = update.AddObject(popup);
        PdfIndirectReference markupAlias = update.AddObject(markup);
        update.SetObject(popup, new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Popup")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), pageAlias),
            new(Name("Parent"), markupAlias)
        ]));
        update.SetObject(markup, new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), pageAlias),
            new(Name("Popup"), popupAlias),
            new(Name("IRT"), popupAlias)
        ]));
        PdfDocument aliased = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([markupAlias, popupAlias])))))
            .Build());

        PdfDocument imported = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(aliased, 0)
            .Build());

        (_, _, PdfDictionary[] importedPages) = FlatPages(imported);
        Assert.Equal(2, Assert.IsType<PdfArray>(importedPages[0][Name("Annots")]).Count);
    }

    [Fact]
    public void Build_RejectsImportedPopupWhoseParentDoesNotLinkBack()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference markup = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[0])
        ]));
        PdfIndirectReference popup = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Popup")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[0]),
            new(Name("Parent"), markup)
        ]));
        PdfDocument malformed = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([popup, markup])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(malformed, 0)
                .Build());

        Assert.Contains("popup /Parent does not link back through /Popup",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsImportedReplyTargetNotRegisteredOnPage()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference target = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[1])
        ]));
        PdfIndirectReference reply = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[0]),
            new(Name("IRT"), target)
        ]));
        PdfDocument malformed = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([reply])))))
            .ReplaceObject(references[1].ObjectNumber,
                new PdfDictionary(pages[1].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([target])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(malformed, 0)
                .Build());

        Assert.Contains("/IRT target is not registered on the imported page",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDuplicateImportedAnnotationNames()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfDictionary Annotation() => new([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[0]),
            new(Name("NM"), new PdfString("duplicate"u8, PdfStringForm.Literal))
        ]);
        PdfIndirectReference first = update.AddObject(Annotation());
        PdfIndirectReference second = update.AddObject(Annotation());
        PdfDocument malformed = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([first, second])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(malformed, 0)
                .Build());

        Assert.Contains("/NM value is not unique on the imported page",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedAnnotationText()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference annotation = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), references[0]),
            new(Name("Contents"), new PdfString(
                new byte[] { 0xFE, 0xFF, 0xD8, 0x00 }, PdfStringForm.Hexadecimal))
        ]));
        PdfDocument malformed = PdfDocument.Open(update
            .ReplaceObject(references[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([annotation])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(malformed, 0)
                .Build());

        Assert.Contains("/Contents value contains malformed UTF-16BE text",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsInvalidImportedAnnotationActionsAndDestinations()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(original);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        PdfDocument invalidAction = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Link")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("A"), new PdfDictionary([
                new(Name("Type"), Name("Action"))
            ]))
        ]));
        InvalidOperationException actionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidAction, 0)
                .Build());
        Assert.Contains("/Annots entry /A value has no valid /S name",
            actionError.Message, StringComparison.Ordinal);

        PdfDocument invalidDestination = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Link")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("Dest"), new PdfArray([
                references[0], Name("FitR"), new PdfInteger(0)
            ]))
        ]));
        InvalidOperationException destinationError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidDestination, 0)
                .Build());
        Assert.Contains("/Annots entry /Dest value /FitR has an invalid operand count",
            destinationError.Message, StringComparison.Ordinal);

        PdfDocument invalidRectangle = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(10)
            ]))
        ]));
        InvalidOperationException rectangleError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidRectangle, 0)
                .Build());
        Assert.Contains("/Annots entry has no four-number /Rect array",
            rectangleError.Message, StringComparison.Ordinal);

        PdfDocument invalidAppearance = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("AP"), new PdfDictionary([
                new(Name("N"), new PdfInteger(17))
            ]))
        ]));
        InvalidOperationException appearanceError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidAppearance, 0)
                .Build());
        Assert.Contains("/AP /N value is not an appearance stream or state dictionary",
            appearanceError.Message, StringComparison.Ordinal);

        PdfDocument invalidColor = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("C"), new PdfArray([
                new PdfReal(1.5), new PdfInteger(0), new PdfInteger(0)
            ]))
        ]));
        InvalidOperationException colorError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidColor, 0)
                .Build());
        Assert.Contains("/C value is not a valid annotation color array",
            colorError.Message, StringComparison.Ordinal);

        PdfDocument invalidHighlight = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Link")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("H"), Name("Blink"))
        ]));
        InvalidOperationException highlightError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidHighlight, 0)
                .Build());
        Assert.Contains("link /H value /Blink is not defined",
            highlightError.Message, StringComparison.Ordinal);

        PdfDocument invalidRemoteAction = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Link")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("A"), new PdfDictionary([
                new(Name("Type"), Name("Action")),
                new(Name("S"), Name("GoToR")),
                new(Name("D"), new PdfString("Chapter"u8, PdfStringForm.Literal))
            ]))
        ]));
        InvalidOperationException remoteActionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidRemoteAction, 0)
                .Build());
        Assert.Contains("/GoToR action has no /F file specification",
            remoteActionError.Message, StringComparison.Ordinal);

        PdfDocument invalidLayerAction = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Link")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("A"), new PdfDictionary([
                new(Name("Type"), Name("Action")),
                new(Name("S"), Name("SetOCGState")),
                new(Name("State"), new PdfArray([new PdfInteger(1)]))
            ]))
        ]));
        InvalidOperationException layerActionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidLayerAction, 0)
                .Build());
        Assert.Contains("/State operand is not an OCG following a state operator",
            layerActionError.Message, StringComparison.Ordinal);

        PdfDocument invalidThreeDimensionalAction = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Link")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("A"), new PdfDictionary([
                new(Name("Type"), Name("Action")),
                new(Name("S"), Name("GoTo3DView")),
                new(Name("V"), Name("Next"))
            ]))
        ]));
        InvalidOperationException threeDimensionalActionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidThreeDimensionalAction, 0)
                .Build());
        Assert.Contains("/GoTo3DView action has no /TA annotation dictionary",
            threeDimensionalActionError.Message, StringComparison.Ordinal);

        PdfDocument invalidSoundAction = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Link")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("A"), new PdfDictionary([
                new(Name("Type"), Name("Action")),
                new(Name("S"), Name("Sound"))
            ]))
        ]));
        InvalidOperationException soundActionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidSoundAction, 0)
                .Build());
        Assert.Contains("/Sound action has no /Sound stream",
            soundActionError.Message, StringComparison.Ordinal);

        PdfDocument invalidFlags = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("F"), new PdfInteger(-1))
        ]));
        InvalidOperationException flagError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidFlags, 0)
                .Build());
        Assert.Contains("/F value is not a nonnegative integer",
            flagError.Message, StringComparison.Ordinal);

        PdfDocument invalidLanguage = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("Lang"), new PdfString("not_valid"u8, PdfStringForm.Literal))
        ]));
        InvalidOperationException languageError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidLanguage, 0)
                .Build());
        Assert.Contains("/Lang value is not a valid BCP 47 language tag",
            languageError.Message, StringComparison.Ordinal);

        PdfDocument directReply = WithAnnotation(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("IRT"), new PdfDictionary([
                new(Name("Type"), Name("Annot")),
                new(Name("Subtype"), Name("Text"))
            ]))
        ]));
        InvalidOperationException replyError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(directReply, 0)
                .Build());
        Assert.Contains("/IRT value is not an indirect annotation dictionary",
            replyError.Message, StringComparison.Ordinal);
        return;

        PdfDocument WithAnnotation(PdfDictionary annotation)
        {
            PdfDictionary page = new(pages[0]
                .Where(entry => !entry.Key.Equals(Name("Annots")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("Annots"), new PdfArray([annotation]))));
            return PdfDocument.Open(new PdfIncrementalUpdateBuilder(original)
                .ReplaceObject(references[0].ObjectNumber, page)
                .Build());
        }
    }

    [Fact]
    public void Build_RejectsInvalidImportedAnnotationAppearanceStreams()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference invalidAppearance = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Form"))
            ]), []));
        var annotation = new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("AP"), new PdfDictionary([
                new(Name("N"), invalidAppearance)
            ]))
        ]);
        update.ReplaceObject(references[0].ObjectNumber, new PdfDictionary(pages[0]
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("Annots"), new PdfArray([annotation])))));
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/AP /N appearance has no four-number /BBox array",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedAnnotationAppearanceResources()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference appearance = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Form")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])),
                new(Name("Resources"), new PdfDictionary([
                    new(Name("ExtGState"), new PdfDictionary([
                        new(Name("Broken"), new PdfInteger(1))
                    ]))
                ]))
            ]), []));
        var annotation = new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("AP"), new PdfDictionary([
                new(Name("N"), appearance)
            ]))
        ]);
        update.ReplaceObject(references[0].ObjectNumber, new PdfDictionary(pages[0]
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("Annots"), new PdfArray([annotation])))));
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("/AP /N appearance /Resources /ExtGState /Broken entry has an invalid object type",
            error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Line", "line annotation has no /L array")]
    [InlineData("LinkTargets", "link annotation contains both /A and /Dest")]
    [InlineData("Sound", "sound annotation has no sound stream")]
    [InlineData("SoundDictionary", "sound annotation has no sound stream")]
    [InlineData("SoundType", "sound stream has an invalid /Type value")]
    [InlineData("SoundRate", "inline sound has no positive finite /R value")]
    [InlineData("Redact", "redaction /Repeat value is not a boolean")]
    [InlineData("RedactOverlay", "redaction with /OverlayText has no /DA string")]
    [InlineData("Movie", "movie annotation has no /Movie dictionary")]
    [InlineData("3D", "3D annotation has no /3DD stream or dictionary")]
    [InlineData("RichMedia", "rich-media annotation has no /RichMediaContent dictionary")]
    [InlineData("Caret", "caret /Sy value /Unexpected is not defined")]
    [InlineData("Square", "Square /RD value is not four nonnegative numbers")]
    [InlineData("SquareBounds", "Square /RD value collapses its annotation rectangle")]
    [InlineData("Watermark", "watermark /FixedPrint value is not a dictionary")]
    [InlineData("Projection", "projection /Measure value is not a dictionary")]
    [InlineData("PrinterMark", "PrinterMark annotation has no /AP appearance dictionary")]
    [InlineData("TrapNet", "TrapNet annotation has no /AP appearance dictionary")]
    [InlineData("PolyLine", "PolyLine /LE value /Unexpected is not defined")]
    [InlineData("Text", "/RT value /Unexpected is not defined")]
    [InlineData("TextIcon", "text annotation /Name value is not a name")]
    [InlineData("Circle", "/ExData value is not a dictionary")]
    [InlineData("MovieActivation", "movie /A value is not an activation dictionary")]
    [InlineData("MovieFile", "movie dictionary has no /F file specification")]
    [InlineData("MoviePoster", "movie /Poster stream is not an image XObject")]
    [InlineData("MovieWindow", "movie /A /FWScale value is not two positive integers")]
    [InlineData("MovieTime", "movie /A /Start value is not a nonnegative integer or 8-byte time string")]
    [InlineData("Screen", "/MK /R value is not a multiple of 90")]
    [InlineData("SquareBorderEffect", "/BE /S value /Unexpected is not defined")]
    [InlineData("TextState", "/State /Unexpected is not defined for /StateModel /Review")]
    [InlineData("LineMeasure", "line /Measure value is not a dictionary")]
    [InlineData("FreeTextStyle", "free-text /DS value is not a string")]
    [InlineData("FreeTextIntent", "free-text /IT /Unexpected is not defined")]
    [InlineData("3DBounds", "3D /3DB value is not four finite numbers")]
    [InlineData("RichMediaAssets", "/RichMediaContent /Assets value is not a name-tree dictionary")]
    [InlineData("RichMediaSettings", "/RichMediaSettings /Activation value is not a dictionary")]
    [InlineData("RichMediaCondition", "/Activation /Condition /Unexpected is not defined")]
    [InlineData("RichMediaConfiguration", "configuration /Subtype /Unexpected is not defined")]
    [InlineData("RichMediaInstance", "rich-media instance is not indirect")]
    [InlineData("RichMediaInstanceSubtype", "does not match its configuration /Subtype /Video")]
    [InlineData("RichMediaAnimation", "rich-media animation /Speed value is not a positive finite number")]
    [InlineData("RichMediaPresentation", "rich-media presentation /Style /Overlay is not defined")]
    [InlineData("RichMediaWindowed", "windowed rich-media presentation has no /Window dictionary")]
    [InlineData("RichMediaParams", "rich-media parameters /Binding /Overlay is not defined")]
    [InlineData("Markup3DData", "Markup3D /ExData has no valid /3DA target")]
    [InlineData("3DSubtype", "3D stream /Subtype /Unexpected is not defined")]
    [InlineData("3DActivation", "3D activation /AIS /Unexpected is not defined")]
    [InlineData("3DScript", "3D stream /OnInstantiate value is not a stream")]
    [InlineData("3DView", "3D view has no /XN string")]
    [InlineData("3DDefaultView", "3D stream /DV value is not a defined view selector")]
    [InlineData("3DAnimation", "3D animation style /TM value is not a positive finite number")]
    [InlineData("3DProjection", "perspective projection has no /FOV value from 0 through 180")]
    [InlineData("3DBackground", "3D background /C value is not an RGB triplet")]
    [InlineData("3DU3DPathBackground", "3D background /C value is not an RGB triplet")]
    [InlineData("3DRenderMode", "3D render mode /O value is outside 0 through 1")]
    [InlineData("3DLighting", "3D lighting scheme has no /Subtype name")]
    [InlineData("3DCrossSection", "3D cross section has no valid /O orientation")]
    [InlineData("3DNode", "3D node has no /N string")]
    [InlineData("3DResources", "3D stream /Resources value is not a name-tree dictionary")]
    [InlineData("TextOptionalContent", "/OC value is not an optional-content dictionary")]
    [InlineData("Popup", "popup annotation has no indirect /Parent dictionary")]
    public void Build_RejectsMalformedImportedAnnotationSubtypeData(
        string subtype, string expectedMessage)
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var pageUpdate = new PdfIncrementalUpdateBuilder(source);
        var annotationEntries = new List<KeyValuePair<PdfName, PdfObject>>([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name(subtype switch
            {
                "MovieActivation" or "MovieFile" or "MoviePoster" or "MovieWindow"
                    or "MovieTime" => "Movie",
                "SoundDictionary" or "SoundType" or "SoundRate" => "Sound",
                "SquareBorderEffect" => "Square",
                "SquareBounds" => "Square",
                "RedactOverlay" => "Redact",
                "LinkTargets" => "Link",
                "TextState" => "Text",
                "TextIcon" => "Text",
                "TextOptionalContent" => "Text",
                "LineMeasure" => "Line",
                "FreeTextStyle" or "FreeTextIntent" => "FreeText",
                "3DBounds" or "3DSubtype" or "3DActivation" or "3DScript" or "3DView"
                    or "3DDefaultView"
                    or "3DAnimation" or "3DProjection" or "3DBackground"
                    or "3DU3DPathBackground"
                    or "3DRenderMode" or "3DLighting" or "3DCrossSection" or "3DNode"
                    or "3DResources" => "3D",
                "RichMediaAssets" or "RichMediaSettings" or "RichMediaCondition"
                    or "RichMediaConfiguration" or "RichMediaInstance"
                    or "RichMediaInstanceSubtype"
                    or "RichMediaAnimation" or "RichMediaPresentation"
                    or "RichMediaWindowed"
                    or "RichMediaParams" => "RichMedia",
                "Markup3DData" => "Circle",
                _ => subtype
            })),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ]))
        ]);
        if (subtype == "Redact")
            annotationEntries.Add(new(Name("Repeat"), new PdfInteger(1)));
        if (subtype == "RedactOverlay")
            annotationEntries.Add(new(Name("OverlayText"),
                new PdfString("Removed"u8, PdfStringForm.Literal)));
        if (subtype == "LinkTargets")
        {
            annotationEntries.Add(new(Name("A"), new PdfDictionary([
                new(Name("S"), Name("URI")),
                new(Name("URI"), new PdfString("https://example.com"u8,
                    PdfStringForm.Literal))
            ])));
            annotationEntries.Add(new(Name("Dest"), Name("Target")));
        }
        if (subtype == "Caret")
            annotationEntries.Add(new(Name("Sy"), Name("Unexpected")));
        if (subtype == "Square")
            annotationEntries.Add(new(Name("RD"), new PdfArray([
                new PdfInteger(1), new PdfInteger(1), new PdfInteger(1)
            ])));
        if (subtype == "SquareBounds")
            annotationEntries.Add(new(Name("RD"), new PdfArray([
                new PdfInteger(6), new PdfInteger(0),
                new PdfInteger(5), new PdfInteger(0)
            ])));
        if (subtype == "Watermark")
            annotationEntries.Add(new(Name("FixedPrint"), new PdfInteger(1)));
        if (subtype == "Projection")
            annotationEntries.Add(new(Name("Measure"), new PdfInteger(1)));
        if (subtype == "PolyLine")
        {
            annotationEntries.Add(new(Name("Vertices"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])));
            annotationEntries.Add(new(Name("LE"), new PdfArray([
                Name("None"), Name("Unexpected")
            ])));
        }
        if (subtype == "Text")
            annotationEntries.Add(new(Name("RT"), Name("Unexpected")));
        if (subtype == "TextIcon")
            annotationEntries.Add(new(Name("Name"), new PdfInteger(1)));
        if (subtype == "Circle")
            annotationEntries.Add(new(Name("ExData"), new PdfInteger(1)));
        if (subtype == "Markup3DData")
            annotationEntries.Add(new(Name("ExData"), new PdfDictionary([
                new(Name("Type"), Name("ExData")),
                new(Name("Subtype"), Name("Markup3D")),
                new(Name("3DV"), new PdfDictionary([]))
            ])));
        if (subtype == "MovieActivation")
        {
            annotationEntries.Add(new(Name("Movie"), new PdfDictionary([
                new(Name("F"), new PdfString("movie.mp4"u8, PdfStringForm.Literal))
            ])));
            annotationEntries.Add(new(Name("A"), new PdfInteger(1)));
        }
        if (subtype == "MovieWindow")
        {
            annotationEntries.Add(new(Name("Movie"), new PdfDictionary([
                new(Name("F"), new PdfString("movie.mp4"u8, PdfStringForm.Literal))
            ])));
            annotationEntries.Add(new(Name("A"), new PdfDictionary([
                new(Name("FWScale"), new PdfArray([
                    new PdfInteger(1), new PdfInteger(0)
                ]))
            ])));
        }
        if (subtype == "MovieTime")
        {
            annotationEntries.Add(new(Name("Movie"), new PdfDictionary([
                new(Name("F"), new PdfString("movie.mp4"u8, PdfStringForm.Literal))
            ])));
            annotationEntries.Add(new(Name("A"), new PdfDictionary([
                new(Name("Start"), new PdfInteger(-1))
            ])));
        }
        if (subtype == "MovieFile")
            annotationEntries.Add(new(Name("Movie"), new PdfDictionary([])));
        if (subtype == "MoviePoster")
            annotationEntries.Add(new(Name("Movie"), new PdfDictionary([
                new(Name("F"), new PdfString("movie.mp4"u8, PdfStringForm.Literal)),
                new(Name("Poster"), pageUpdate.AddObject(
                    new PdfStream(new PdfDictionary([]), [])))
            ])));
        if (subtype == "SoundDictionary")
            annotationEntries.Add(new(Name("Sound"), new PdfDictionary([])));
        if (subtype == "SoundType")
            annotationEntries.Add(new(Name("Sound"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("Unexpected"))
                ]), []))));
        if (subtype == "SoundRate")
            annotationEntries.Add(new(Name("Sound"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("Sound"))
                ]), []))));
        if (subtype == "Screen")
            annotationEntries.Add(new(Name("MK"), new PdfDictionary([
                new(Name("R"), new PdfInteger(45))
            ])));
        if (subtype == "SquareBorderEffect")
            annotationEntries.Add(new(Name("BE"), new PdfDictionary([
                new(Name("S"), Name("Unexpected"))
            ])));
        if (subtype == "TextState")
        {
            annotationEntries.Add(new(Name("StateModel"),
                new PdfString("Review"u8, PdfStringForm.Literal)));
            annotationEntries.Add(new(Name("State"),
                new PdfString("Unexpected"u8, PdfStringForm.Literal)));
        }
        if (subtype == "TextOptionalContent")
            annotationEntries.Add(new(Name("OC"), new PdfInteger(1)));
        if (subtype == "LineMeasure")
        {
            annotationEntries.Add(new(Name("L"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])));
            annotationEntries.Add(new(Name("Measure"), new PdfInteger(1)));
        }
        if (subtype == "FreeTextStyle")
        {
            annotationEntries.Add(new(Name("DA"), new PdfString(
                "/Helv 12 Tf"u8, PdfStringForm.Literal)));
            annotationEntries.Add(new(Name("DS"), new PdfInteger(1)));
        }
        if (subtype == "FreeTextIntent")
        {
            annotationEntries.Add(new(Name("DA"), new PdfString(
                "/Helv 12 Tf"u8, PdfStringForm.Literal)));
            annotationEntries.Add(new(Name("IT"), Name("Unexpected")));
        }
        if (subtype == "3DBounds")
        {
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("U3D"))
                ]), []))));
            annotationEntries.Add(new(Name("3DB"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(10)
            ])));
        }
        if (subtype == "3DSubtype")
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("Unexpected"))
                ]), []))));
        if (subtype == "3DActivation")
        {
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("U3D"))
                ]), []))));
            annotationEntries.Add(new(Name("3DA"), new PdfDictionary([
                new(Name("AIS"), Name("Unexpected"))
            ])));
        }
        if (subtype == "3DScript")
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("U3D")),
                    new(Name("OnInstantiate"), new PdfInteger(1))
                ]), []))));
        if (subtype == "3DView")
        {
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("U3D"))
                ]), []))));
            annotationEntries.Add(new(Name("3DV"), new PdfDictionary([
                new(Name("Type"), Name("3DView"))
            ])));
        }
        if (subtype == "3DDefaultView")
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("U3D")),
                    new(Name("VA"), new PdfArray([new PdfDictionary([
                        new(Name("Type"), Name("3DView")),
                        new(Name("XN"), new PdfString("View"u8, PdfStringForm.Literal))
                    ])])),
                    new(Name("DV"), new PdfInteger(2))
                ]), []))));
        if (subtype == "3DAnimation")
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("U3D")),
                    new(Name("AN"), new PdfDictionary([
                        new(Name("Type"), Name("3DAnimationStyle")),
                        new(Name("Subtype"), Name("Linear")),
                        new(Name("TM"), new PdfInteger(0))
                    ]))
                ]), []))));
        if (subtype == "3DResources")
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("U3D")),
                    new(Name("Resources"), new PdfInteger(1))
                ]), []))));
        if (subtype is "3DProjection" or "3DBackground" or "3DU3DPathBackground"
            or "3DRenderMode" or "3DLighting"
            or "3DCrossSection" or "3DNode")
        {
            var viewEntries = new List<KeyValuePair<PdfName, PdfObject>>([
                new(Name("Type"), Name("3DView")),
                new(Name("XN"), new PdfString("View"u8, PdfStringForm.Literal))
            ]);
            if (subtype == "3DU3DPathBackground")
            {
                viewEntries.Add(new(Name("MS"), Name("U3D")));
                viewEntries.Add(new(Name("U3DPath"), new PdfString(
                    "Node"u8, PdfStringForm.Literal)));
                viewEntries.Add(new(Name("BG"), new PdfDictionary([
                    new(Name("C"), new PdfArray([
                        new PdfInteger(1), new PdfInteger(1)
                    ]))
                ])));
            }
            else if (subtype == "3DProjection")
                viewEntries.Add(new(Name("P"), new PdfDictionary([
                    new(Name("Subtype"), Name("P")),
                    new(Name("N"), new PdfInteger(1))
                ])));
            else
            if (subtype == "3DBackground")
                viewEntries.Add(new(Name("BG"), new PdfDictionary([
                    new(Name("C"), new PdfArray([
                        new PdfInteger(1), new PdfInteger(1)
                    ]))
                ])));
            else if (subtype == "3DRenderMode")
                viewEntries.Add(new(Name("RM"), new PdfDictionary([
                    new(Name("Subtype"), Name("Solid")),
                    new(Name("O"), new PdfInteger(2))
                ])));
            else if (subtype == "3DLighting")
                viewEntries.Add(new(Name("LS"), new PdfDictionary([
                    new(Name("Type"), Name("3DLightingScheme"))
                ])));
            else if (subtype == "3DCrossSection")
                viewEntries.Add(new(Name("SA"), new PdfArray([
                    new PdfDictionary([
                        new(Name("O"), new PdfArray([
                            new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)
                        ]))
                    ])
                ])));
            else
                viewEntries.Add(new(Name("NA"), new PdfArray([
                    new PdfDictionary([new(Name("Type"), Name("3DNode"))])
                ])));
            annotationEntries.Add(new(Name("3DD"), pageUpdate.AddObject(
                new PdfStream(new PdfDictionary([
                    new(Name("Type"), Name("3D")),
                    new(Name("Subtype"), Name("U3D")),
                    new(Name("VA"), new PdfArray([new PdfDictionary(viewEntries)]))
                ]), []))));
        }
        if (subtype == "RichMediaAssets")
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([
                new(Name("Assets"), new PdfInteger(1))
            ])));
        if (subtype == "RichMediaSettings")
        {
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([])));
            annotationEntries.Add(new(Name("RichMediaSettings"), new PdfDictionary([
                new(Name("Activation"), new PdfInteger(1))
            ])));
        }
        if (subtype == "RichMediaCondition")
        {
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([])));
            annotationEntries.Add(new(Name("RichMediaSettings"), new PdfDictionary([
                new(Name("Activation"), new PdfDictionary([
                    new(Name("Condition"), Name("Unexpected"))
                ]))
            ])));
        }
        if (subtype == "RichMediaConfiguration")
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([
                new(Name("Configurations"), new PdfArray([
                    pageUpdate.AddObject(new PdfDictionary([
                        new(Name("Subtype"), Name("Unexpected"))
                    ]))
                ]))
            ])));
        if (subtype == "RichMediaInstance")
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([
                new(Name("Configurations"), new PdfArray([
                    pageUpdate.AddObject(new PdfDictionary([
                        new(Name("Subtype"), Name("Video")),
                        new(Name("Instances"), new PdfArray([
                            new PdfDictionary([])
                        ]))
                    ]))
                ]))
            ])));
        if (subtype == "RichMediaInstanceSubtype")
        {
            PdfIndirectReference asset = pageUpdate.AddObject(new PdfDictionary([
                new(Name("Type"), Name("Filespec")),
                new(Name("F"), new PdfString("movie.mp4"u8, PdfStringForm.Literal))
            ]));
            PdfIndirectReference instance = pageUpdate.AddObject(new PdfDictionary([
                new(Name("Subtype"), Name("Sound")),
                new(Name("Asset"), asset)
            ]));
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([
                new(Name("Assets"), new PdfDictionary([
                    new(Name("Names"), new PdfArray([
                        new PdfString("movie.mp4"u8, PdfStringForm.Literal), asset
                    ]))
                ])),
                new(Name("Configurations"), new PdfArray([
                    pageUpdate.AddObject(new PdfDictionary([
                        new(Name("Subtype"), Name("Video")),
                        new(Name("Instances"), new PdfArray([instance]))
                    ]))
                ]))
            ])));
        }
        if (subtype == "RichMediaAnimation")
        {
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([])));
            annotationEntries.Add(new(Name("RichMediaSettings"), new PdfDictionary([
                new(Name("Activation"), new PdfDictionary([
                    new(Name("Animation"), new PdfDictionary([
                        new(Name("Type"), Name("RichMediaAnimation")),
                        new(Name("Speed"), new PdfInteger(0))
                    ]))
                ]))
            ])));
        }
        if (subtype == "RichMediaPresentation")
        {
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([])));
            annotationEntries.Add(new(Name("RichMediaSettings"), new PdfDictionary([
                new(Name("Activation"), new PdfDictionary([
                    new(Name("Presentation"), new PdfDictionary([
                        new(Name("Style"), Name("Overlay"))
                    ]))
                ]))
            ])));
        }
        if (subtype == "RichMediaWindowed")
        {
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([])));
            annotationEntries.Add(new(Name("RichMediaSettings"), new PdfDictionary([
                new(Name("Activation"), new PdfDictionary([
                    new(Name("Presentation"), new PdfDictionary([
                        new(Name("Style"), Name("Windowed"))
                    ]))
                ]))
            ])));
        }
        if (subtype == "RichMediaParams")
        {
            PdfIndirectReference embedded = pageUpdate.AddObject(new PdfStream(
                new PdfDictionary([
                    new(Name("Type"), Name("EmbeddedFile")),
                    new(Name("Subtype"), Name("application/x-shockwave-flash"))
                ]), []));
            PdfIndirectReference asset = pageUpdate.AddObject(new PdfDictionary([
                new(Name("Type"), Name("Filespec")),
                new(Name("F"), new PdfString("movie.swf"u8, PdfStringForm.Literal)),
                new(Name("EF"), new PdfDictionary([new(Name("F"), embedded)]))
            ]));
            PdfIndirectReference instance = pageUpdate.AddObject(new PdfDictionary([
                new(Name("Type"), Name("RichMediaInstance")),
                new(Name("Subtype"), Name("Flash")),
                new(Name("Asset"), asset),
                new(Name("Params"), new PdfDictionary([
                    new(Name("Binding"), Name("Overlay"))
                ]))
            ]));
            PdfIndirectReference configuration = pageUpdate.AddObject(new PdfDictionary([
                new(Name("Type"), Name("RichMediaConfiguration")),
                new(Name("Subtype"), Name("Flash")),
                new(Name("Instances"), new PdfArray([instance]))
            ]));
            annotationEntries.Add(new(Name("RichMediaContent"), new PdfDictionary([
                new(Name("Assets"), new PdfDictionary([
                    new(Name("Names"), new PdfArray([
                        new PdfString("movie.swf"u8, PdfStringForm.Literal), asset
                    ]))
                ])),
                new(Name("Configurations"), new PdfArray([configuration]))
            ])));
        }
        var annotation = new PdfDictionary(annotationEntries);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                new PdfArray([annotation]))));
        source = PdfDocument.Open(pageUpdate
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPrepressAnnotations()
    {
        PdfDocument printerMark = WithAnnotation("PrinterMark", []);
        InvalidOperationException printerMarkError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(printerMark, 0).Build());
        Assert.Contains("printer-mark /F value does not contain only Print and ReadOnly flags",
            printerMarkError.Message, StringComparison.Ordinal);

        PdfDocument malformedPrinterMark = WithAnnotation("PrinterMark", [
            new(Name("F"), new PdfInteger(68)),
            new(Name("MN"), new PdfInteger(1))
        ]);
        InvalidOperationException markNameError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(malformedPrinterMark, 0).Build());
        Assert.Contains("printer-mark /MN value is not a name",
            markNameError.Message, StringComparison.Ordinal);

        PdfDocument malformedMarkForm = WithAnnotation("PrinterMark", [
            new(Name("F"), new PdfInteger(68))
        ], [
            new(Name("MarkStyle"), new PdfInteger(1))
        ]);
        InvalidOperationException markStyleError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(malformedMarkForm, 0).Build());
        Assert.Contains("/MarkStyle value is not a text string",
            markStyleError.Message, StringComparison.Ordinal);

        PdfDocument trapNetwork = WithAnnotation("TrapNet", [
            new(Name("AS"), Name("Normal")),
            new(Name("F"), new PdfInteger(68))
        ]);
        InvalidOperationException trapNetworkError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(trapNetwork, 0).Build());
        Assert.Contains("requires either /LastModified or both /Version and /AnnotStates",
            trapNetworkError.Message, StringComparison.Ordinal);

        KeyValuePair<PdfName, PdfObject>[] validTrapEntries = [
            new(Name("AS"), Name("Normal")),
            new(Name("F"), new PdfInteger(68)),
            new(Name("LastModified"), new PdfString(
                "D:20260824120000-07'00'"u8, PdfStringForm.Literal))
        ];
        PdfDocument directTrapAppearance = WithAnnotation(
            "TrapNet", validTrapEntries, directTrapAppearance: true);
        InvalidOperationException directTrapError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(directTrapAppearance, 0).Build());
        Assert.Contains("/AP /N value is not an appearance-state dictionary",
            directTrapError.Message, StringComparison.Ordinal);

        PdfDocument missingProcessModel = WithAnnotation(
            "TrapNet", validTrapEntries, omitTrapProcessModel: true);
        InvalidOperationException processModelError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(missingProcessModel, 0).Build());
        Assert.Contains("has no /PCM process-color-model name",
            processModelError.Message, StringComparison.Ordinal);

        PdfDocument malformedTrapStyles = WithAnnotation(
            "TrapNet", validTrapEntries, [
                new(Name("TrapStyles"), new PdfInteger(1))
            ]);
        InvalidOperationException trapStylesError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(malformedTrapStyles, 0).Build());
        Assert.Contains("/TrapStyles value is not a text string",
            trapStylesError.Message, StringComparison.Ordinal);

        PdfDocument validPrinterMark = WithAnnotation("PrinterMark", [
            new(Name("MN"), Name("Registration")),
            new(Name("F"), new PdfInteger(68))
        ]);
        _ = new PdfIncrementalPageEditor(PdfDocument.Open(
                new PdfDocumentBuilder().Build()))
            .AddImportedPage(validPrinterMark, 0).Build();

        PdfDocument datedTrapNetwork = WithAnnotation("TrapNet", [
            new(Name("AS"), Name("Normal")),
            new(Name("F"), new PdfInteger(68)),
            new(Name("LastModified"), new PdfString(
                "D:20260824120000-07'00'"u8, PdfStringForm.Literal))
        ]);
        _ = new PdfIncrementalPageEditor(PdfDocument.Open(
                new PdfDocumentBuilder().Build()))
            .AddImportedPage(datedTrapNetwork, 0).Build();

        PdfDocument versionedTrapNetwork = WithAnnotation("TrapNet", [
            new(Name("AS"), Name("Normal")),
            new(Name("F"), new PdfInteger(68)),
            new(Name("Version"), new PdfArray([])),
            new(Name("AnnotStates"), new PdfArray([Name("Normal")]))
        ]);
        _ = new PdfIncrementalPageEditor(PdfDocument.Open(
                new PdfDocumentBuilder().Build()))
            .AddImportedPage(versionedTrapNetwork, 0).Build();

        PdfDocument WithAnnotation(
            string annotationSubtype,
            IEnumerable<KeyValuePair<PdfName, PdfObject>> extraEntries,
            IEnumerable<KeyValuePair<PdfName, PdfObject>>? appearanceExtraEntries = null,
            bool omitTrapProcessModel = false,
            bool directTrapAppearance = false)
        {
            PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
                .AddBlankPage(200, 300).Build());
            (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) =
                FlatPages(source);
            var update = new PdfIncrementalUpdateBuilder(source);
            var appearanceEntries = new List<KeyValuePair<PdfName, PdfObject>>
            {
                    new(Name("Type"), Name("XObject")),
                    new(Name("Subtype"), Name("Form")),
                    new(Name("BBox"), new PdfArray([
                        new PdfInteger(0), new PdfInteger(0),
                        new PdfInteger(10), new PdfInteger(10)
                    ]))
            };
            if (annotationSubtype == "TrapNet" && !omitTrapProcessModel)
                appearanceEntries.Add(new(Name("PCM"), Name("DeviceCMYK")));
            if (appearanceExtraEntries is not null)
                appearanceEntries.AddRange(appearanceExtraEntries);
            PdfIndirectReference appearance = update.AddObject(new PdfStream(
                new PdfDictionary(appearanceEntries), []));
            PdfObject normalAppearance = annotationSubtype == "TrapNet"
                && !directTrapAppearance
                ? new PdfDictionary([new(Name("Normal"), appearance)])
                : appearance;
            var annotation = new PdfDictionary(new[]
            {
                new KeyValuePair<PdfName, PdfObject>(Name("Type"), Name("Annot")),
                new KeyValuePair<PdfName, PdfObject>(
                    Name("Subtype"), Name(annotationSubtype)),
                new KeyValuePair<PdfName, PdfObject>(Name("Rect"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])),
                new KeyValuePair<PdfName, PdfObject>(Name("AP"), new PdfDictionary([
                    new(Name("N"), normalAppearance)
                ]))
            }.Concat(extraEntries));
            update.ReplaceObject(pageReferences[0].ObjectNumber,
                new PdfDictionary(pages[0].Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([annotation])))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void Build_ImportsMovieAnnotationActivationDictionary()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var annotation = new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Movie")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("Movie"), new PdfDictionary([
                new(Name("F"), new PdfString("movie.mp4"u8, PdfStringForm.Literal))
            ])),
            new(Name("A"), new PdfDictionary([
                new(Name("Mode"), Name("Once")),
                new(Name("Rate"), new PdfInteger(1)),
                new(Name("Volume"), new PdfInteger(0)),
                new(Name("ShowControls"), new PdfBoolean(true))
            ]))
        ]);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, new PdfDictionary(pages[0].Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([annotation])))))
            .Build());

        byte[] output = new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build();

        (_, PdfIndirectReference[] outputPages, _) = FlatPages(PdfDocument.Open(output));
        Assert.Single(outputPages);
    }

    [Fact]
    public void Build_PreservesMultiHopIndirectImportedPageValues()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(original);
        var update = new PdfIncrementalUpdateBuilder(original);

        PdfIndirectReference userUnit = update.AddObject(new PdfInteger(2));
        PdfIndirectReference userUnitAlias = update.AddObject(userUnit);
        PdfIndirectReference zoom = update.AddObject(new PdfReal(1.25));
        PdfIndirectReference zoomAlias = update.AddObject(zoom);
        PdfIndirectReference tabs = update.AddObject(Name("S"));
        PdfIndirectReference tabsAlias = update.AddObject(tabs);
        PdfIndirectReference modified = update.AddObject(
            new PdfString("D:20260824120000-07'00'"u8, PdfStringForm.Literal));
        PdfIndirectReference modifiedAlias = update.AddObject(modified);
        PdfIndirectReference content = update.AddObject(new PdfStream(
            new PdfDictionary([]), "q Q"u8.ToArray()));
        PdfIndirectReference contentAlias = update.AddObject(content);
        PdfIndirectReference contents = update.AddObject(new PdfArray([contentAlias]));
        PdfIndirectReference contentsAlias = update.AddObject(contents);

        update.ReplaceObject(pageReferences[0].ObjectNumber,
            new PdfDictionary(pages[0]
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("UserUnit"), userUnitAlias))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("PZ"), zoomAlias))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Tabs"), tabsAlias))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("LastModified"), modifiedAlias))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Contents"), contentsAlias))));
        PdfDocument source = PdfDocument.Open(update.Build());

        PdfDocument imported = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        PdfDictionary page = FlatPages(imported).Pages[0];
        PdfArray importedContents = Assert.IsType<PdfArray>(
            ResolveFully(imported, page[Name("Contents")]));

        Assert.Equal(2, Assert.IsType<PdfInteger>(
            ResolveFully(imported, page[Name("UserUnit")])).Value);
        Assert.Equal(1.25, Assert.IsType<PdfReal>(
            ResolveFully(imported, page[Name("PZ")])).Value);
        Assert.Equal("S", Assert.IsType<PdfName>(
            ResolveFully(imported, page[Name("Tabs")])).ValueAsLatin1());
        Assert.Equal("q Q", Encoding.ASCII.GetString(PdfStreamDecoder.Decode(
            Assert.IsType<PdfStream>(ResolveFully(imported, importedContents[0])))));

        static PdfObject ResolveFully(PdfDocument document, PdfObject value)
        {
            while (value is PdfIndirectReference reference)
                value = document.Resolve(reference);
            return value;
        }
    }

    [Fact]
    public void Build_RejectsInvalidImportedPagePresentationValues()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(original);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        PdfDocument invalidTransition = WithPageValue("Trans", new PdfDictionary([
            new(Name("Type"), Name("Trans")),
            new(Name("S"), Name("Teleport"))
        ]));
        InvalidOperationException transitionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidTransition, 0)
                .Build());
        Assert.Contains("/Trans value /S value /Teleport is not defined",
            transitionError.Message, StringComparison.Ordinal);

        PdfDocument invalidUserUnit = WithPageValue("UserUnit", new PdfInteger(0));
        InvalidOperationException userUnitError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidUserUnit, 0)
                .Build());
        Assert.Contains("/UserUnit value is outside the supported range",
            userUnitError.Message, StringComparison.Ordinal);

        PdfDocument invalidViewport = WithPageValue("VP", new PdfArray([
            new PdfDictionary([
                new(Name("Type"), Name("Viewport")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0), new PdfInteger(10)
                ]))
            ])
        ]));
        InvalidOperationException viewportError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(invalidViewport, 0)
                .Build());
        Assert.Contains("/VP value entry has no four-number /BBox array",
            viewportError.Message, StringComparison.Ordinal);

        PdfDocument emptyViewports = WithPageValue("VP", new PdfArray([]));
        InvalidOperationException emptyViewportsError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedPage(emptyViewports, 0)
                    .Build());
        Assert.Contains("/VP value array is empty",
            emptyViewportsError.Message, StringComparison.Ordinal);

        PdfDocument collapsedViewport = WithPageValue("VP", new PdfArray([
            new PdfDictionary([
                new(Name("Type"), Name("Viewport")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(0), new PdfInteger(10)
                ]))
            ])
        ]));
        InvalidOperationException collapsedViewportError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedPage(collapsedViewport, 0)
                    .Build());
        Assert.Contains("/VP value entry /BBox rectangle is collapsed",
            collapsedViewportError.Message, StringComparison.Ordinal);

        PdfDocument invalidPresentationSteps = WithPageValue(
            "PresSteps", new PdfInteger(1));
        InvalidOperationException presentationStepsError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedPage(invalidPresentationSteps, 0)
                    .Build());
        Assert.Contains("/PresSteps value is not a navigation-node dictionary",
            presentationStepsError.Message, StringComparison.Ordinal);

        InvalidOperationException zoomError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(WithPageValue("PZ", new PdfInteger(0)), 0)
                .Build());
        Assert.Contains("/PZ value is not a positive finite number",
            zoomError.Message, StringComparison.Ordinal);

        InvalidOperationException identifierError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(WithPageValue("ID", new PdfInteger(17)), 0)
                .Build());
        Assert.Contains("/ID value is not a byte string",
            identifierError.Message, StringComparison.Ordinal);

        InvalidOperationException templateError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(WithPageValue("TemplateInstantiated", new PdfInteger(1)), 0)
                .Build());
        Assert.Contains("/TemplateInstantiated value is not a name",
            templateError.Message, StringComparison.Ordinal);

        var invalidBoxColors = new PdfDictionary([
            new(Name("TrimBox"), new PdfDictionary([
                new(Name("C"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0), new PdfInteger(2)
                ]))
            ]))
        ]);
        InvalidOperationException boxColorError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(WithPageValue("BoxColorInfo", invalidBoxColors), 0)
                .Build());
        Assert.Contains("/BoxColorInfo value /TrimBox /C value is not a valid RGB color array",
            boxColorError.Message, StringComparison.Ordinal);

        InvalidOperationException groupError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(WithPageValue("Group", new PdfDictionary([
                    new(Name("Type"), Name("Group"))
                ])), 0)
                .Build());
        Assert.Contains("/Group value has no /S /Transparency value",
            groupError.Message, StringComparison.Ordinal);

        InvalidOperationException groupColorSpaceError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedPage(WithPageValue("Group", new PdfDictionary([
                        new(Name("Type"), Name("Group")),
                        new(Name("S"), Name("Transparency")),
                        new(Name("CS"), new PdfArray([
                            Name("Lab"), new PdfDictionary([
                                new(Name("WhitePoint"), new PdfArray([
                                    new PdfInteger(1), new PdfInteger(1), new PdfInteger(1)
                                ]))
                            ])
                        ]))
                    ])), 0)
                    .Build());
        Assert.Contains("prohibited for transparency blending",
            groupColorSpaceError.Message, StringComparison.Ordinal);

        InvalidOperationException documentPartError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedPage(WithPageValue("DPart", new PdfDictionary([
                        new(Name("Type"), Name("DPart"))
                    ])), 0)
                    .Build());
        Assert.Contains("/DPart value is not an indirect document-part dictionary",
            documentPartError.Message, StringComparison.Ordinal);

        NotSupportedException partialDocumentPartError = Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(WithIndirectPageValue("DPart", new PdfDictionary([
                    new(Name("Type"), Name("DPart"))
                ])), 0)
                .Build());
        Assert.Contains("document-part membership require a complete-document import",
            partialDocumentPartError.Message, StringComparison.Ordinal);
        return;

        PdfDocument WithPageValue(string key, PdfObject value)
        {
            PdfName name = Name(key);
            PdfDictionary page = new(pages[0]
                .Where(entry => !entry.Key.Equals(name))
                .Append(new KeyValuePair<PdfName, PdfObject>(name, value)));
            return PdfDocument.Open(new PdfIncrementalUpdateBuilder(original)
                .ReplaceObject(references[0].ObjectNumber, page)
                .Build());
        }

        PdfDocument WithIndirectPageValue(string key, PdfObject value)
        {
            PdfName name = Name(key);
            var update = new PdfIncrementalUpdateBuilder(original);
            PdfIndirectReference valueReference = update.AddObject(value);
            PdfDictionary page = new(pages[0]
                .Where(entry => !entry.Key.Equals(name))
                .Append(new KeyValuePair<PdfName, PdfObject>(name, valueReference)));
            update.ReplaceObject(references[0].ObjectNumber, page);
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void Build_RejectsMalformedImportedViewportMeasures()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfDocument original = source;
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        var viewport = new PdfDictionary([
            new(Name("Type"), Name("Viewport")),
            new(Name("BBox"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(100), new PdfInteger(100)
            ])),
            new(Name("Measure"), new PdfDictionary([
                new(Name("Type"), Name("Measure")),
                new(Name("Subtype"), Name("RL"))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("VP"),
                new PdfArray([viewport]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .Build());

        Assert.Contains("rectilinear measure has no /R string",
            error.Message, StringComparison.Ordinal);

        PdfArray points = new([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(1), new PdfInteger(1)
        ]);
        PdfDictionary invalidCoordinateSystem = GeospatialViewport(
            new PdfDictionary([new(Name("Type"), Name("GEOGCS"))]));
        InvalidOperationException coordinateError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(WithViewport(invalidCoordinateSystem), 0).Build());
        Assert.Contains("/GCS dictionary has neither /EPSG nor /WKT",
            coordinateError.Message, StringComparison.Ordinal);

        PdfDictionary missingLocalPoints = GeospatialViewport(new PdfDictionary([
            new(Name("Type"), Name("GEOGCS")),
            new(Name("EPSG"), new PdfInteger(4326))
        ]), includeLocalPoints: false);
        InvalidOperationException localPointsError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(WithViewport(missingLocalPoints), 0).Build());
        Assert.Contains("has no /LPTS array",
            localPointsError.Message, StringComparison.Ordinal);

        PdfDictionary outOfBoundsLocalPoints = GeospatialViewport(new PdfDictionary([
            new(Name("Type"), Name("GEOGCS")),
            new(Name("EPSG"), new PdfInteger(4326))
        ]), localPoints: new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(2), new PdfInteger(1)
        ]));
        InvalidOperationException localBoundsError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(WithViewport(outOfBoundsLocalPoints), 0).Build());
        Assert.Contains("/LPTS contains a coordinate outside the unit square",
            localBoundsError.Message, StringComparison.Ordinal);

        PdfDictionary validCoordinateSystem = GeospatialViewport(new PdfDictionary([
            new(Name("Type"), Name("GEOGCS")),
            new(Name("EPSG"), new PdfInteger(4326))
        ]));
        _ = new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(WithViewport(validCoordinateSystem), 0).Build();

        PdfDictionary GeospatialViewport(
            PdfDictionary coordinateSystem, bool includeLocalPoints = true,
            PdfArray? localPoints = null)
        {
            var measureEntries = new List<KeyValuePair<PdfName, PdfObject>>([
                new(Name("Type"), Name("Measure")),
                new(Name("Subtype"), Name("GEO")),
                new(Name("GCS"), coordinateSystem),
                new(Name("GPTS"), points)
            ]);
            if (includeLocalPoints)
                measureEntries.Add(new(Name("LPTS"), localPoints ?? points));
            return new PdfDictionary([
                new(Name("Type"), Name("Viewport")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(100), new PdfInteger(100)
                ])),
                new(Name("Measure"), new PdfDictionary(measureEntries))
            ]);
        }

        PdfDocument WithViewport(PdfDictionary value)
        {
            PdfDictionary page = new(pages[0].Append(
                new KeyValuePair<PdfName, PdfObject>(Name("VP"),
                    new PdfArray([value]))));
            return PdfDocument.Open(new PdfIncrementalUpdateBuilder(original)
                .ReplaceObject(references[0].ObjectNumber, page)
                .Build());
        }
    }

    [Fact]
    public void Build_ValidatesImportedViewportNumberFormats()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(original);
        var malformed = new (PdfDictionary Format, string Message)[]
        {
            (Format(new KeyValuePair<PdfName, PdfObject>(Name("Type"), Name("MeasureFormat"))), "invalid /Type"),
            (Format(new KeyValuePair<PdfName, PdfObject>(Name("F"), Name("E"))), "/F /E is not defined"),
            (Format(new KeyValuePair<PdfName, PdfObject>(Name("D"), new PdfInteger(16))), "invalid /D"),
            (Format(new KeyValuePair<PdfName, PdfObject>(Name("FD"), new PdfInteger(1))), "/FD value is not boolean"),
            (Format(new KeyValuePair<PdfName, PdfObject>(Name("RT"), Name("comma"))), "/RT value is not a string"),
            (Format(new KeyValuePair<PdfName, PdfObject>(Name("O"), Name("Before"))), "/O /Before is not defined")
        };

        foreach ((PdfDictionary format, string message) in malformed)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                    .AddImportedPage(WithFormats(new PdfArray([format])), 0)
                    .Build());
            Assert.Contains(message, error.Message, StringComparison.Ordinal);
        }

        PdfDictionary feet = Format();
        PdfDictionary inches = Format(
            new(Name("Type"), Name("NumberFormat")),
            new(Name("F"), Name("F")),
            new(Name("D"), new PdfInteger(16)),
            new(Name("FD"), new PdfBoolean(true)),
            new(Name("RT"), new PdfString(","u8, PdfStringForm.Literal)),
            new(Name("RD"), new PdfString("."u8, PdfStringForm.Literal)),
            new(Name("PS"), new PdfString(" "u8, PdfStringForm.Literal)),
            new(Name("SS"), new PdfString(""u8, PdfStringForm.Literal)),
            new(Name("O"), Name("S")));
        _ = new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(WithFormats(new PdfArray([feet, inches])), 0)
            .Build();

        PdfDictionary Format(params KeyValuePair<PdfName, PdfObject>[] additions) =>
            new(new[]
            {
                new KeyValuePair<PdfName, PdfObject>(Name("U"), new PdfString("ft"u8, PdfStringForm.Literal)),
                new KeyValuePair<PdfName, PdfObject>(Name("C"), new PdfInteger(1))
            }.Concat(additions));

        PdfDocument WithFormats(PdfArray formats)
        {
            var viewport = new PdfDictionary([
                new(Name("Type"), Name("Viewport")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(100), new PdfInteger(100)
                ])),
                new(Name("Measure"), new PdfDictionary([
                    new(Name("Type"), Name("Measure")),
                    new(Name("Subtype"), Name("RL")),
                    new(Name("R"), new PdfString("1 in = 1 ft"u8, PdfStringForm.Literal)),
                    new(Name("X"), formats),
                    new(Name("Y"), formats)
                ]))
            ]);
            PdfDictionary page = new(pages[0].Append(
                new KeyValuePair<PdfName, PdfObject>(Name("VP"),
                    new PdfArray([viewport]))));
            return PdfDocument.Open(new PdfIncrementalUpdateBuilder(original)
                .ReplaceObject(references[0].ObjectNumber, page)
                .Build());
        }
    }

    [Fact]
    public void Build_RejectsMalformedImportedViewportPointData()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(source);
        PdfArray square = new([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(1), new PdfInteger(1)
        ]);
        var viewport = new PdfDictionary([
            new(Name("Type"), Name("Viewport")),
            new(Name("BBox"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(100), new PdfInteger(100)
            ])),
            new(Name("Measure"), new PdfDictionary([
                new(Name("Type"), Name("Measure")),
                new(Name("Subtype"), Name("GEO")),
                new(Name("Bounds"), square),
                new(Name("GCS"), new PdfDictionary([
                    new(Name("Type"), Name("GEOGCS")),
                    new(Name("EPSG"), new PdfInteger(4326))
                ])),
                new(Name("GPTS"), square),
                new(Name("LPTS"), square)
            ])),
            new(Name("PtData"), new PdfDictionary([
                new(Name("Type"), Name("PtData")),
                new(Name("Subtype"), Name("Cloud")),
                new(Name("Names"), new PdfArray([Name("LAT"), Name("LON")])),
                new(Name("XPTS"), new PdfArray([
                    new PdfArray([new PdfInteger(1)])
                ]))
            ]))
        ]);
        PdfDictionary invalidPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("VP"),
                new PdfArray([viewport]))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(references[0].ObjectNumber, invalidPage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0).Build());
        Assert.Contains("/PtData /XPTS tuple does not match /Names",
            error.Message, StringComparison.Ordinal);

        PdfDictionary emptyCollectionViewport = new(viewport
            .Where(entry => !entry.Key.Equals(Name("PtData")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("PtData"), new PdfArray([]))));
        PdfDictionary emptyCollectionPage = new(pages[0].Append(
            new KeyValuePair<PdfName, PdfObject>(Name("VP"),
                new PdfArray([emptyCollectionViewport]))));
        PdfDocument emptyCollectionSource = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(PdfDocument.Open(
                    new PdfDocumentBuilder().AddBlankPage(200, 300).Build()))
                .ReplaceObject(references[0].ObjectNumber, emptyCollectionPage)
                .Build());
        InvalidOperationException emptyCollectionError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedPage(emptyCollectionSource, 0).Build());
        Assert.Contains("/PtData collection is empty",
            emptyCollectionError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectStaleStandardCatalogProperties()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfDictionary staleCatalog = new(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("PageLayout"),
                new PdfIndirectReference(
                    pages[0].ObjectNumber, pages[0].Generation + 1))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber, staleCatalog)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source)
                .Build());

        Assert.Contains("catalog /PageLayout value is not a name or resolves to null",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_AcceptAliasedDocumentPartHierarchyLinks()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference root = update.ReserveObject();
        PdfIndirectReference child = update.ReserveObject();
        PdfIndirectReference rootAlias = update.AddObject(root);
        PdfIndirectReference rootOuterAlias = update.AddObject(rootAlias);
        PdfIndirectReference childAlias = update.AddObject(child);
        PdfIndirectReference childOuterAlias = update.AddObject(childAlias);
        PdfIndirectReference pageAlias = update.AddObject(pages[0]);
        update.SetObject(root, new PdfDictionary([
            new(Name("Type"), Name("DPartRootNode")),
            new(Name("DParts"), new PdfArray([
                new PdfArray([childOuterAlias])
            ]))
        ]));
        update.SetObject(child, new PdfDictionary([
            new(Name("Type"), Name("DPart")),
            new(Name("Parent"), rootOuterAlias),
            new(Name("Start"), pageAlias)
        ]));
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("DPartRoot"), rootOuterAlias))));
        PdfDocument aliased = PdfDocument.Open(update.Build());

        PdfDocument imported = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedDocument(aliased)
            .Build());

        Assert.True(ResolveDictionary(imported,
            imported.Trailer[Name("Root")]).ContainsKey(Name("DPartRoot")));
    }

    [Fact]
    public void CompleteDocumentImports_RejectUndefinedPageModeAndLayout()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(original, catalogReference);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        InvalidOperationException modeError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithCatalogValue("PageMode", Name("Continuous")))
                .Build());
        Assert.Contains("/PageMode value /Continuous is not defined", modeError.Message,
            StringComparison.Ordinal);

        InvalidOperationException layoutError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithCatalogValue("PageLayout", Name("Book")))
                .Build());
        Assert.Contains("/PageLayout value /Book is not defined", layoutError.Message,
            StringComparison.Ordinal);

        InvalidOperationException documentPartRootError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithCatalogValue("DPartRoot", new PdfDictionary([
                        new(Name("Type"), Name("DPartRoot"))
                    ])))
                    .Build());
        Assert.Contains("/DPartRoot value is not an indirect reference",
            documentPartRootError.Message, StringComparison.Ordinal);

        InvalidOperationException documentPartRootTypeError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithIndirectCatalogValue("DPartRoot",
                        new PdfDictionary([
                            new(Name("Type"), Name("DPartRoot"))
                        ])))
                    .Build());
        Assert.Contains("has no /Type /DPartRootNode entry",
            documentPartRootTypeError.Message, StringComparison.Ordinal);

        InvalidOperationException documentPartNodeNamesError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithIndirectCatalogValue("DPartRoot",
                        new PdfDictionary([
                            new(Name("Type"), Name("DPartRootNode")),
                            new(Name("NodeNameList"), new PdfArray([
                                new PdfInteger(1)
                            ]))
                        ])))
                    .Build());
        Assert.Contains("/NodeNameList value is not a string array",
            documentPartNodeNamesError.Message, StringComparison.Ordinal);
        return;

        PdfDocument WithCatalogValue(string key, PdfObject value)
        {
            PdfName name = Name(key);
            var update = new PdfIncrementalUpdateBuilder(original);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(name))
                .Append(new KeyValuePair<PdfName, PdfObject>(name, value))));
            return PdfDocument.Open(update.Build());
        }

        PdfDocument WithIndirectCatalogValue(string key, PdfObject value)
        {
            PdfName name = Name(key);
            var update = new PdfIncrementalUpdateBuilder(original);
            PdfIndirectReference valueReference = update.AddObject(value);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(name))
                .Append(new KeyValuePair<PdfName, PdfObject>(name, valueReference))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void CompleteDocumentImports_RejectInvalidWebCaptureInformation()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(original, catalogReference);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        PdfDocument invalidVersion = WithSpiderInfo(new PdfDictionary([
            new(Name("V"), new PdfInteger(1))
        ]));
        InvalidOperationException versionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(invalidVersion)
                .Build());
        Assert.Contains("/SpiderInfo value has no real /V value of 1.0",
            versionError.Message, StringComparison.Ordinal);

        PdfDocument directCommand = WithSpiderInfo(new PdfDictionary([
            new(Name("V"), new PdfReal(1.0)),
            new(Name("C"), new PdfArray([
                new PdfDictionary([
                    new(Name("URL"), new PdfString(
                        "https://example.test"u8, PdfStringForm.Literal))
                ])
            ]))
        ]));
        InvalidOperationException commandError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(directCommand)
                .Build());
        Assert.Contains("/SpiderInfo value /C entry is not an indirect command dictionary",
            commandError.Message, StringComparison.Ordinal);

        var flagUpdate = new PdfIncrementalUpdateBuilder(original);
        PdfIndirectReference invalidFlagCommand = flagUpdate.AddObject(new PdfDictionary([
            new(Name("URL"), new PdfString(
                "https://example.test"u8, PdfStringForm.Literal)),
            new(Name("F"), new PdfInteger(8))
        ]));
        flagUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("SpiderInfo")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("SpiderInfo"),
                    new PdfDictionary([
                        new(Name("V"), new PdfReal(1.0)),
                        new(Name("C"), new PdfArray([invalidFlagCommand]))
                    ])))));
        PdfDocument invalidFlags = PdfDocument.Open(flagUpdate.Build());
        InvalidOperationException flagError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(invalidFlags)
                .Build());
        Assert.Contains("/F value uses undefined Web Capture flags",
            flagError.Message, StringComparison.Ordinal);
        return;

        PdfDocument WithSpiderInfo(PdfDictionary information)
        {
            var update = new PdfIncrementalUpdateBuilder(original);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("SpiderInfo")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("SpiderInfo"), information))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void CompleteDocumentImports_RejectInvalidUriAndMarkInfoDictionaries()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(original, catalogReference);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        InvalidOperationException uriError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithCatalogValue("URI", new PdfDictionary([
                    new(Name("Base"), new PdfInteger(17))
                ])))
                .Build());
        Assert.Contains("/URI value /Base is not a string", uriError.Message,
            StringComparison.Ordinal);

        InvalidOperationException markInfoError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithCatalogValue("MarkInfo", new PdfDictionary([
                    new(Name("UserProperties"), Name("Yes"))
                ])))
                .Build());
        Assert.Contains("/MarkInfo value /UserProperties is not a boolean",
            markInfoError.Message, StringComparison.Ordinal);
        return;

        PdfDocument WithCatalogValue(string key, PdfObject value)
        {
            PdfName name = Name(key);
            var update = new PdfIncrementalUpdateBuilder(original);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(name))
                .Append(new KeyValuePair<PdfName, PdfObject>(name, value))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void CompleteDocumentImports_RejectReusedFinalActionsBehindDistinctAliases()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference child = update.AddObject(new PdfDictionary([
            new(Name("S"), Name("Named")),
            new(Name("N"), Name("PrevPage"))
        ]));
        PdfIndirectReference firstAlias = update.AddObject(child);
        PdfIndirectReference secondAlias = update.AddObject(child);
        PdfIndirectReference action = update.AddObject(new PdfDictionary([
            new(Name("S"), Name("Named")),
            new(Name("N"), Name("NextPage")),
            new(Name("Next"), new PdfArray([firstAlias, secondAlias]))
        ]));
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("OpenAction"), action))));
        PdfDocument aliased = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(aliased)
                .Build());

        Assert.Contains("cycle or reused action", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectInvalidOpenActions()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(original, catalogReference);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        InvalidOperationException destinationError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithOpenAction(new PdfArray([])))
                .Build());
        Assert.Contains("empty destination array", destinationError.Message,
            StringComparison.Ordinal);

        PdfIndirectReference pageReference = FlatPages(original).References[0];
        InvalidOperationException operandError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithOpenAction(new PdfArray([
                    pageReference, Name("FitR"), new PdfInteger(0)
                ])))
                .Build());
        Assert.Contains("destination /FitR has an invalid operand count",
            operandError.Message, StringComparison.Ordinal);

        InvalidOperationException actionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithOpenAction(new PdfDictionary([
                    new(Name("Type"), Name("Action"))
                ])))
                .Build());
        Assert.Contains("has no valid /S name", actionError.Message,
            StringComparison.Ordinal);

        InvalidOperationException uriActionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithOpenAction(new PdfDictionary([
                    new(Name("S"), Name("URI"))
                ])))
                .Build());
        Assert.Contains("/URI action has no valid /URI string",
            uriActionError.Message, StringComparison.Ordinal);

        InvalidOperationException goToActionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(WithOpenAction(new PdfDictionary([
                    new(Name("S"), Name("GoTo")),
                    new(Name("D"), new PdfArray([pageReference, Name("FitV")]))
                ])))
                .Build());
        Assert.Contains("/GoTo /D value /FitV has an invalid operand count",
            goToActionError.Message, StringComparison.Ordinal);

        InvalidOperationException structureDestinationError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithOpenAction(new PdfDictionary([
                        new(Name("S"), Name("GoTo")),
                        new(Name("D"), new PdfArray([pageReference, Name("Fit")])),
                        new(Name("SD"), new PdfInteger(7))
                    ])))
                    .Build());
        Assert.Contains("/GoTo /SD value is not an array",
            structureDestinationError.Message, StringComparison.Ordinal);

        InvalidOperationException undefinedActionError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithOpenAction(new PdfDictionary([
                        new(Name("S"), Name("Teleport"))
                    ])))
                    .Build());
        Assert.Contains("undefined action subtype /Teleport",
            undefinedActionError.Message, StringComparison.Ordinal);

        InvalidOperationException documentPartActionError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithOpenAction(new PdfDictionary([
                        new(Name("S"), Name("GoToDp")),
                        new(Name("Dp"), new PdfDictionary([
                            new(Name("Type"), Name("DPart"))
                        ]))
                    ])))
                    .Build());
        Assert.Contains("/GoToDp action has no indirect typed /Dp document part",
            documentPartActionError.Message, StringComparison.Ordinal);

        InvalidOperationException launchActionError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithOpenAction(new PdfDictionary([
                        new(Name("S"), Name("Launch")),
                        new(Name("F"), new PdfString("tool.exe"u8, PdfStringForm.Literal)),
                        new(Name("Win"), new PdfDictionary([
                            new(Name("F"), new PdfInteger(7))
                        ]))
                    ])))
                    .Build());
        Assert.Contains("/Launch /Win has no /F string",
            launchActionError.Message, StringComparison.Ordinal);

        InvalidOperationException embeddedTargetError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithOpenAction(new PdfDictionary([
                        new(Name("S"), Name("GoToE")),
                        new(Name("D"), new PdfString("chapter"u8, PdfStringForm.Literal)),
                        new(Name("T"), new PdfDictionary([
                            new(Name("R"), Name("Sibling"))
                        ]))
                    ])))
                    .Build());
        Assert.Contains("/GoToE /T value has no defined /R relationship",
            embeddedTargetError.Message, StringComparison.Ordinal);

        InvalidOperationException namedActionError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithOpenAction(new PdfDictionary([
                        new(Name("S"), Name("Named")),
                        new(Name("N"), Name("DeleteEverything"))
                    ])))
                    .Build());
        Assert.Contains("/Named /N value /DeleteEverything is not defined",
            namedActionError.Message, StringComparison.Ordinal);

        InvalidOperationException richMediaCommandError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(WithInvalidRichMediaCommand())
                    .Build());
        Assert.Contains("/RichMediaExecute /CMD has no /C string",
            richMediaCommandError.Message, StringComparison.Ordinal);

        var invalidAdditionalActions = new PdfDictionary([
            new(Name("WC"), new PdfDictionary([
                new(Name("Type"), Name("Action"))
            ]))
        ]);
        var additionalActionsUpdate = new PdfIncrementalUpdateBuilder(original);
        additionalActionsUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Append(new KeyValuePair<PdfName, PdfObject>(
                Name("AA"), invalidAdditionalActions))));
        PdfDocument invalidAdditionalActionsDocument = PdfDocument.Open(
            additionalActionsUpdate.Build());
        InvalidOperationException additionalActionError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(empty)
                    .AddImportedDocument(invalidAdditionalActionsDocument)
                    .Build());
        Assert.Contains("/AA value /WC entry has no valid /S name",
            additionalActionError.Message, StringComparison.Ordinal);
        return;

        PdfDocument WithOpenAction(PdfObject value)
        {
            var update = new PdfIncrementalUpdateBuilder(original);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("OpenAction")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("OpenAction"), value))));
            return PdfDocument.Open(update.Build());
        }

        PdfDocument WithInvalidRichMediaCommand()
        {
            var update = new PdfIncrementalUpdateBuilder(original);
            PdfIndirectReference annotation = update.AddObject(new PdfDictionary([
                new(Name("Type"), Name("Annot")),
                new(Name("Subtype"), Name("RichMedia"))
            ]));
            var action = new PdfDictionary([
                new(Name("S"), Name("RichMediaExecute")),
                new(Name("TA"), annotation),
                new(Name("CMD"), new PdfDictionary([
                    new(Name("Type"), Name("RichMediaCommand")),
                    new(Name("C"), Name("play"))
                ]))
            ]);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("OpenAction")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("OpenAction"), action))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void CompleteDocumentImports_RejectStaleDocumentInformation()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference information = update.AddObject(new PdfDictionary([
            new(Name("Title"), new PdfString("stale"u8, PdfStringForm.Literal))
        ]));
        string updatedText = Encoding.Latin1.GetString(
            update.SetDocumentInformation(information).Build());
        updatedText = updatedText.Replace(
            $"/Info {information.ObjectNumber} 0 R",
            $"/Info {information.ObjectNumber} 1 R",
            StringComparison.Ordinal);
        source = PdfDocument.Open(Encoding.Latin1.GetBytes(updatedText));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source)
                .Build());

        Assert.Contains("trailer /Info value is not a dictionary or resolves to null",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectInvalidStandardDocumentInformation()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .Build());
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        PdfDocument invalidTitle = WithInformation(new PdfDictionary([
            new(Name("Title"), new PdfString("17"u8, PdfStringForm.Literal))
        ]), "/Title (17)", "/Title  17 ");
        InvalidOperationException titleError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(invalidTitle)
                .Build());
        Assert.Contains("/Info value /Title is not a string", titleError.Message,
            StringComparison.Ordinal);

        PdfDocument invalidTrapped = WithInformation(new PdfDictionary([
            new(Name("Trapped"), Name("Unknown"))
        ]), "/Trapped /Unknown", "/Trapped /Maybe  ");
        InvalidOperationException trappedError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(invalidTrapped)
                .Build());
        Assert.Contains("/Info value /Trapped /Maybe is not defined", trappedError.Message,
            StringComparison.Ordinal);
        return;

        PdfDocument WithInformation(
            PdfDictionary information, string validSyntax, string invalidSyntax)
        {
            var update = new PdfIncrementalUpdateBuilder(original);
            PdfIndirectReference reference = update.AddObject(information);
            update.SetDocumentInformation(reference);
            string sourceText = Encoding.Latin1.GetString(update.Build())
                .Replace(validSyntax, invalidSyntax, StringComparison.Ordinal);
            return PdfDocument.Open(Encoding.Latin1.GetBytes(sourceText));
        }
    }

    [Fact]
    public void Build_ImportsAuthenticatedEncryptedPageIntoEncryptedDestination()
    {
        byte[] sourceBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "source-user", OwnerPassword = "source-owner"
            })
            .AddPage(200, 300, "q 10 20 30 40 re f Q"u8.ToArray()).Build();
        byte[] targetBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "target-user", OwnerPassword = "target-owner"
            })
            .AddBlankPage(100, 100).Build();

        byte[] result = new PdfIncrementalPageEditor(
                PdfDocument.Open(targetBytes, "target-owner"))
            .AddImportedPage(PdfDocument.Open(sourceBytes, "source-user"), 0)
            .Build(StructuralOptions());
        PdfDocument reopened = PdfDocument.Open(result, "target-user");
        (_, _, PdfDictionary[] pages) = FlatPages(reopened);
        PdfStream content = ResolveStream(reopened, pages[1][Name("Contents")]);

        Assert.Equal("q 10 20 30 40 re f Q",
            Encoding.ASCII.GetString(PdfStreamDecoder.Decode(content)));
        Assert.True(reopened.CrossReferences.Sections[0].IsStream);
        Assert.Contains(reopened.CrossReferences.Sections[0].Values,
            entry => entry.Type == PdfCrossReferenceEntryType.Compressed);
        Assert.Equal(-1, result.AsSpan().IndexOf("q 10 20 30 40 re f Q"u8));
    }

    [Fact]
    public void Build_ImportsEncryptedSelectedPageBatchAcrossDistinctKeys()
    {
        byte[] sourceBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "source-user", OwnerPassword = "source-owner"
            })
            .AddPage(200, 300, "q 1 2 30 40 re f Q"u8.ToArray())
            .AddPage(300, 400, "q 5 6 70 80 re f Q"u8.ToArray())
            .Build();
        byte[] targetBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "target-user", OwnerPassword = "target-owner"
            })
            .AddBlankPage(100, 100)
            .Build();

        byte[] result = new PdfIncrementalPageEditor(
                PdfDocument.Open(targetBytes, "target-owner"))
            .AddImportedPages(PdfDocument.Open(sourceBytes, "source-user"), [1, 0])
            .Build(StructuralOptions());
        PdfDocument reopened = PdfDocument.Open(result, "target-user");
        (_, _, PdfDictionary[] pages) = FlatPages(reopened);

        Assert.Equal("q 5 6 70 80 re f Q", Encoding.ASCII.GetString(
            PdfStreamDecoder.Decode(ResolveStream(reopened, pages[1][Name("Contents")]))));
        Assert.Equal("q 1 2 30 40 re f Q", Encoding.ASCII.GetString(
            PdfStreamDecoder.Decode(ResolveStream(reopened, pages[2][Name("Contents")]))));
        Assert.Equal(-1, result.AsSpan().IndexOf("q 5 6 70 80 re f Q"u8));
        Assert.Equal(-1, result.AsSpan().IndexOf("q 1 2 30 40 re f Q"u8));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            PdfDocument.Open(result, "source-user"));
    }

    [Fact]
    public void Build_RejectsUnauthenticatedEncryptedImportSource()
    {
        byte[] sourceBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "user", OwnerPassword = "owner"
            }).AddBlankPage().Build();
        PdfDocument target = PdfDocument.Open(new PdfDocumentBuilder().Build());

        Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedPage(PdfDocument.Open(sourceBytes), 0).Build());
    }

    [Fact]
    public void Build_PreservesCompleteTaggedDocumentStructureAndAccessibilityMetadata()
    {
        var content = new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Figure, 0)
            .Rectangle(10, 10, 20, 20).Fill()
            .EndMarkedContent();
        byte[] sourceBytes = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Imported accessible document",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddPage(100, 100, content)
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
                alternateDescription: "A square")
            .Build();
        byte[] targetBytes = new PdfDocumentBuilder().Build();

        byte[] result = new PdfIncrementalPageEditor(PdfDocument.Open(targetBytes))
            .AddImportedDocument(PdfDocument.Open(sourceBytes))
            .Build();
        PdfDocument reopened = PdfDocument.Open(result);
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary structureRoot = ResolveDictionary(reopened, catalog[Name("StructTreeRoot")]);
        PdfDictionary markInfo = Assert.IsType<PdfDictionary>(catalog[Name("MarkInfo")]);
        PdfDictionary viewerPreferences = Assert.IsType<PdfDictionary>(
            catalog[Name("ViewerPreferences")]);
        PdfStream metadata = ResolveStream(reopened, catalog[Name("Metadata")]);
        PdfDictionary page = FlatPages(reopened).Pages[0];
        PdfDictionary parentTree = ResolveDictionary(reopened, structureRoot[Name("ParentTree")]);
        PdfArray parentNumbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);
        PdfArray mapping = Assert.IsType<PdfArray>(parentNumbers[1]);
        PdfDictionary documentElement = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(structureRoot[Name("K")])[0]);
        PdfIndirectReference figureReference = Assert.IsType<PdfIndirectReference>(
            documentElement[Name("K")]);
        PdfDictionary figure = ResolveDictionary(reopened, figureReference);

        Assert.True(result.AsSpan(0, targetBytes.Length).SequenceEqual(targetBytes));
        Assert.True(Assert.IsType<PdfBoolean>(markInfo[Name("Marked")]).Value);
        Assert.True(Assert.IsType<PdfBoolean>(
            viewerPreferences[Name("DisplayDocTitle")]).Value);
        Assert.Equal("en-US", DecodeUnicode(Assert.IsType<PdfString>(catalog[Name("Lang")])));
        Assert.Contains("pdfuaid:part", Encoding.UTF8.GetString(metadata.EncodedData.Span));
        Assert.Equal(0, Assert.IsType<PdfInteger>(page[Name("StructParents")]).Value);
        Assert.Equal(0, Assert.IsType<PdfInteger>(parentNumbers[0]).Value);
        Assert.Equal(Assert.IsType<PdfIndirectReference>(mapping[0]).ObjectNumber,
            figureReference.ObjectNumber);
        Assert.Equal(FlatPages(reopened).References[0].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(figure[Name("Pg")]).ObjectNumber);
        Assert.Equal("A square", DecodeUnicode(Assert.IsType<PdfString>(figure[Name("Alt")])));
    }

    [Fact]
    public void TaggedImports_SupportSelectedAndCombinedPageSetsButRejectUntaggedDestinations()
    {
        PdfDocument tagged = PdfDocument.Open(BuildTaggedDocument());
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());
        PdfDocument occupied = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument selected = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty).AddImportedPage(tagged, 0).Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selected, selected.Trailer[Name("Root")]);
        PdfDictionary selectedRoot = ResolveDictionary(
            selected, selectedCatalog[Name("StructTreeRoot")]);
        PdfArray selectedParents = Assert.IsType<PdfArray>(DictionaryValue(
            selected, selectedRoot[Name("ParentTree")])[Name("Nums")]);
        Assert.Equal(2, selectedParents.Count);
        Assert.True(Assert.IsType<PdfBoolean>(Assert.IsType<PdfDictionary>(
            selectedCatalog[Name("MarkInfo")])[Name("Marked")]).Value);

        PdfDocument reducedImport = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(tagged).RemovePage(1).Build());
        Assert.True(ResolveDictionary(reducedImport, reducedImport.Trailer[Name("Root")])
            .ContainsKey(Name("StructTreeRoot")));
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(tagged).Build());
        PdfDocument combined = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(tagged)
                .AddImportedDocument(tagged)
                .Build());
        Assert.Equal(4, FlatPages(combined).Pages.Length);

        PdfDocument mixed = PdfDocument.Open(BuildTaggedDocument());
        (_, PdfIndirectReference[] mixedReferences, PdfDictionary[] mixedPages) = FlatPages(mixed);
        var update = new PdfIncrementalUpdateBuilder(mixed);
        update.ReplaceObject(mixedReferences[1].ObjectNumber, new PdfDictionary(
            mixedPages[1].Where(entry => !entry.Key.Equals(Name("StructParents")))));
        PdfDocument independentPageSource = PdfDocument.Open(update.Build());
        PdfDocument independentPage = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(independentPageSource, 1)
                .Build());
        PdfDictionary independentCatalog = ResolveDictionary(
            independentPage, independentPage.Trailer[Name("Root")]);
        Assert.False(independentCatalog.ContainsKey(Name("StructTreeRoot")));
        Assert.False(independentCatalog.ContainsKey(Name("MarkInfo")));
        Assert.False(FlatPages(independentPage).Pages[0].ContainsKey(Name("StructParents")));
    }

    [Fact]
    public void TaggedImports_MergeStructureKidsParentTreeAndPageKeys()
    {
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(merged, rootReference);
        PdfArray rootKids = Assert.IsType<PdfArray>(root[Name("K")]);
        PdfDictionary documentElement = ResolveDictionary(merged, rootKids[0]);
        PdfArray documentKids = Assert.IsType<PdfArray>(documentElement[Name("K")]);
        PdfDictionary parentTree = ResolveDictionary(merged, root[Name("ParentTree")]);
        PdfArray numbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);
        (_, _, PdfDictionary[] pages) = FlatPages(merged);

        Assert.Equal(4, pages.Length);
        Assert.Single(rootKids);
        Assert.Equal(4, documentKids.Count);
        Assert.Equal(rootReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(documentElement[Name("P")]).ObjectNumber);
        Assert.Equal([0L, 1L, 2L, 3L], Enumerable.Range(0, numbers.Count / 2)
            .Select(index => Assert.IsType<PdfInteger>(numbers[index * 2]).Value));
        Assert.Equal(2, Assert.IsType<PdfInteger>(pages[2][Name("StructParents")]).Value);
        Assert.Equal(3, Assert.IsType<PdfInteger>(pages[3][Name("StructParents")]).Value);
        Assert.Equal(4, Assert.IsType<PdfInteger>(
            root[Name("ParentTreeNextKey")]).Value);
    }

    [Fact]
    public void TaggedImports_ResolveIndirectStructureParentKeys()
    {
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary targetCatalog = ResolveDictionary(target, target.Trailer[Name("Root")]);
        PdfIndirectReference targetRootReference = Assert.IsType<PdfIndirectReference>(
            targetCatalog[Name("StructTreeRoot")]);
        PdfDictionary targetRoot = ResolveDictionary(target, targetRootReference);
        var targetUpdate = new PdfIncrementalUpdateBuilder(target);
        PdfIndirectReference nextKeyReference = targetUpdate.AddObject(new PdfInteger(2));
        PdfIndirectReference parentTreeReference = Assert.IsType<PdfIndirectReference>(
            targetRoot[Name("ParentTree")]);
        PdfIndirectReference parentTreeAlias = targetUpdate.AddObject(parentTreeReference);
        PdfIndirectReference parentTreeOuterAlias = targetUpdate.AddObject(parentTreeAlias);
        PdfIndirectReference documentReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(targetRoot[Name("K")])[0]);
        PdfIndirectReference documentAlias = targetUpdate.AddObject(documentReference);
        PdfIndirectReference documentOuterAlias = targetUpdate.AddObject(documentAlias);
        targetUpdate.ReplaceObject(targetRootReference.ObjectNumber, new PdfDictionary(
            targetRoot.Select(entry => entry.Key.Equals(Name("ParentTreeNextKey"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, nextKeyReference)
                : entry.Key.Equals(Name("ParentTree"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, parentTreeOuterAlias)
                    : entry.Key.Equals(Name("K"))
                        ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                            new PdfArray([documentOuterAlias]))
                        : entry)));
        PdfIndirectReference rootAlias = targetUpdate.AddObject(targetRootReference);
        PdfIndirectReference rootOuterAlias = targetUpdate.AddObject(rootAlias);
        PdfIndirectReference targetCatalogReference = Assert.IsType<PdfIndirectReference>(
            target.Trailer[Name("Root")]);
        targetUpdate.ReplaceObject(targetCatalogReference.ObjectNumber,
            new PdfDictionary(targetCatalog.Select(entry =>
                entry.Key.Equals(Name("StructTreeRoot"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, rootOuterAlias)
                    : entry)));
        target = PdfDocument.Open(targetUpdate.Build());

        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        (_, PdfIndirectReference[] sourcePageReferences, PdfDictionary[] sourcePages) =
            FlatPages(source);
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference firstKeyReference = sourceUpdate.AddObject(new PdfInteger(0));
        sourceUpdate.ReplaceObject(sourcePageReferences[0].ObjectNumber, new PdfDictionary(
            sourcePages[0].Where(entry => !entry.Key.Equals(Name("StructParents"))).Append(
                new KeyValuePair<PdfName, PdfObject>(
                    Name("StructParents"), firstKeyReference))));
        source = PdfDocument.Open(sourceUpdate.Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(source).Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);
        PdfDictionary mergedRoot = ResolveDictionary(
            merged, mergedCatalog[Name("StructTreeRoot")]);
        (_, _, PdfDictionary[] mergedPages) = FlatPages(merged);

        Assert.Equal(4, Assert.IsType<PdfInteger>(
            mergedRoot[Name("ParentTreeNextKey")]).Value);
        Assert.Equal(2, Assert.IsType<PdfInteger>(
            mergedPages[2][Name("StructParents")]).Value);
        Assert.Equal(rootOuterAlias.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(mergedCatalog[Name("StructTreeRoot")]).ObjectNumber);
        Assert.Equal(parentTreeOuterAlias.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(mergedRoot[Name("ParentTree")]).ObjectNumber);
        Assert.Equal(documentOuterAlias.ObjectNumber, Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(mergedRoot[Name("K")])[0]).ObjectNumber);
    }

    [Fact]
    public void TaggedImports_MergeSelectedAndReorderedTaggedPages()
    {
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedPage(source, 1).Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);
        PdfDictionary mergedRoot = ResolveDictionary(
            merged, mergedCatalog[Name("StructTreeRoot")]);
        PdfArray mergedParents = Assert.IsType<PdfArray>(DictionaryValue(
            merged, mergedRoot[Name("ParentTree")])[Name("Nums")]);
        (_, _, PdfDictionary[] mergedPages) = FlatPages(merged);

        Assert.Equal([0L, 1L, 2L], Enumerable.Range(0, mergedParents.Count / 2)
            .Select(index => Assert.IsType<PdfInteger>(mergedParents[index * 2]).Value));
        Assert.Equal([0L, 1L, 2L], mergedPages.Select(page =>
            Assert.IsType<PdfInteger>(page[Name("StructParents")]).Value));

        PdfDocument reordered = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPages(source, [1, 0]).Build());
        (_, _, PdfDictionary[] reorderedPages) = FlatPages(reordered);
        Assert.Equal([1L, 0L], reorderedPages.Select(page =>
            Assert.IsType<PdfInteger>(page[Name("StructParents")]).Value));
        Assert.True(ResolveDictionary(reordered, reordered.Trailer[Name("Root")])
            .ContainsKey(Name("StructTreeRoot")));
    }

    [Fact]
    public void TaggedImports_ImportSelectedPageAcrossDistinctEncryptionKeys()
    {
        byte[] sourceBytes = BuildTaggedDocument("source-user", "source-owner");
        byte[] targetBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "target-user", OwnerPassword = "target-owner"
            })
            .Build();

        byte[] result = new PdfIncrementalPageEditor(
                PdfDocument.Open(targetBytes, "target-owner"))
            .AddImportedPage(PdfDocument.Open(sourceBytes, "source-user"), 1)
            .Build();
        PdfDocument reopened = PdfDocument.Open(result, "target-user");
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(reopened, catalog[Name("StructTreeRoot")]);
        PdfArray parents = Assert.IsType<PdfArray>(DictionaryValue(
            reopened, root[Name("ParentTree")])[Name("Nums")]);

        Assert.Equal(2, parents.Count);
        Assert.True(Assert.IsType<PdfBoolean>(Assert.IsType<PdfDictionary>(
            catalog[Name("MarkInfo")])[Name("Marked")]).Value);
        Assert.Equal(-1, result.AsSpan().IndexOf("Selected square"u8));
    }

    [Fact]
    public void TaggedImports_RenameCollidingRoleAndClassMapEntries()
    {
        PdfDocument target = AddMaps(PdfDocument.Open(BuildTaggedDocument()), "P", "Layout");
        PdfDocument source = AddMaps(PdfDocument.Open(BuildTaggedDocument()), "Figure", "Table");

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(merged, catalog[Name("StructTreeRoot")]);
        PdfDictionary roleMap = Assert.IsType<PdfDictionary>(root[Name("RoleMap")]);
        PdfDictionary classMap = Assert.IsType<PdfDictionary>(root[Name("ClassMap")]);
        PdfDictionary document = ResolveDictionary(merged,
            Assert.IsType<PdfArray>(root[Name("K")])[0]);
        PdfArray children = Assert.IsType<PdfArray>(document[Name("K")]);
        PdfDictionary importedFigure = ResolveDictionary(merged, children[2]);

        Assert.Equal("P", Assert.IsType<PdfName>(roleMap[Name("Custom")]).ValueAsLatin1());
        Assert.Equal("Figure", Assert.IsType<PdfName>(
            roleMap[Name("KPRole1")]).ValueAsLatin1());
        Assert.True(classMap.ContainsKey(Name("Style")));
        Assert.True(classMap.ContainsKey(Name("KPClass1")));
        Assert.Equal("KPRole1", Assert.IsType<PdfName>(
            importedFigure[Name("S")]).ValueAsLatin1());
        Assert.Equal("KPClass1", Assert.IsType<PdfName>(Assert.IsType<PdfArray>(
            importedFigure[Name("C")])[0]).ValueAsLatin1());

        static PdfDocument AddMaps(PdfDocument document, string role, string owner)
        {
            PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
            PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
                catalog[Name("StructTreeRoot")]);
            PdfDictionary root = ResolveDictionary(document, rootReference);
            PdfIndirectReference topReference = Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfArray>(root[Name("K")])[0]);
            PdfDictionary top = ResolveDictionary(document, topReference);
            PdfArray children = Assert.IsType<PdfArray>(top[Name("K")]);
            var update = new PdfIncrementalUpdateBuilder(document);
            bool indirect = owner == "Table";
            if (indirect)
            {
                PdfIndirectReference documentRole = update.AddObject(
                    update.AddObject(Name("Document")));
                update.ReplaceObject(topReference.ObjectNumber, new PdfDictionary(top
                    .Where(entry => !entry.Key.Equals(Name("S")))
                    .Append(new KeyValuePair<PdfName, PdfObject>(
                        Name("S"), documentRole))));
            }
            PdfObject rootType = indirect
                ? update.AddObject(update.AddObject(Name("StructTreeRoot")))
                : root[Name("Type")];
            update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root
                .Where(entry => !entry.Key.Equals(Name("Type")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Type"), rootType))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("RoleMap"),
                    new PdfDictionary([new(Name("Custom"), Name(role))])))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("ClassMap"),
                    new PdfDictionary([new(Name("Style"),
                        new PdfDictionary([new(Name("O"), Name(owner))]))])))));
            foreach (PdfIndirectReference childReference in children
                         .Select(Assert.IsType<PdfIndirectReference>))
            {
                PdfDictionary child = ResolveDictionary(document, childReference);
                PdfObject childRole = indirect
                    ? update.AddObject(update.AddObject(Name("Custom"))) : Name("Custom");
                PdfObject childClass = indirect
                    ? update.AddObject(new PdfArray([
                        update.AddObject(update.AddObject(Name("Style"))) ]))
                    : new PdfArray([Name("Style")]);
                update.ReplaceObject(childReference.ObjectNumber, new PdfDictionary(child
                    .Where(entry => !entry.Key.Equals(Name("S")))
                    .Append(new KeyValuePair<PdfName, PdfObject>(Name("S"), childRole))
                    .Append(new KeyValuePair<PdfName, PdfObject>(Name("C"), childClass))));
            }
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void TaggedImports_PreserveDistinctStructureRootExtensionsAndRejectCollisions()
    {
        PdfDocument target = AddRootExtension(
            PdfDocument.Open(BuildTaggedDocument()), "TargetData", "target");
        PdfDocument source = AddRootExtension(
            PdfDocument.Open(BuildTaggedDocument()), "SourceData", "source");

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(merged, catalog[Name("StructTreeRoot")]);

        PdfDictionary targetData = ResolveDictionary(merged, root[Name("TargetData")]);
        PdfDictionary sourceData = ResolveDictionary(merged, root[Name("SourceData")]);
        Assert.Equal("target", Encoding.Latin1.GetString(
            Assert.IsType<PdfString>(targetData[Name("Marker")]).Bytes.Span));
        Assert.Equal("source", Encoding.Latin1.GetString(
            Assert.IsType<PdfString>(sourceData[Name("Marker")]).Bytes.Span));
        PdfIndirectReference mergedRootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfIndirectReference importedRootBackReference = Assert.IsType<PdfIndirectReference>(
            sourceData[Name("Root")]);
        Assert.Equal(mergedRootReference.ObjectNumber, importedRootBackReference.ObjectNumber);
        Assert.Equal(mergedRootReference.Generation, importedRootBackReference.Generation);
        PdfIndirectReference mergedDocumentReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(root[Name("K")])[0]);
        PdfIndirectReference importedDocumentBackReference = Assert.IsType<PdfIndirectReference>(
            sourceData[Name("Document")]);
        Assert.Equal(mergedDocumentReference.ObjectNumber,
            importedDocumentBackReference.ObjectNumber);
        Assert.Equal(mergedDocumentReference.Generation,
            importedDocumentBackReference.Generation);

        PdfDocument conflicting = AddRootExtension(
            PdfDocument.Open(BuildTaggedDocument()), "TargetData", "other");
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(target).AddImportedDocument(conflicting).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0).Build());

        PdfDictionary sourceCatalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference sourceRootReference = Assert.IsType<PdfIndirectReference>(
            sourceCatalog[Name("StructTreeRoot")]);
        PdfDictionary sourceRoot = ResolveDictionary(source, sourceRootReference);
        (_, PdfIndirectReference[] sourcePages, _) = FlatPages(source);
        PdfDocument staleExtension = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(source)
                .ReplaceObject(sourceRootReference.ObjectNumber,
                    new PdfDictionary(sourceRoot.Append(
                        new KeyValuePair<PdfName, PdfObject>(Name("StaleData"),
                            new PdfIndirectReference(sourcePages[0].ObjectNumber,
                                sourcePages[0].Generation + 1)))))
                .Build());
        InvalidOperationException staleError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(staleExtension).Build());
        Assert.Contains("source /StructTreeRoot /StaleData extension resolves to null",
            staleError.Message, StringComparison.Ordinal);

        static PdfDocument AddRootExtension(
            PdfDocument document, string key, string value)
        {
            PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
            PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
                catalog[Name("StructTreeRoot")]);
            PdfDictionary root = ResolveDictionary(document, rootReference);
            PdfIndirectReference documentReference = Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfArray>(root[Name("K")])[0]);
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfIndirectReference extension = update.AddObject(new PdfDictionary([
                new(Name("Marker"), new PdfString(
                    Encoding.Latin1.GetBytes(value), PdfStringForm.Literal)),
                new(Name("Root"), rootReference),
                new(Name("Document"), documentReference)
            ]));
            update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Append(
                new KeyValuePair<PdfName, PdfObject>(Name(key), extension))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void TaggedSubsetImport_DoesNotCopySourceCatalogExtensions()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference extension = sourceUpdate.AddObject(new PdfDictionary([
            new(Name("BaseVersion"), Name("2.0")),
            new(Name("ExtensionLevel"), new PdfInteger(7))
        ]));
        PdfIndirectReference sourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary sourceCatalog = ResolveDictionary(source, sourceCatalogReference);
        sourceUpdate.ReplaceObject(sourceCatalogReference.ObjectNumber,
            new PdfDictionary(sourceCatalog.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Extensions"),
                    new PdfDictionary([new(Name("Vendor"), extension)])))));
        source = PdfDocument.Open(sourceUpdate.Build());

        PdfDocument selected = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0).Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selected, selected.Trailer[Name("Root")]);

        Assert.False(selectedCatalog.ContainsKey(Name("Extensions")));
        Assert.True(selectedCatalog.ContainsKey(Name("StructTreeRoot")));
        Assert.True(selectedCatalog.ContainsKey(Name("Metadata")));
    }

    [Fact]
    public void TaggedSubsetImport_RemovesStaleOutputIntents()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
            .Where(entry => !entry.Key.Equals(Name("OutputIntents")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("OutputIntents"),
                new PdfArray([new PdfIndirectReference(
                    pages[0].ObjectNumber, pages[0].Generation + 1)])))));
        source = PdfDocument.Open(update.Build());

        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selected, selected.Trailer[Name("Root")]);

        Assert.False(selectedCatalog.ContainsKey(Name("OutputIntents")));
        Assert.True(selectedCatalog.ContainsKey(Name("Metadata")));
    }

    [Fact]
    public void CompleteDocumentImports_PreserveMultiHopIndirectCatalogValues()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(original, catalogReference);
        var update = new PdfIncrementalUpdateBuilder(original);

        PdfIndirectReference subtype = update.AddObject(Name("GTS_PDFX"));
        PdfIndirectReference subtypeAlias = update.AddObject(subtype);
        PdfIndirectReference intent = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("OutputIntent")),
            new(Name("S"), subtypeAlias)
        ]));
        PdfIndirectReference intentAlias = update.AddObject(intent);
        PdfIndirectReference intents = update.AddObject(new PdfArray([intentAlias]));
        PdfIndirectReference intentsAlias = update.AddObject(intents);

        PdfIndirectReference displayTitle = update.AddObject(new PdfBoolean(true));
        PdfIndirectReference displayTitleAlias = update.AddObject(displayTitle);
        PdfIndirectReference preferences = update.AddObject(new PdfDictionary([
            new(Name("DisplayDocTitle"), displayTitleAlias)
        ]));
        PdfIndirectReference preferencesAlias = update.AddObject(preferences);

        update.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("OutputIntents"), intentsAlias))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("ViewerPreferences"), preferencesAlias))));
        PdfDocument source = PdfDocument.Open(update.Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedDocument(source)
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);
        PdfArray mergedIntents = Assert.IsType<PdfArray>(
            ResolveFully(merged, mergedCatalog[Name("OutputIntents")]));
        PdfDictionary mergedIntent = Assert.IsType<PdfDictionary>(
            ResolveFully(merged, mergedIntents[0]));
        PdfDictionary mergedPreferences = Assert.IsType<PdfDictionary>(
            ResolveFully(merged, mergedCatalog[Name("ViewerPreferences")]));

        Assert.Equal("GTS_PDFX", Assert.IsType<PdfName>(
            ResolveFully(merged, mergedIntent[Name("S")])).ValueAsLatin1());
        Assert.True(Assert.IsType<PdfBoolean>(ResolveFully(
            merged, mergedPreferences[Name("DisplayDocTitle")])).Value);

        static PdfObject ResolveFully(PdfDocument document, PdfObject value)
        {
            while (value is PdfIndirectReference reference)
                value = document.Resolve(reference);
            return value;
        }
    }

    [Fact]
    public void CompleteDocumentImports_RejectInvalidOutputIntents()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfDocument source = original;
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary invalidIntent = new([
            new(Name("Type"), Name("OutputIntent")),
            new(Name("S"), new PdfInteger(7))
        ]);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("OutputIntents"),
                    new PdfArray([invalidIntent])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("/OutputIntents entry has no valid /S name",
            error.Message, StringComparison.Ordinal);

        var profileUpdate = new PdfIncrementalUpdateBuilder(original);
        PdfIndirectReference invalidProfile = profileUpdate.AddObject(new PdfStream(
            new PdfDictionary([]), BuildRgbProfile()));
        var profileIntent = new PdfDictionary([
            new(Name("Type"), Name("OutputIntent")),
            new(Name("S"), Name("GTS_PDFA1")),
            new(Name("DestOutputProfile"), invalidProfile)
        ]);
        profileUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Append(new KeyValuePair<PdfName, PdfObject>(
                Name("OutputIntents"), new PdfArray([profileIntent])))));
        PdfDocument invalidProfileDocument = PdfDocument.Open(profileUpdate.Build());
        InvalidOperationException profileError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(invalidProfileDocument).Build());

        Assert.Contains("/DestOutputProfile has no valid /N component count",
            profileError.Message, StringComparison.Ordinal);

        var referenceUpdate = new PdfIncrementalUpdateBuilder(original);
        var referencedIntent = new PdfDictionary([
            new(Name("Type"), Name("OutputIntent")),
            new(Name("S"), Name("GTS_PDFX")),
            new(Name("DestOutputProfileRef"), new PdfDictionary([
                new(Name("CheckSum"), new PdfString([1, 2, 3], PdfStringForm.Hexadecimal)),
                new(Name("ICCVersion"), new PdfString("4.3"u8, PdfStringForm.Literal)),
                new(Name("ProfileCS"), new PdfString("RGB"u8, PdfStringForm.Literal)),
                new(Name("ProfileName"), new PdfString("Test"u8, PdfStringForm.Literal)),
                new(Name("URLs"), new PdfArray([
                    new PdfDictionary([
                        new(Name("FS"), Name("URL")),
                        new(Name("F"), new PdfString(
                            "https://example.test/profile.icc"u8, PdfStringForm.Literal))
                    ])
                ]))
            ]))
        ]);
        referenceUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Append(new KeyValuePair<PdfName, PdfObject>(
                Name("OutputIntents"), new PdfArray([referencedIntent])))));
        PdfDocument invalidReferenceDocument = PdfDocument.Open(referenceUpdate.Build());
        InvalidOperationException referenceError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(invalidReferenceDocument).Build());

        Assert.Contains("/DestOutputProfileRef has no 16-byte /CheckSum string",
            referenceError.Message, StringComparison.Ordinal);

        var mixingUpdate = new PdfIncrementalUpdateBuilder(original);
        var invalidMixingIntent = new PdfDictionary([
            new(Name("Type"), Name("OutputIntent")),
            new(Name("S"), Name("GTS_PDFX")),
            new(Name("MixingHints"), new PdfDictionary([
                new(Name("DotGain"), new PdfDictionary([]))
            ]))
        ]);
        mixingUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Append(new KeyValuePair<PdfName, PdfObject>(
                Name("OutputIntents"), new PdfArray([invalidMixingIntent])))));
        PdfDocument invalidMixingDocument = PdfDocument.Open(mixingUpdate.Build());
        InvalidOperationException mixingError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(invalidMixingDocument).Build());

        Assert.Contains("/MixingHints contains the prohibited /DotGain entry",
            mixingError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectInvalidViewerPreferences()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfDocument source = original;
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary invalidPreferences = new([
            new(Name("HideToolbar"), new PdfInteger(1))
        ]);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("ViewerPreferences"),
                    invalidPreferences))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("/HideToolbar value is not a boolean",
            error.Message, StringComparison.Ordinal);

        var invalidDirectionUpdate = new PdfIncrementalUpdateBuilder(original);
        invalidDirectionUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Append(new KeyValuePair<PdfName, PdfObject>(
                Name("ViewerPreferences"), new PdfDictionary([
                    new(Name("Direction"), Name("TopToBottom"))
                ])))));
        PdfDocument invalidDirection = PdfDocument.Open(invalidDirectionUpdate.Build());
        InvalidOperationException directionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(invalidDirection).Build());

        Assert.Contains("/Direction value /TopToBottom is not defined",
            directionError.Message, StringComparison.Ordinal);

        var reversedRangeUpdate = new PdfIncrementalUpdateBuilder(original);
        reversedRangeUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Append(new KeyValuePair<PdfName, PdfObject>(
                Name("ViewerPreferences"), new PdfDictionary([
                    new(Name("PrintPageRange"), new PdfArray([
                        new PdfInteger(4), new PdfInteger(2)
                    ]))
                ])))));
        PdfDocument reversedRange = PdfDocument.Open(reversedRangeUpdate.Build());
        InvalidOperationException rangeError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(reversedRange).Build());

        Assert.Contains("/PrintPageRange contains a reversed page range",
            rangeError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectMalformedFormFieldTypes()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfDocument source = original;
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        var field = new PdfDictionary([
            new(Name("T"), new PdfString("Broken"u8, PdfStringForm.Literal)),
            new(Name("FT"), Name("Unknown"))
        ]);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("AcroForm"),
                        new PdfDictionary([
                            new(Name("Fields"), new PdfArray([field]))
                        ])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("An AcroForm field /FT value /Unknown is not defined",
            error.Message, StringComparison.Ordinal);

        PdfDocument resourceSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().Build());
        PdfIndirectReference resourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            resourceSource.Trailer[Name("Root")]);
        PdfDictionary resourceCatalog = ResolveDictionary(
            resourceSource, resourceCatalogReference);
        resourceSource = PdfDocument.Open(new PdfIncrementalUpdateBuilder(resourceSource)
            .ReplaceObject(resourceCatalogReference.ObjectNumber,
                new PdfDictionary(resourceCatalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("AcroForm"),
                        new PdfDictionary([
                            new(Name("Fields"), new PdfArray([])),
                            new(Name("DR"), new PdfDictionary([
                                new(Name("ExtGState"), new PdfDictionary([
                                    new(Name("Broken"), new PdfInteger(1))
                                ]))
                            ]))
                        ])))))
            .Build());
        InvalidOperationException resourceError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(resourceSource).Build());
        Assert.Contains("/AcroForm /DR /ExtGState /Broken entry has an invalid object type",
            resourceError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectMalformedCatalogRequirements()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Requirements"),
                        new PdfArray([new PdfInteger(1)])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("catalog /Requirements value entry is not a dictionary",
            error.Message, StringComparison.OrdinalIgnoreCase);

        PdfDocument handlerSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference handlerCatalogReference = Assert.IsType<PdfIndirectReference>(
            handlerSource.Trailer[Name("Root")]);
        PdfDictionary handlerCatalog = ResolveDictionary(
            handlerSource, handlerCatalogReference);
        var invalidRequirement = new PdfDictionary([
            new(Name("Type"), Name("Requirement")),
            new(Name("S"), Name("CustomRequirement")),
            new(Name("RH"), new PdfArray([
                new PdfDictionary([
                    new(Name("Type"), Name("ReqHandler")),
                    new(Name("S"), new PdfInteger(1))
                ])
            ]))
        ]);
        handlerSource = PdfDocument.Open(new PdfIncrementalUpdateBuilder(handlerSource)
            .ReplaceObject(handlerCatalogReference.ObjectNumber,
                new PdfDictionary(handlerCatalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Requirements"),
                        new PdfArray([invalidRequirement])))))
            .Build());

        InvalidOperationException handlerError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(handlerSource).Build());
        Assert.Contains("/RH handler has no /S name",
            handlerError.Message, StringComparison.Ordinal);

        PdfDocument penaltySource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference penaltyCatalogReference = Assert.IsType<PdfIndirectReference>(
            penaltySource.Trailer[Name("Root")]);
        PdfDictionary penaltyCatalog = ResolveDictionary(
            penaltySource, penaltyCatalogReference);
        penaltySource = PdfDocument.Open(new PdfIncrementalUpdateBuilder(penaltySource)
            .ReplaceObject(penaltyCatalogReference.ObjectNumber,
                new PdfDictionary(penaltyCatalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Requirements"),
                        new PdfArray([new PdfDictionary([
                            new(Name("S"), Name("CustomRequirement")),
                            new(Name("Penalty"), new PdfInteger(101))
                        ])])))))
            .Build());

        InvalidOperationException penaltyError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(penaltySource).Build());
        Assert.Contains("/Penalty value is not an integer from 0 through 100",
            penaltyError.Message, StringComparison.Ordinal);

        PdfDocument versionSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference versionCatalogReference = Assert.IsType<PdfIndirectReference>(
            versionSource.Trailer[Name("Root")]);
        PdfDictionary versionCatalog = ResolveDictionary(versionSource, versionCatalogReference);
        versionSource = PdfDocument.Open(new PdfIncrementalUpdateBuilder(versionSource)
            .ReplaceObject(versionCatalogReference.ObjectNumber,
                new PdfDictionary(versionCatalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Requirements"),
                        new PdfArray([new PdfDictionary([
                            new(Name("S"), Name("U3D")),
                            new(Name("V"), new PdfString(
                                "1"u8, PdfStringForm.Literal))
                        ])])))))
            .Build());

        InvalidOperationException versionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(versionSource).Build());
        Assert.Contains("/V value is not a name or developer-extension dictionary",
            versionError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectMalformedDocumentSecurityStores()
    {
        PdfDocument directStreamSource = WithDss((_, _) => new PdfDictionary([
            new(Name("Type"), Name("DSS")),
            new(Name("Certs"), new PdfArray([new PdfInteger(1)]))
        ]));
        InvalidOperationException directStreamError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedDocument(directStreamSource).Build());
        Assert.Contains("/DSS value /Certs entry is not an indirect stream reference",
            directStreamError.Message, StringComparison.Ordinal);

        PdfDocument unregisteredVriSource = WithDss((update, _) =>
        {
            PdfIndirectReference certificate = update.AddObject(
                new PdfStream(new PdfDictionary([]), [1]));
            PdfIndirectReference unregisteredCertificate = update.AddObject(
                new PdfStream(new PdfDictionary([]), [2]));
            return new PdfDictionary([
                new(Name("Certs"), new PdfArray([certificate])),
                new(Name("VRI"), new PdfDictionary([
                    new(Name("0123456789ABCDEF0123456789ABCDEF01234567"),
                        new PdfDictionary([
                            new(Name("Type"), Name("VRI")),
                            new(Name("Cert"), new PdfArray([unregisteredCertificate]))
                        ]))
                ]))
            ]);
        });
        InvalidOperationException registrationError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(PdfDocument.Open(
                        new PdfDocumentBuilder().Build()))
                    .AddImportedDocument(unregisteredVriSource).Build());
        Assert.Contains("/Cert entry is absent from its DSS validation-data array",
            registrationError.Message, StringComparison.Ordinal);

        PdfDocument validSource = WithDss((update, _) =>
        {
            PdfIndirectReference certificate = update.AddObject(
                new PdfStream(new PdfDictionary([]), [1, 2, 3]));
            PdfIndirectReference certificateAlias = update.AddObject(certificate);
            return new PdfDictionary([
                new(Name("Type"), Name("DSS")),
                new(Name("Certs"), new PdfArray([certificateAlias])),
                new(Name("VRI"), new PdfDictionary([
                    new(Name("0123456789ABCDEF0123456789ABCDEF01234567"),
                        new PdfDictionary([
                            new(Name("Type"), Name("VRI")),
                            new(Name("Cert"), new PdfArray([certificate])),
                            new(Name("TU"), new PdfString(
                                "D:20260824120000-07'00'"u8,
                                PdfStringForm.Literal))
                        ]))
                ]))
            ]);
        });
        PdfDocument imported = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().Build()))
                .AddImportedDocument(validSource).Build());
        Assert.True(ResolveDictionary(imported,
            imported.Trailer[Name("Root")]).ContainsKey(Name("DSS")));

        static PdfDocument WithDss(
            Func<PdfIncrementalUpdateBuilder, PdfDocument, PdfDictionary> createDss)
        {
            PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
                .AddBlankPage().Build());
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
                document.Trailer[Name("Root")]);
            PdfDictionary catalog = ResolveDictionary(document, catalogReference);
            update.ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("DSS"),
                        createDss(update, document)))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void CompleteDocumentImports_RejectMalformedLegalAttestations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Legal"),
                        new PdfDictionary([
                            new(Name("Type"), Name("Legal")),
                            new(Name("JavaScriptActions"), Name("Maybe"))
                        ])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("/Legal value /JavaScriptActions value /Maybe is not defined",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectMalformedCollectionSchemas()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfDocument source = original;
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Collection"),
                        new PdfDictionary([
                            new(Name("Type"), Name("Collection")),
                            new(Name("Schema"), new PdfDictionary([
                                new(Name("Column"), new PdfDictionary([
                                    new(Name("Subtype"), Name("S"))
                                ]))
                            ]))
                        ])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("/Collection value /Schema /Column has no /N string",
            error.Message, StringComparison.Ordinal);

        PdfDocument invalidColors = PdfDocument.Open(new PdfIncrementalUpdateBuilder(original)
            .ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Collection"),
                        new PdfDictionary([
                            new(Name("Type"), Name("Collection")),
                            new(Name("Colors"), new PdfDictionary([
                                new(Name("Type"), Name("CollectionColors")),
                                new(Name("PrimaryText"), new PdfArray([
                                    new PdfInteger(0), new PdfInteger(0), new PdfInteger(2)
                                ]))
                            ]))
                        ])))))
            .Build());
        InvalidOperationException colorError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(invalidColors).Build());
        Assert.Contains("/Collection value /Colors /PrimaryText value is not a valid RGB array",
            colorError.Message, StringComparison.Ordinal);

        PdfDocument invalidFolders = PdfDocument.Open(new PdfIncrementalUpdateBuilder(original)
            .ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Collection"),
                        new PdfDictionary([
                            new(Name("Type"), Name("Collection")),
                            new(Name("Folders"), new PdfDictionary([
                                new(Name("Type"), Name("Folder")),
                                new(Name("ID"), new PdfInteger(0)),
                                new(Name("Name"), new PdfString("Root"u8, PdfStringForm.Literal))
                            ]))
                        ])))))
            .Build());
        InvalidOperationException folderError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(invalidFolders).Build());
        Assert.Contains("/Collection value /Folders value is not an indirect folder reference",
            folderError.Message, StringComparison.Ordinal);

        var folderUpdate = new PdfIncrementalUpdateBuilder(original);
        PdfIndirectReference rootFolder = folderUpdate.ReserveObject();
        PdfIndirectReference childFolder = folderUpdate.ReserveObject();
        PdfIndirectReference rootFolderAlias = folderUpdate.AddObject(rootFolder);
        PdfIndirectReference rootFolderOuterAlias = folderUpdate.AddObject(rootFolderAlias);
        PdfIndirectReference childFolderAlias = folderUpdate.AddObject(childFolder);
        PdfIndirectReference childFolderOuterAlias = folderUpdate.AddObject(childFolderAlias);
        folderUpdate.SetObject(rootFolder, new PdfDictionary([
            new(Name("Type"), Name("Folder")),
            new(Name("ID"), new PdfInteger(0)),
            new(Name("Name"), new PdfString("Root"u8, PdfStringForm.Literal)),
            new(Name("Child"), childFolderOuterAlias)
        ]));
        folderUpdate.SetObject(childFolder, new PdfDictionary([
            new(Name("Type"), Name("Folder")),
            new(Name("ID"), new PdfInteger(1)),
            new(Name("Name"), new PdfString("Child"u8, PdfStringForm.Literal)),
            new(Name("Parent"), rootFolder)
        ]));
        folderUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Collection"),
                    new PdfDictionary([
                        new(Name("Type"), Name("Collection")),
                        new(Name("Folders"), rootFolderOuterAlias)
                    ])))));
        PdfDocument aliasedFolders = PdfDocument.Open(folderUpdate.Build());

        PdfDocument importedFolders = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedDocument(aliasedFolders)
            .Build());
        Assert.True(ResolveDictionary(importedFolders,
            importedFolders.Trailer[Name("Root")]).ContainsKey(Name("Collection")));
    }

    [Fact]
    public void CompleteDocumentImports_RejectInvalidMetadataStreams()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference invalidMetadata = update.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("Wrong")),
                new(Name("Subtype"), Name("XML"))
            ]), "<x:xmpmeta/>"u8));
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Metadata"), invalidMetadata))));
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("catalog /Metadata value has an invalid /Type value",
            error.Message, StringComparison.Ordinal);

    }

    [Fact]
    public void CompleteDocumentImports_RejectInvalidLanguageTags()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfDocument original = source;
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Lang"),
                    new PdfString("not_valid"u8, PdfStringForm.Literal)))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("not a valid BCP 47 language tag",
            error.Message, StringComparison.Ordinal);

        PdfDictionary originalCatalog = ResolveDictionary(original,
            Assert.IsType<PdfIndirectReference>(original.Trailer[Name("Root")]));
        byte[] utf8Language = [0xEF, 0xBB, 0xBF, .. "en-US"u8.ToArray()];
        PdfDocument validUtf8 = PdfDocument.Open(new PdfIncrementalUpdateBuilder(original)
            .ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(originalCatalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Lang"),
                        new PdfString(utf8Language, PdfStringForm.Hexadecimal)))))
            .Build());

        _ = new PdfIncrementalPageEditor(PdfDocument.Open(
                new PdfDocumentBuilder().Build()))
            .AddImportedDocument(validUtf8)
            .Build();

        PdfDocument malformedUtf8 = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(original)
                .ReplaceObject(catalogReference.ObjectNumber,
                    new PdfDictionary(originalCatalog.Append(
                        new KeyValuePair<PdfName, PdfObject>(Name("Lang"),
                            new PdfString([0xEF, 0xBB, 0xBF, 0xC3, 0x28],
                                PdfStringForm.Hexadecimal)))))
                .Build());
        InvalidOperationException malformedError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().Build()))
                .AddImportedDocument(malformedUtf8)
                .Build());
        Assert.Contains("contains malformed UTF-8 text",
            malformedError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RejectUndefinedCatalogVersions()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        byte[] validVersion = new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Version"), Name("2.0")))))
            .Build();
        string invalidVersion = Encoding.Latin1.GetString(validVersion)
            .Replace("/Version /2.0", "/Version /3.0", StringComparison.Ordinal);
        source = PdfDocument.Open(Encoding.Latin1.GetBytes(invalidVersion));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("declares undefined PDF 3.0",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedCompleteImport_RemapCatalogExtensionStructureRootBackReference()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary sourceCatalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference sourceRootReference = Assert.IsType<PdfIndirectReference>(
            sourceCatalog[Name("StructTreeRoot")]);
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference extension = sourceUpdate.AddObject(new PdfDictionary([
            new(Name("BaseVersion"), Name("2.0")),
            new(Name("ExtensionLevel"), new PdfInteger(1)),
            new(Name("StructureRoot"), sourceRootReference)
        ]));
        PdfIndirectReference sourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        sourceUpdate.ReplaceObject(sourceCatalogReference.ObjectNumber,
            new PdfDictionary(sourceCatalog.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Extensions"),
                    new PdfDictionary([new(Name("Vendor"), extension)])))));
        source = PdfDocument.Open(sourceUpdate.Build());

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfIndirectReference mergedRootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary extensions = DictionaryValue(merged, catalog[Name("Extensions")]);
        PdfDictionary vendor = ResolveDictionary(merged, extensions[Name("Vendor")]);
        PdfIndirectReference backReference = Assert.IsType<PdfIndirectReference>(
            vendor[Name("StructureRoot")]);

        Assert.Equal(mergedRootReference.ObjectNumber, backReference.ObjectNumber);
        Assert.Equal(mergedRootReference.Generation, backReference.Generation);
    }

    [Fact]
    public void CompleteDocumentImports_RemoveStaleCatalogExtensionNamespaces()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfDictionary extensions = new([
            new(Name("Vendor"), new PdfIndirectReference(
                pages[0].ObjectNumber, pages[0].Generation + 1))
        ]);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Extensions"), extensions))));
        source = PdfDocument.Open(update.Build());
        PdfDocument target = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(source)
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);

        Assert.False(mergedCatalog.ContainsKey(Name("Extensions")));
    }

    [Fact]
    public void TaggedImport_RejectsExhaustedStructureParentKeySpace()
    {
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(target, target.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(target, rootReference);
        var setup = new PdfIncrementalUpdateBuilder(target);
        var rootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        rootEntries[Name("ParentTreeNextKey")] = new PdfInteger(long.MaxValue);
        setup.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(rootEntries));
        target = PdfDocument.Open(setup.Build());

        Assert.Throws<OverflowException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(PdfDocument.Open(BuildTaggedDocument()))
                .Build());
    }

    [Fact]
    public void TaggedImport_RejectsNegativeDestinationStructureParentTreeKey()
    {
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(target, target.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(target, rootReference);
        var setup = new PdfIncrementalUpdateBuilder(target);
        var rootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        rootEntries[Name("ParentTree")] = new PdfDictionary([
            new(Name("Nums"), new PdfArray([new PdfInteger(-1), PdfNull.Instance]))
        ]);
        setup.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(rootEntries));
        target = PdfDocument.Open(setup.Build());

        Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(PdfDocument.Open(BuildTaggedDocument()))
                .Build());
    }

    [Fact]
    public void TaggedCompleteImport_FlattensIndirectStructureKidsArrays()
    {
        PdfDocument target = WithIndirectKids(PdfDocument.Open(BuildTaggedDocument()));
        PdfDocument source = WithIndirectKids(PdfDocument.Open(BuildTaggedDocument()));

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(merged, catalog[Name("StructTreeRoot")]);
        PdfArray rootKids = Assert.IsType<PdfArray>(root[Name("K")]);
        PdfDictionary documentElement = ResolveDictionary(merged, rootKids[0]);
        PdfArray documentKids = Assert.IsType<PdfArray>(documentElement[Name("K")]);

        Assert.Single(rootKids);
        Assert.Equal(4, documentKids.Count);
        Assert.DoesNotContain(documentKids, value => value is PdfIndirectReference reference
            && merged.Resolve(reference) is PdfArray);

        PdfDocument selected = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0).Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selected, selected.Trailer[Name("Root")]);
        PdfDictionary selectedRoot = ResolveDictionary(
            selected, selectedCatalog[Name("StructTreeRoot")]);
        PdfDictionary selectedDocument = ResolveDictionary(
            selected, Assert.IsType<PdfArray>(selectedRoot[Name("K")])[0]);
        PdfObject selectedKids = selectedDocument[Name("K")];
        Assert.False(selectedKids is PdfIndirectReference reference
            && selected.Resolve(reference) is PdfArray);

        static PdfDocument WithIndirectKids(PdfDocument document)
        {
            PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
            PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
                catalog[Name("StructTreeRoot")]);
            PdfDictionary root = ResolveDictionary(document, rootReference);
            PdfArray rootKids = Assert.IsType<PdfArray>(root[Name("K")]);
            PdfIndirectReference documentReference = Assert.IsType<PdfIndirectReference>(rootKids[0]);
            PdfDictionary documentElement = ResolveDictionary(document, documentReference);
            PdfObject documentKidsValue = documentElement[Name("K")];
            PdfArray documentKids = documentKidsValue is PdfArray array
                ? array : new PdfArray([documentKidsValue]);
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfIndirectReference indirectRootKids = update.AddObject(rootKids);
            PdfIndirectReference indirectDocumentKids = update.AddObject(documentKids);
            update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root
                .Where(entry => !entry.Key.Equals(Name("K")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("K"), indirectRootKids))));
            update.ReplaceObject(documentReference.ObjectNumber, new PdfDictionary(documentElement
                .Where(entry => !entry.Key.Equals(Name("K")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("K"), indirectDocumentKids))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void TaggedCompleteImport_PromotesDirectDestinationDocumentElement()
    {
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary targetCatalog = ResolveDictionary(
            target, target.Trailer[Name("Root")]);
        PdfIndirectReference targetRootReference = Assert.IsType<PdfIndirectReference>(
            targetCatalog[Name("StructTreeRoot")]);
        PdfDictionary targetRoot = ResolveDictionary(target, targetRootReference);
        PdfArray targetRootKids = Assert.IsType<PdfArray>(targetRoot[Name("K")]);
        PdfDictionary directDocument = ResolveDictionary(target, targetRootKids[0]);
        var setup = new PdfIncrementalUpdateBuilder(target);
        setup.ReplaceObject(targetRootReference.ObjectNumber, new PdfDictionary(targetRoot
            .Where(entry => !entry.Key.Equals(Name("K")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("K"),
                new PdfArray([directDocument])))));
        target = PdfDocument.Open(setup.Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(PdfDocument.Open(BuildTaggedDocument())).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(merged, rootReference);
        PdfArray rootKids = Assert.IsType<PdfArray>(root[Name("K")]);
        PdfDictionary documentElement = ResolveDictionary(merged, rootKids[0]);

        Assert.Single(rootKids);
        PdfArray documentKids = Assert.IsType<PdfArray>(documentElement[Name("K")]);
        Assert.Equal(4, documentKids.Count);
        PdfIndirectReference documentReference = Assert.IsType<PdfIndirectReference>(rootKids[0]);
        Assert.Equal(rootReference.ObjectNumber, Assert.IsType<PdfIndirectReference>(
            documentElement[Name("P")]).ObjectNumber);
        Assert.All(documentKids, child => Assert.Equal(documentReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(
                ResolveDictionary(merged, child)[Name("P")]).ObjectNumber));
    }

    [Fact]
    public void TaggedCompleteImport_NormalizesDirectSourceDocumentElement()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary sourceCatalog = ResolveDictionary(
            source, source.Trailer[Name("Root")]);
        PdfIndirectReference sourceRootReference = Assert.IsType<PdfIndirectReference>(
            sourceCatalog[Name("StructTreeRoot")]);
        PdfDictionary sourceRoot = ResolveDictionary(source, sourceRootReference);
        PdfArray sourceRootKids = Assert.IsType<PdfArray>(sourceRoot[Name("K")]);
        PdfDictionary directDocument = ResolveDictionary(source, sourceRootKids[0]);
        var setup = new PdfIncrementalUpdateBuilder(source);
        setup.ReplaceObject(sourceRootReference.ObjectNumber, new PdfDictionary(sourceRoot
            .Where(entry => !entry.Key.Equals(Name("K")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("K"), directDocument))));
        source = PdfDocument.Open(setup.Build());

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(merged, catalog[Name("StructTreeRoot")]);
        PdfArray rootKids = Assert.IsType<PdfArray>(root[Name("K")]);
        PdfDictionary documentElement = ResolveDictionary(merged, rootKids[0]);

        Assert.Single(rootKids);
        Assert.Equal(4, Assert.IsType<PdfArray>(documentElement[Name("K")]).Count);
    }

    [Fact]
    public void TaggedImports_NormalizeDirectSourceAndDestinationStructureRoots()
    {
        PdfDocument target = DirectRoot(PdfDocument.Open(BuildTaggedDocument()));
        PdfDocument source = DirectRoot(PdfDocument.Open(BuildTaggedDocument()));

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(merged, rootReference);
        PdfArray rootKids = Assert.IsType<PdfArray>(root[Name("K")]);
        PdfDictionary document = ResolveDictionary(merged, rootKids[0]);

        Assert.Single(rootKids);
        Assert.Equal(4, Assert.IsType<PdfArray>(document[Name("K")]).Count);

        PdfDocument selected = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0).Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selected, selected.Trailer[Name("Root")]);
        Assert.IsType<PdfIndirectReference>(selectedCatalog[Name("StructTreeRoot")]);

        static PdfDocument DirectRoot(PdfDocument document)
        {
            PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
                document.Trailer[Name("Root")]);
            PdfDictionary catalog = ResolveDictionary(document, catalogReference);
            PdfDictionary root = ResolveDictionary(document, catalog[Name("StructTreeRoot")]);
            var update = new PdfIncrementalUpdateBuilder(document);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("StructTreeRoot")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("StructTreeRoot"), root))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void TaggedImports_RenameCollidingStructureIdsAndRebuildIdTree()
    {
        PdfDocument target = AddId(PdfDocument.Open(BuildTaggedDocument()), indirect: false);
        PdfDocument source = AddId(PdfDocument.Open(BuildTaggedDocument()), indirect: true);

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(merged, catalog[Name("StructTreeRoot")]);
        PdfDictionary idTree = Assert.IsType<PdfDictionary>(root[Name("IDTree")]);
        PdfArray names = Assert.IsType<PdfArray>(idTree[Name("Names")]);
        PdfDictionary document = ResolveDictionary(merged,
            Assert.IsType<PdfArray>(root[Name("K")])[0]);
        PdfDictionary importedFigure = ResolveDictionary(merged,
            Assert.IsType<PdfArray>(document[Name("K")])[2]);

        Assert.Equal(4, names.Count);
        Assert.Equal("shared", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(names[0]).Bytes.Span));
        Assert.Equal("shared-KP1", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(names[2]).Bytes.Span));
        Assert.Equal("shared-KP1", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(importedFigure[Name("ID")]).Bytes.Span));

        PdfDocument selectedWithId = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0).Build());
        PdfDictionary selectedRoot = ResolveDictionary(selectedWithId,
            ResolveDictionary(selectedWithId,
                selectedWithId.Trailer[Name("Root")])[Name("StructTreeRoot")]);
        Assert.Equal(2, Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(
            selectedRoot[Name("IDTree")])[Name("Names")]).Count);

        PdfDocument selectedWithoutId = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 1).Build());
        PdfDictionary selectedWithoutIdRoot = ResolveDictionary(selectedWithoutId,
            ResolveDictionary(selectedWithoutId,
                selectedWithoutId.Trailer[Name("Root")])[Name("StructTreeRoot")]);
        Assert.False(selectedWithoutIdRoot.ContainsKey(Name("IDTree")));

        static PdfDocument AddId(PdfDocument document, bool indirect)
        {
            PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
            PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
                catalog[Name("StructTreeRoot")]);
            PdfDictionary root = ResolveDictionary(document, rootReference);
            PdfDictionary top = ResolveDictionary(document,
                Assert.IsType<PdfArray>(root[Name("K")])[0]);
            PdfIndirectReference figureReference = Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfArray>(top[Name("K")])[0]);
            PdfDictionary figure = ResolveDictionary(document, figureReference);
            PdfString id = new("shared"u8, PdfStringForm.Literal);
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfObject structureId = indirect
                ? update.AddObject(update.AddObject(id)) : id;
            update.ReplaceObject(figureReference.ObjectNumber, new PdfDictionary(figure.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("ID"), structureId))));
            update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("IDTree"),
                    new PdfDictionary([new(Name("Names"),
                        new PdfArray([id, figureReference]))])))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void SelectedTaggedImports_RemoveStaleIdTreeGenerations()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        PdfDictionary top = ResolveDictionary(source,
            Assert.IsType<PdfArray>(root[Name("K")])[0]);
        PdfIndirectReference figureReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(top[Name("K")])[0]);
        PdfString retainedId = new("retained"u8, PdfStringForm.Literal);
        PdfString staleId = new("stale"u8, PdfStringForm.Literal);
        PdfDictionary figure = ResolveDictionary(source, figureReference);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(figureReference.ObjectNumber, new PdfDictionary(figure
            .Where(entry => !entry.Key.Equals(Name("ID")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("ID"), retainedId))));
        update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("IDTree"),
                new PdfDictionary([new(Name("Names"), new PdfArray([
                    retainedId, figureReference,
                    staleId, new PdfIndirectReference(
                        figureReference.ObjectNumber, figureReference.Generation + 1)
                ]))])))));
        source = PdfDocument.Open(update.Build());

        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selected, selected.Trailer[Name("Root")]);
        PdfDictionary selectedRoot = ResolveDictionary(
            selected, selectedCatalog[Name("StructTreeRoot")]);
        PdfArray names = Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(selectedRoot[Name("IDTree")])[Name("Names")]);

        Assert.Equal(2, names.Count);
        Assert.Equal("retained", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(names[0]).Bytes.Span));
        Assert.IsType<PdfIndirectReference>(names[1]);
    }

    [Fact]
    public void CompleteTaggedMerges_RemoveStaleIdTreeGenerations()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        PdfDictionary top = ResolveDictionary(source,
            Assert.IsType<PdfArray>(root[Name("K")])[0]);
        PdfIndirectReference figureReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(top[Name("K")])[0]);
        PdfString staleId = new("stale"u8, PdfStringForm.Literal);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("IDTree"),
                new PdfDictionary([new(Name("Names"), new PdfArray([
                    staleId, new PdfIndirectReference(
                        figureReference.ObjectNumber, figureReference.Generation + 1)
                ]))])))));
        source = PdfDocument.Open(update.Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(BuildTaggedDocument()))
            .AddImportedDocument(source)
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);
        PdfDictionary mergedRoot = ResolveDictionary(
            merged, mergedCatalog[Name("StructTreeRoot")]);

        Assert.False(mergedRoot.ContainsKey(Name("IDTree")));
    }

    [Fact]
    public void TaggedDocumentMerges_RejectMismatchedIdTreeValues()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference invalidElement = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("StructElem")),
            new(Name("S"), Name("P")),
            new(Name("ID"), new PdfString("actual"u8, PdfStringForm.Literal))
        ]));
        PdfDictionary idTree = new([
            new(Name("Names"), new PdfArray([
                new PdfString("registered"u8, PdfStringForm.Literal),
                invalidElement
            ]))
        ]);
        update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root
            .Where(entry => !entry.Key.Equals(Name("IDTree")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("IDTree"), idTree))));
        source = PdfDocument.Open(update.Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source).Build());

        Assert.Contains("role-bearing structure element with a matching /ID",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedImports_MergeStructureRootAssociatedFilesAndLexicons()
    {
        PdfDocument target = AddRootArrays(PdfDocument.Open(BuildTaggedDocument()), "target");
        PdfDocument source = AddRootArrays(PdfDocument.Open(BuildTaggedDocument()), "source");

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(merged, catalog[Name("StructTreeRoot")]);

        Assert.Equal(2, Assert.IsType<PdfArray>(root[Name("AF")]).Count);
        Assert.Equal(2, Assert.IsType<PdfArray>(
            root[Name("PronunciationLexicon")]).Count);

        PdfDocument invalid = AddRootArrays(
            PdfDocument.Open(BuildTaggedDocument()), "invalid", "Wrong");
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalid).Build());
        Assert.Contains("has an invalid /Type value", error.Message,
            StringComparison.Ordinal);

        static PdfDocument AddRootArrays(
            PdfDocument document, string label, string type = "Filespec")
        {
            PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
            PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
                catalog[Name("StructTreeRoot")]);
            PdfDictionary root = ResolveDictionary(document, rootReference);
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfIndirectReference value = update.AddObject(new PdfDictionary([
                new(Name("Type"), Name(type)),
                new(Name("F"), new PdfString(Encoding.ASCII.GetBytes(label), PdfStringForm.Literal))
            ]));
            update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("AF"), new PdfArray([value])))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("PronunciationLexicon"), new PdfArray([value])))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void ExistingTaggedDocument_RewritesStructureForRemovedPages()
    {
        PdfDocument authored = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary authoredCatalog = ResolveDictionary(
            authored, authored.Trailer[Name("Root")]);
        PdfIndirectReference authoredRootReference = Assert.IsType<PdfIndirectReference>(
            authoredCatalog[Name("StructTreeRoot")]);
        PdfDictionary authoredRoot = ResolveDictionary(authored, authoredRootReference);
        PdfIndirectReference authoredParentTreeReference = Assert.IsType<PdfIndirectReference>(
            authoredRoot[Name("ParentTree")]);
        var aliasUpdate = new PdfIncrementalUpdateBuilder(authored);
        PdfIndirectReference documentReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(authoredRoot[Name("K")])[0]);
        PdfDictionary structureDocument = ResolveDictionary(authored, documentReference);
        PdfIndirectReference firstFigureReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(structureDocument[Name("K")])[0]);
        PdfDictionary firstFigure = ResolveDictionary(authored, firstFigureReference);
        PdfInteger markedContentId = Assert.IsType<PdfInteger>(firstFigure[Name("K")]);
        PdfIndirectReference firstPageReference = FlatPages(authored).References[0];
        PdfIndirectReference markedContentTypeAlias = aliasUpdate.AddObject(Name("MCR"));
        PdfIndirectReference markedContentTypeOuterAlias =
            aliasUpdate.AddObject(markedContentTypeAlias);
        var markedContent = new PdfDictionary(new[]
        {
            new KeyValuePair<PdfName, PdfObject>(Name("Type"), markedContentTypeOuterAlias),
            new KeyValuePair<PdfName, PdfObject>(Name("Pg"), firstPageReference),
            new KeyValuePair<PdfName, PdfObject>(Name("MCID"), markedContentId)
        });
        aliasUpdate.ReplaceObject(firstFigureReference.ObjectNumber,
            new PdfDictionary(firstFigure.Select(entry =>
                entry.Key.Equals(Name("K"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, markedContent)
                    : entry)));
        PdfIndirectReference parentTreeAlias =
            aliasUpdate.AddObject(authoredParentTreeReference);
        PdfIndirectReference parentTreeOuterAlias = aliasUpdate.AddObject(parentTreeAlias);
        aliasUpdate.ReplaceObject(authoredRootReference.ObjectNumber,
            new PdfDictionary(authoredRoot.Select(entry =>
                entry.Key.Equals(Name("ParentTree"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, parentTreeOuterAlias)
                    : entry)));
        PdfIndirectReference rootAlias = aliasUpdate.AddObject(authoredRootReference);
        PdfIndirectReference rootOuterAlias = aliasUpdate.AddObject(rootAlias);
        PdfIndirectReference authoredCatalogReference = Assert.IsType<PdfIndirectReference>(
            authored.Trailer[Name("Root")]);
        aliasUpdate.ReplaceObject(authoredCatalogReference.ObjectNumber,
            new PdfDictionary(authoredCatalog.Select(entry =>
                entry.Key.Equals(Name("StructTreeRoot"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, rootOuterAlias)
                    : entry)));
        byte[] source = aliasUpdate.Build();
        PdfDocument original = PdfDocument.Open(source);
        int removedPageNumber = FlatPages(original).References[0].ObjectNumber;

        PdfDocument reordered = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .MovePage(0, 1)
                .Build());
        Assert.True(ResolveDictionary(reordered, reordered.Trailer[Name("Root")])
            .ContainsKey(Name("StructTreeRoot")));
        PdfDocument reduced = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .RemovePage(0).Build());
        PdfDictionary reducedCatalog = ResolveDictionary(
            reduced, reduced.Trailer[Name("Root")]);
        PdfDictionary reducedRoot = ResolveDictionary(
            reduced, reducedCatalog[Name("StructTreeRoot")]);
        PdfDictionary reducedParentTree = ResolveDictionary(
            reduced, reducedRoot[Name("ParentTree")]);
        PdfArray reducedNumbers = Assert.IsType<PdfArray>(
            reducedParentTree[Name("Nums")]);
        Assert.Equal(2, reducedNumbers.Count);
        Assert.Equal(1, Assert.IsType<PdfInteger>(reducedNumbers[0]).Value);
        Assert.Equal(rootOuterAlias.ObjectNumber, Assert.IsType<PdfIndirectReference>(
            reducedCatalog[Name("StructTreeRoot")]).ObjectNumber);
        Assert.Equal(parentTreeOuterAlias.ObjectNumber, Assert.IsType<PdfIndirectReference>(
            reducedRoot[Name("ParentTree")]).ObjectNumber);
        Assert.Single(FlatPages(reduced).Pages);
        AssertStructureTreeDoesNotReferencePage(reduced, reducedRoot, removedPageNumber);
        PdfDocument extended = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .AddBlankPage().Build());
        Assert.Equal(3, FlatPages(extended).Pages.Length);
        Assert.True(ResolveDictionary(extended, extended.Trailer[Name("Root")])
            .ContainsKey(Name("StructTreeRoot")));
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .AddImportedDocument(PdfDocument.Open(
                    new PdfDocumentBuilder().AddBlankPage().Build()))
                .Build());
    }

    [Fact]
    public void ExistingTaggedDocument_ComposesPageRemovalWithTaggedMerge()
    {
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        int removedPageNumber = FlatPages(target).References[0].ObjectNumber;

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .RemovePage(0).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(merged, catalog[Name("StructTreeRoot")]);
        PdfDictionary document = ResolveDictionary(merged,
            Assert.IsType<PdfArray>(root[Name("K")])[0]);
        PdfArray documentKids = Assert.IsType<PdfArray>(document[Name("K")]);
        PdfDictionary parentTree = ResolveDictionary(merged, root[Name("ParentTree")]);
        PdfArray numbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);
        (_, _, PdfDictionary[] pages) = FlatPages(merged);

        Assert.Equal(3, pages.Length);
        Assert.Equal(3, documentKids.Count);
        Assert.Equal([1L, 2L, 3L], Enumerable.Range(0, numbers.Count / 2)
            .Select(index => Assert.IsType<PdfInteger>(numbers[index * 2]).Value));
        AssertStructureTreeDoesNotReferencePage(merged, root, removedPageNumber);
        Assert.Equal(4, Assert.IsType<PdfInteger>(
            root[Name("ParentTreeNextKey")]).Value);
    }

    [Fact]
    public void ExistingTaggedDocument_RebuildsDirectParentTreeWhenRemovingPage()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        PdfDictionary directParentTree = ResolveDictionary(
            source, root[Name("ParentTree")]);
        var update = new PdfIncrementalUpdateBuilder(source);
        var directRoot = new PdfDictionary(root
            .Where(entry => !entry.Key.Equals(Name("ParentTree")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("ParentTree"), directParentTree)));
        update.ReplaceObject(rootReference.ObjectNumber, directRoot);
        PdfDocument direct = PdfDocument.Open(update.Build());

        PdfDocument reduced = PdfDocument.Open(
            new PdfIncrementalPageEditor(direct).RemovePage(0).Build());
        PdfDictionary reducedCatalog = ResolveDictionary(
            reduced, reduced.Trailer[Name("Root")]);
        PdfDictionary reducedRoot = ResolveDictionary(
            reduced, reducedCatalog[Name("StructTreeRoot")]);
        PdfDictionary parentTree = Assert.IsType<PdfDictionary>(
            reducedRoot[Name("ParentTree")]);
        PdfArray numbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);

        Assert.Equal(2, numbers.Count);
        Assert.Equal(1, Assert.IsType<PdfInteger>(numbers[0]).Value);

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(direct)
            .RemovePage(0)
            .AddImportedDocument(PdfDocument.Open(BuildTaggedDocument()))
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);
        PdfDictionary mergedRoot = ResolveDictionary(
            merged, mergedCatalog[Name("StructTreeRoot")]);
        PdfDictionary mergedParentTree = Assert.IsType<PdfDictionary>(
            mergedRoot[Name("ParentTree")]);
        PdfArray mergedNumbers = Assert.IsType<PdfArray>(mergedParentTree[Name("Nums")]);
        Assert.Equal([1L, 2L, 3L], Enumerable.Range(0, mergedNumbers.Count / 2)
            .Select(index => Assert.IsType<PdfInteger>(mergedNumbers[index * 2]).Value));
    }

    [Fact]
    public void Build_PreservesCompleteOptionalContentConfigurationAndLayerReferences()
    {
        byte[] source = BuildLayeredDocument();
        byte[] target = new PdfDocumentBuilder().Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(PdfDocument.Open(source))
                .Build());
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary optionalContent = Assert.IsType<PdfDictionary>(
            catalog[Name("OCProperties")]);
        PdfIndirectReference groupReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(optionalContent[Name("OCGs")])[0]);
        (_, _, PdfDictionary[] pages) = FlatPages(reopened);

        Assert.Equal("Review layer", DecodeUnicode(Assert.IsType<PdfString>(
            ResolveDictionary(reopened, groupReference)[Name("Name")])));
        foreach (PdfDictionary page in pages)
        {
            PdfDictionary resources = Assert.IsType<PdfDictionary>(page[Name("Resources")]);
            PdfDictionary properties = Assert.IsType<PdfDictionary>(resources[Name("Properties")]);
            Assert.Equal(groupReference.ObjectNumber,
                Assert.IsType<PdfIndirectReference>(properties[Name("OC1")]).ObjectNumber);
        }
    }

    [Fact]
    public void Build_PreservesMetadataLanguageAndOutputIntentForSoleCompleteImport()
    {
        PdfIccProfile profile = PdfIccProfile.Load(BuildRgbProfile());
        byte[] source = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Imported archival document",
                Language = "en-US"
            })
            .SetOutputIntent(profile, "Test RGB")
            .EnablePdfA4Conformance()
            .AddBlankPage()
            .Build();
        byte[] target = new PdfDocumentBuilder().Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(PdfDocument.Open(source))
                .Build());
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfStream metadata = ResolveStream(reopened, catalog[Name("Metadata")]);
        PdfDictionary outputIntent = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(catalog[Name("OutputIntents")])[0]);
        PdfStream importedProfile = ResolveStream(reopened,
            outputIntent[Name("DestOutputProfile")]);

        Assert.Equal("en-US", DecodeUnicode(Assert.IsType<PdfString>(catalog[Name("Lang")])));
        Assert.Contains("pdfaid:part", Encoding.UTF8.GetString(metadata.EncodedData.Span));
        Assert.Equal(3, Assert.IsType<PdfInteger>(
            importedProfile.Dictionary[Name("N")]).Value);
        Assert.Equal(profile.Data.ToArray(), importedProfile.EncodedData.ToArray());
    }

    [Fact]
    public void Build_PreservesWholeCatalogForSoleCompleteImportExceptSignaturePermissions()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference sourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary sourceCatalog = ResolveDictionary(source, sourceCatalogReference);
        PdfIndirectReference customGraph = update.AddObject(new PdfDictionary([
            new(Name("Catalog"), sourceCatalogReference),
            new(Name("Value"), new PdfInteger(17))
        ]));
        PdfIndirectReference information = update.AddObject(new PdfDictionary([
            new(Name("Title"), new PdfString("Imported information"u8,
                PdfStringForm.Literal))
        ]));
        update.SetDocumentInformation(information);
        PdfIndirectReference sourcePage = FlatPages(source).References[0];
        update.ReplaceObject(sourceCatalogReference.ObjectNumber, new PdfDictionary(sourceCatalog
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("URI"), new PdfDictionary([
                new(Name("Base"), new PdfString("https://example.test/"u8,
                    PdfStringForm.Literal))
            ])))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("OpenAction"),
                new PdfArray([sourcePage, Name("Fit")])))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("CustomCatalogGraph"), customGraph))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Perms"),
                new PdfDictionary([new(Name("DocMDP"), customGraph)])))));
        source = PdfDocument.Open(update.Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedDocument(source).Build());
        PdfIndirectReference mergedCatalogReference = Assert.IsType<PdfIndirectReference>(
            merged.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(merged, mergedCatalogReference);
        PdfDictionary uri = DictionaryValue(merged, catalog[Name("URI")]);
        PdfArray openAction = Assert.IsType<PdfArray>(catalog[Name("OpenAction")]);
        PdfDictionary graph = ResolveDictionary(merged, catalog[Name("CustomCatalogGraph")]);
        PdfDictionary mergedInformation = ResolveDictionary(
            merged, merged.Trailer[Name("Info")]);

        Assert.Equal("https://example.test/", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(uri[Name("Base")]).Bytes.Span));
        Assert.Equal(FlatPages(merged).References[0].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(openAction[0]).ObjectNumber);
        Assert.Equal(mergedCatalogReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(graph[Name("Catalog")]).ObjectNumber);
        Assert.False(catalog.ContainsKey(Name("Perms")));
        Assert.Equal("Imported information", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(mergedInformation[Name("Title")]).Bytes.Span));
    }

    [Fact]
    public void LayeredDocuments_AllowSafePageChangesAndSelectedConfigurationMerges()
    {
        byte[] source = BuildLayeredDocument();
        PdfDocument layered = PdfDocument.Open(source);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());
        PdfDocument occupied = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument selectedLayer = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty).AddImportedPage(layered, 0).Build());
        PdfDictionary selectedLayerCatalog = ResolveDictionary(
            selectedLayer, selectedLayer.Trailer[Name("Root")]);
        Assert.Single(Assert.IsType<PdfArray>(DictionaryValue(
            selectedLayer, selectedLayerCatalog[Name("OCProperties")])[Name("OCGs")]));
        PdfDocument reducedLayerImport = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(layered).RemovePage(1).Build());
        Assert.True(ResolveDictionary(
                reducedLayerImport, reducedLayerImport.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));

        var mixedLayer = new PdfOptionalContentGroup("Mixed layer", initiallyVisible: true);
        PdfDocument mixed = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginOptionalContent(mixedLayer)
                .Rectangle(10, 10, 20, 20).Stroke()
                .EndMarkedContent())
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginText().SetFont(PdfStandardFont.Helvetica, 10)
                .MoveText(10, 50).ShowLatin1Text("literal /OC text").EndText())
            .Build());
        PdfDocument selectedMixedLayer = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty).AddImportedPage(mixed, 0).Build());
        Assert.True(ResolveDictionary(
                selectedMixedLayer, selectedMixedLayer.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));
        PdfDocument independent = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty).AddImportedPage(mixed, 1).Build());
        Assert.False(ResolveDictionary(independent, independent.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));
        Assert.False(Assert.IsType<PdfDictionary>(FlatPages(independent).Pages[0][Name("Resources")])
            .ContainsKey(Name("Properties")));
        PdfDocument reducedMixed = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(mixed).RemovePage(0).Build());
        Assert.False(ResolveDictionary(reducedMixed, reducedMixed.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));

        (_, _, PdfDictionary[] mixedPages) = FlatPages(mixed);
        PdfIndirectReference mixedContentReference = Assert.IsType<PdfIndirectReference>(
            mixedPages[1][Name("Contents")]);
        PdfStream mixedContent = ResolveStream(mixed, mixedContentReference);
        var inlineUpdate = new PdfIncrementalUpdateBuilder(mixed);
        inlineUpdate.ReplaceObject(mixedContentReference.ObjectNumber, new PdfStream(
            new PdfDictionary(mixedContent.Dictionary.Where(entry =>
                !entry.Key.Equals(Name("Length"))
                && !entry.Key.Equals(Name("Filter"))
                && !entry.Key.Equals(Name("DecodeParms")))),
            "BI /W 2 /H 1 /BPC 8 /CS /RGB ID /OCabc EI\n"u8));
        PdfDocument inlineImageSource = PdfDocument.Open(inlineUpdate.Build());
        PdfDocument inlineImagePage = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(inlineImageSource, 1).Build());
        Assert.False(ResolveDictionary(
                inlineImagePage, inlineImagePage.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));

        var nestedLayer = new PdfOptionalContentGroup("Nested layer");
        var layeredForm = new PdfFormXObject(20, 20, new PdfContentStreamBuilder()
            .BeginOptionalContent(nestedLayer)
            .Rectangle(0, 0, 20, 20).Fill()
            .EndMarkedContent());
        PdfDocument nested = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .DrawForm(layeredForm, 10, 10))
            .Build());
        (_, _, PdfDictionary[] nestedPages) = FlatPages(nested);
        PdfDictionary nestedResources = DictionaryValue(
            nested, nestedPages[0][Name("Resources")]);
        PdfDictionary nestedXObjects = DictionaryValue(
            nested, nestedResources[Name("XObject")]);
        PdfIndirectReference nestedFormReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(nestedXObjects).Value);
        PdfStream nestedFormStream = ResolveStream(nested, nestedFormReference);
        PdfDictionary nestedCatalog = ResolveDictionary(
            nested, nested.Trailer[Name("Root")]);
        PdfDictionary nestedProperties = DictionaryValue(
            nested, nestedCatalog[Name("OCProperties")]);
        PdfIndirectReference nestedGroupReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(nestedProperties[Name("OCGs")])));
        PdfDictionary nestedGroup = ResolveDictionary(nested, nestedGroupReference);
        var nestedAliasUpdate = new PdfIncrementalUpdateBuilder(nested);
        PdfIndirectReference formSubtype = nestedAliasUpdate.AddObject(Name("Form"));
        PdfIndirectReference formSubtypeAlias = nestedAliasUpdate.AddObject(formSubtype);
        PdfIndirectReference groupType = nestedAliasUpdate.AddObject(Name("OCG"));
        PdfIndirectReference groupTypeAlias = nestedAliasUpdate.AddObject(groupType);
        PdfIndirectReference groupName = nestedAliasUpdate.AddObject(
            new PdfString("Nested layer"u8, PdfStringForm.Literal));
        PdfIndirectReference groupNameAlias = nestedAliasUpdate.AddObject(groupName);
        nestedAliasUpdate.ReplaceObject(nestedFormReference.ObjectNumber,
            new PdfStream(new PdfDictionary(nestedFormStream.Dictionary.Select(entry =>
                entry.Key.Equals(Name("Subtype"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, formSubtypeAlias)
                    : entry)), nestedFormStream.EncodedData.Span));
        nestedAliasUpdate.ReplaceObject(nestedGroupReference.ObjectNumber,
            new PdfDictionary(nestedGroup.Select(entry =>
                entry.Key.Equals(Name("Type"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, groupTypeAlias)
                    : entry.Key.Equals(Name("Name"))
                        ? new KeyValuePair<PdfName, PdfObject>(entry.Key, groupNameAlias)
                        : entry)));
        nested = PdfDocument.Open(nestedAliasUpdate.Build());
        PdfDocument selectedNestedLayer = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty).AddImportedPage(nested, 0).Build());
        Assert.True(ResolveDictionary(
                selectedNestedLayer, selectedNestedLayer.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));

        var omittedLayer = new PdfOptionalContentGroup("Omitted layer", initiallyVisible: true);
        var retainedLayer = new PdfOptionalContentGroup("Retained layer", initiallyVisible: false);
        PdfDocument distinctLayers = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginOptionalContent(omittedLayer)
                .Rectangle(10, 10, 20, 20).Fill().EndMarkedContent())
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginOptionalContent(retainedLayer)
                .Rectangle(30, 30, 20, 20).Fill().EndMarkedContent())
            .Build());
        PdfDocument retainedLayerOnly = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(distinctLayers, 1).Build());
        PdfDictionary retainedCatalog = ResolveDictionary(
            retainedLayerOnly, retainedLayerOnly.Trailer[Name("Root")]);
        PdfDictionary retainedProperties = DictionaryValue(
            retainedLayerOnly, retainedCatalog[Name("OCProperties")]);
        PdfIndirectReference retainedGroupReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(retainedProperties[Name("OCGs")] )));
        PdfDictionary retainedGroup = ResolveDictionary(
            retainedLayerOnly, retainedGroupReference);
        Assert.Equal("Retained layer", DecodeUnicode(
            Assert.IsType<PdfString>(retainedGroup[Name("Name")] )));
        PdfDictionary retainedDefault = DictionaryValue(
            retainedLayerOnly, retainedProperties[Name("D")]);
        Assert.Single(Assert.IsType<PdfArray>(retainedDefault[Name("OFF")]));

        PdfDocument occupiedMerge = PdfDocument.Open(
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(layered).Build());
        PdfDictionary occupiedCatalog = ResolveDictionary(
            occupiedMerge, occupiedMerge.Trailer[Name("Root")]);
        (_, _, PdfDictionary[] occupiedPages) = FlatPages(occupiedMerge);
        Assert.Equal(3, occupiedPages.Length);
        Assert.True(occupiedCatalog.ContainsKey(Name("OCProperties")));
        Assert.False(Assert.IsType<PdfDictionary>(
            occupiedPages[0][Name("Resources")]).ContainsKey(Name("Properties")));
        Assert.True(Assert.IsType<PdfDictionary>(
            occupiedPages[1][Name("Resources")]).ContainsKey(Name("Properties")));
        PdfDocument combinedLayers = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(layered)
                .AddImportedDocument(layered)
                .Build());
        Assert.Equal(4, FlatPages(combinedLayers).Pages.Length);
        PdfDocument reduced = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .RemovePage(0).Build());
        PdfDictionary reducedCatalog = ResolveDictionary(
            reduced, reduced.Trailer[Name("Root")]);
        Assert.True(reducedCatalog.ContainsKey(Name("OCProperties")));
        (_, _, PdfDictionary[] reducedPages) = FlatPages(reduced);
        Assert.Single(reducedPages);
        Assert.True(Assert.IsType<PdfDictionary>(
                reducedPages[0][Name("Resources")]).ContainsKey(Name("Properties")));

        PdfDocument extended = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .AddBlankPage().Build());
        Assert.Equal(3, FlatPages(extended).Pages.Length);
        Assert.True(ResolveDictionary(extended, extended.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));

        PdfDocument reordered = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .MovePage(0, 1)
                .Build());
        Assert.True(ResolveDictionary(reordered, reordered.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));
    }

    [Fact]
    public void LayeredDocuments_MergeGroupsVisibilityOrderAndPageResources()
    {
        PdfDocument target = PdfDocument.Open(BuildLayeredDocument());
        PdfDocument source = PdfDocument.Open(BuildLayeredDocument());
        PdfDictionary targetCatalog = ResolveDictionary(
            target, target.Trailer[Name("Root")]);
        PdfDictionary targetProperties = DictionaryValue(
            target, targetCatalog[Name("OCProperties")]);
        var targetUpdate = new PdfIncrementalUpdateBuilder(target);
        PdfIndirectReference targetGroupReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(targetProperties[Name("OCGs")])));
        PdfIndirectReference targetGroupAlias = targetUpdate.AddObject(targetGroupReference);
        PdfIndirectReference targetGroupOuterAlias = targetUpdate.AddObject(targetGroupAlias);
        PdfDictionary aliasedTargetProperties = new(targetProperties.Select(entry =>
            entry.Key.Equals(Name("OCGs"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                    new PdfArray([targetGroupOuterAlias]))
                : entry));
        PdfIndirectReference propertiesReference = targetUpdate.AddObject(aliasedTargetProperties);
        PdfIndirectReference propertiesAlias = targetUpdate.AddObject(propertiesReference);
        PdfIndirectReference propertiesOuterAlias = targetUpdate.AddObject(propertiesAlias);
        PdfIndirectReference targetCatalogReference = Assert.IsType<PdfIndirectReference>(
            target.Trailer[Name("Root")]);
        targetUpdate.ReplaceObject(targetCatalogReference.ObjectNumber,
            new PdfDictionary(targetCatalog.Select(entry =>
                entry.Key.Equals(Name("OCProperties"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                        propertiesOuterAlias)
                    : entry)));
        target = PdfDocument.Open(targetUpdate.Build());

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary properties = ResolveDictionary(merged,
            catalog[Name("OCProperties")]);
        PdfArray groups = Assert.IsType<PdfArray>(properties[Name("OCGs")]);
        PdfDictionary configuration = Assert.IsType<PdfDictionary>(properties[Name("D")]);
        PdfArray hidden = Assert.IsType<PdfArray>(configuration[Name("OFF")]);
        PdfArray order = Assert.IsType<PdfArray>(configuration[Name("Order")]);
        (_, _, PdfDictionary[] pages) = FlatPages(merged);
        PdfDictionary importedResources = Assert.IsType<PdfDictionary>(pages[2][Name("Resources")]);
        PdfDictionary importedProperties = Assert.IsType<PdfDictionary>(
            importedResources[Name("Properties")]);
        PdfIndirectReference importedGroup = Assert.IsType<PdfIndirectReference>(
            importedProperties[Name("OC1")]);

        Assert.Equal(4, pages.Length);
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, hidden.Count);
        Assert.Equal(2, order.Count);
        Assert.Equal(propertiesOuterAlias.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(catalog[Name("OCProperties")]).ObjectNumber);
        Assert.Equal(targetGroupOuterAlias.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(groups[0]).ObjectNumber);
        Assert.Equal(Assert.IsType<PdfIndirectReference>(groups[1]).ObjectNumber,
            importedGroup.ObjectNumber);
    }

    [Fact]
    public void SelectedLayerImports_RemoveStaleConfigurationReferences()
    {
        var omittedLayer = new PdfOptionalContentGroup("Omitted layer");
        var retainedLayer = new PdfOptionalContentGroup("Retained layer");
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginOptionalContent(omittedLayer)
                .Rectangle(10, 10, 20, 20).Fill().EndMarkedContent())
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginOptionalContent(retainedLayer)
                .Rectangle(30, 30, 20, 20).Fill().EndMarkedContent())
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary properties = DictionaryValue(source, catalog[Name("OCProperties")]);
        PdfArray groups = Assert.IsType<PdfArray>(properties[Name("OCGs")]);
        PdfIndirectReference retainedReference = Assert.IsType<PdfIndirectReference>(groups[1]);
        PdfDictionary configuration = DictionaryValue(source, properties[Name("D")]);
        PdfDictionary staleConfiguration = new(configuration
            .Where(entry => !entry.Key.Equals(Name("Order")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Order"), new PdfArray([
                retainedReference,
                new PdfIndirectReference(
                    retainedReference.ObjectNumber, retainedReference.Generation + 1)
            ]))));
        PdfDictionary staleProperties = new(properties
            .Where(entry => !entry.Key.Equals(Name("D")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("D"), staleConfiguration)));
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
            .Where(entry => !entry.Key.Equals(Name("OCProperties")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("OCProperties"), staleProperties))));
        source = PdfDocument.Open(update.Build());

        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 1)
            .Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selected, selected.Trailer[Name("Root")]);
        PdfDictionary selectedProperties = DictionaryValue(
            selected, selectedCatalog[Name("OCProperties")]);
        PdfDictionary selectedConfiguration = DictionaryValue(
            selected, selectedProperties[Name("D")]);
        PdfArray order = Assert.IsType<PdfArray>(selectedConfiguration[Name("Order")]);

        Assert.Single(order);
        Assert.IsType<PdfIndirectReference>(order[0]);
    }

    [Fact]
    public void LayeredDocumentMerges_RejectDuplicateGroupRegistrations()
    {
        PdfDocument source = PdfDocument.Open(BuildLayeredDocument());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary properties = DictionaryValue(source, catalog[Name("OCProperties")]);
        PdfIndirectReference group = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(properties[Name("OCGs")])));
        PdfDictionary duplicateProperties = new(properties
            .Where(entry => !entry.Key.Equals(Name("OCGs")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("OCGs"),
                new PdfArray([group, group]))));
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
            .Where(entry => !entry.Key.Equals(Name("OCProperties")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("OCProperties"), duplicateProperties))));
        source = PdfDocument.Open(update.Build());
        PdfDocument occupied = PdfDocument.Open(BuildLayeredDocument());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(source)
                .Build());

        Assert.Contains("duplicate group reference", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LayeredDocumentMerges_RejectInvalidGroupRegistrations()
    {
        PdfDocument original = PdfDocument.Open(BuildLayeredDocument());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(original, catalogReference);
        PdfDictionary properties = DictionaryValue(original, catalog[Name("OCProperties")]);
        PdfIndirectReference groupReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(properties[Name("OCGs")])));
        PdfDictionary group = ResolveDictionary(original, groupReference);

        var invalidTypeUpdate = new PdfIncrementalUpdateBuilder(original);
        invalidTypeUpdate.ReplaceObject(groupReference.ObjectNumber, new PdfDictionary(group
            .Where(entry => !entry.Key.Equals(Name("Type")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Type"), Name("Pages")))));
        PdfDocument invalidType = PdfDocument.Open(invalidTypeUpdate.Build());
        PdfDocument occupied = PdfDocument.Open(BuildLayeredDocument());

        InvalidOperationException typeError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(invalidType)
                .Build());
        Assert.Contains("/Type is not /OCG", typeError.Message,
            StringComparison.Ordinal);

        PdfIndirectReference staleGroup = new(
            groupReference.ObjectNumber, groupReference.Generation + 1);
        PdfDictionary staleProperties = new(properties
            .Where(entry => !entry.Key.Equals(Name("OCGs")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("OCGs"), new PdfArray([staleGroup]))));
        var staleUpdate = new PdfIncrementalUpdateBuilder(original);
        staleUpdate.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
            .Where(entry => !entry.Key.Equals(Name("OCProperties")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("OCProperties"), staleProperties))));
        PdfDocument stale = PdfDocument.Open(staleUpdate.Build());

        Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(stale)
                .Build());

        PdfDictionary configuration = DictionaryValue(original, properties[Name("D")]);
        PdfDictionary invalidConfiguration = new(configuration
            .Where(entry => !entry.Key.Equals(Name("ListMode")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("ListMode"), Name("SometimesVisible"))));
        PdfDictionary invalidConfigurationProperties = new(properties
            .Where(entry => !entry.Key.Equals(Name("D")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("D"), invalidConfiguration)));
        var invalidConfigurationUpdate = new PdfIncrementalUpdateBuilder(original);
        invalidConfigurationUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("OCProperties")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("OCProperties"), invalidConfigurationProperties))));
        PdfDocument invalidListMode = PdfDocument.Open(invalidConfigurationUpdate.Build());

        InvalidOperationException listModeError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(invalidListMode)
                .Build());
        Assert.Contains("/ListMode /SometimesVisible is not defined", listModeError.Message,
            StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(
                    PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(invalidListMode)
                .Build());

        PdfDictionary unregisteredConfiguration = new(configuration
            .Where(entry => !entry.Key.Equals(Name("ON")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("ON"), new PdfArray([staleGroup]))));
        PdfDictionary unregisteredProperties = new(properties
            .Where(entry => !entry.Key.Equals(Name("D")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("D"), unregisteredConfiguration)));
        var unregisteredUpdate = new PdfIncrementalUpdateBuilder(original);
        unregisteredUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("OCProperties")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("OCProperties"), unregisteredProperties))));
        PdfDocument unregistered = PdfDocument.Open(unregisteredUpdate.Build());

        InvalidOperationException unregisteredError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(unregistered)
                .Build());
        Assert.Contains("/ON entry is absent from /OCGs", unregisteredError.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LayeredDocumentMerges_RejectInvalidConfigurationCollections()
    {
        PdfDocument original = PdfDocument.Open(BuildLayeredDocument());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(original, catalogReference);
        PdfDictionary properties = DictionaryValue(original, catalog[Name("OCProperties")]);
        PdfDictionary configuration = DictionaryValue(original, properties[Name("D")]);
        PdfIndirectReference groupReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(properties[Name("OCGs")])));
        PdfIndirectReference staleGroup = new(
            groupReference.ObjectNumber, groupReference.Generation + 1);
        PdfDocument occupied = PdfDocument.Open(BuildLayeredDocument());

        PdfDocument invalidLocked = WithDefault(new PdfDictionary(configuration
            .Where(entry => !entry.Key.Equals(Name("Locked")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("Locked"), new PdfArray([staleGroup])))));
        InvalidOperationException lockedError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(invalidLocked)
                .Build());
        Assert.Contains("/Locked entry is absent from /OCGs", lockedError.Message,
            StringComparison.Ordinal);

        var invalidApplication = new PdfDictionary([
            new(Name("Event"), Name("Sometimes")),
            new(Name("Category"), new PdfArray([Name("View")])),
            new(Name("OCGs"), new PdfArray([groupReference]))
        ]);
        PdfDocument invalidAs = WithDefault(new PdfDictionary(configuration
            .Where(entry => !entry.Key.Equals(Name("AS")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("AS"), new PdfArray([invalidApplication])))));
        InvalidOperationException asError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(invalidAs)
                .Build());
        Assert.Contains("/AS entry has an invalid /Event", asError.Message,
            StringComparison.Ordinal);
        return;

        PdfDocument WithDefault(PdfDictionary replacement)
        {
            PdfDictionary replacementProperties = new(properties
                .Where(entry => !entry.Key.Equals(Name("D")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("D"), replacement)));
            var update = new PdfIncrementalUpdateBuilder(original);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("OCProperties")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("OCProperties"), replacementProperties))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void LayeredDocuments_ImportSelectedLayerAcrossDistinctEncryptionKeys()
    {
        var firstLayer = new PdfOptionalContentGroup("Encrypted omitted layer");
        var secondLayer = new PdfOptionalContentGroup(
            "Encrypted selected layer", initiallyVisible: false);
        byte[] sourceBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "source-user", OwnerPassword = "source-owner"
            })
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginOptionalContent(firstLayer)
                .Rectangle(10, 10, 20, 20).Fill().EndMarkedContent())
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginOptionalContent(secondLayer)
                .Rectangle(30, 30, 20, 20).Fill().EndMarkedContent())
            .Build();
        byte[] targetBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "target-user", OwnerPassword = "target-owner"
            })
            .Build();

        byte[] result = new PdfIncrementalPageEditor(
                PdfDocument.Open(targetBytes, "target-owner"))
            .AddImportedPage(PdfDocument.Open(sourceBytes, "source-user"), 1)
            .Build();
        PdfDocument reopened = PdfDocument.Open(result, "target-user");
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary properties = DictionaryValue(
            reopened, catalog[Name("OCProperties")]);

        Assert.Single(Assert.IsType<PdfArray>(properties[Name("OCGs")]));
        Assert.Equal(-1, result.AsSpan().IndexOf("Encrypted selected layer"u8));
        Assert.Equal(-1, result.AsSpan().IndexOf("Encrypted omitted layer"u8));
    }

    [Fact]
    public void Build_PreservesLinksBetweenPagesImportedTogether()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .AddBlankPage(400, 500)
            .AddPageLink(0, 10, 10, 40, 20, 1)
            .Build());
        byte[] targetBytes = new PdfDocumentBuilder().Build();
        var editor = new PdfIncrementalPageEditor(PdfDocument.Open(targetBytes))
            .AddImportedDocument(source);

        PdfDocument reopened = PdfDocument.Open(editor.Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(reopened);
        PdfDictionary link = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[0][Name("Annots")])[0]);
        PdfArray destination = Assert.IsType<PdfArray>(link[Name("Dest")]);

        Assert.Equal(references[1].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(destination[0]).ObjectNumber);
        Assert.Equal([200d, 400d], pages.Select(BoxWidth));
    }

    [Fact]
    public void Import_RejectsOmittedPageLinksAndPrunesSelectedFormState()
    {
        PdfDocument linkedPages = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage().AddPageLink(0, 0, 0, 20, 20, 1).Build());
        PdfDocument formPage = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddTextField(0, "name", 10, 10, 100, 20).Build());
        byte[] target = new PdfDocumentBuilder().Build();

        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedPage(linkedPages, 0).Build());
        PdfDocument selectedForm = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedPage(formPage, 0).Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selectedForm, selectedForm.Trailer[Name("Root")]);
        PdfDictionary selectedAcroForm = DictionaryValue(
            selectedForm, selectedCatalog[Name("AcroForm")]);
        PdfArray selectedFields = Assert.IsType<PdfArray>(selectedAcroForm[Name("Fields")]);
        PdfArray selectedAnnotations = Assert.IsType<PdfArray>(
            FlatPages(selectedForm).Pages[0][Name("Annots")]);
        Assert.Single(selectedFields);
        Assert.Equal(Assert.IsType<PdfIndirectReference>(selectedFields[0]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(selectedAnnotations[0]).ObjectNumber);

        PdfDocument ordinaryPage = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedPage(formPage, 1)
                .Build());
        PdfDictionary ordinaryCatalog = ResolveDictionary(
            ordinaryPage, ordinaryPage.Trailer[Name("Root")]);
        Assert.False(ordinaryCatalog.ContainsKey(Name("AcroForm")));
    }

    [Fact]
    public void Build_CanImportAnIndependentPageWithASelfLink()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .AddPageLink(0, 10, 10, 40, 20, 0)
            .Build());

        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(reopened);
        PdfDictionary link = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[0][Name("Annots")])[0]);
        PdfArray destination = Assert.IsType<PdfArray>(link[Name("Dest")]);

        Assert.Equal(pageReferences[0].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(destination[0]).ObjectNumber);
    }

    [Fact]
    public void Build_CanImportAnOrderedPageSubsetWithInternalLinks()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .AddBlankPage(300, 400)
            .AddBlankPage(400, 500)
            .AddPageLink(0, 10, 10, 40, 20, 2)
            .Build());

        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPages(source, [2, 0])
            .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(reopened);
        PdfDictionary link = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[1][Name("Annots")])[0]);
        PdfArray destination = Assert.IsType<PdfArray>(link[Name("Dest")]);

        Assert.Equal([400d, 200d], pages.Select(BoxWidth));
        Assert.Equal(pageReferences[0].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(destination[0]).ObjectNumber);
    }

    [Fact]
    public void SelectedPageImport_RejectsOmittedDependenciesAndDuplicateSelections()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage().AddBlankPage()
            .AddPageLink(0, 0, 0, 20, 20, 2)
            .Build());
        PdfDocument target = PdfDocument.Open(new PdfDocumentBuilder().Build());

        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedPages(source, [0, 1])
                .Build());
        Assert.Throws<ArgumentException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedPages(source, [0, 0]));
        Assert.Throws<ArgumentException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedPages(source, [0, 1, 2, 0]));
    }

    [Fact]
    public void Build_CanImportTheSameIndependentPageMoreThanOnce()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300)
            .AddTextNote(0, 150, 250, "Copy me")
            .Build());

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0)
                .AddImportedPage(source, 0)
                .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(reopened);
        PdfIndirectReference firstAnnotation = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(pages[0][Name("Annots")])[0]);
        PdfIndirectReference secondAnnotation = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(pages[1][Name("Annots")])[0]);
        PdfDictionary firstNote = ResolveDictionary(reopened, firstAnnotation);
        PdfDictionary secondNote = ResolveDictionary(reopened, secondAnnotation);

        Assert.NotEqual(pageReferences[0].ObjectNumber, pageReferences[1].ObjectNumber);
        Assert.NotEqual(firstAnnotation.ObjectNumber, secondAnnotation.ObjectNumber);
        Assert.Equal(pageReferences[0].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(firstNote[Name("P")]).ObjectNumber);
        Assert.Equal(pageReferences[1].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(secondNote[Name("P")]).ObjectNumber);
    }

    [Fact]
    public void Build_CanImportTheSameLinkedDocumentMoreThanOnce()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddPageLink(0, 10, 10, 40, 20, 1)
            .Build());

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(source)
                .AddImportedDocument(source)
                .Build());
        (_, PdfIndirectReference[] references, PdfDictionary[] pages) = FlatPages(reopened);
        PdfDictionary firstLink = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[0][Name("Annots")])[0]);
        PdfDictionary secondLink = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[2][Name("Annots")])[0]);

        Assert.Equal(references[1].ObjectNumber, Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(firstLink[Name("Dest")])[0]).ObjectNumber);
        Assert.Equal(references[3].ObjectNumber, Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(secondLink[Name("Dest")])[0]).ObjectNumber);
    }

    [Fact]
    public void ImportedPageGraphs_AreDeterministic()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(200, 300, new PdfContentStreamBuilder()
                .SetFillRgb(0.2, 0.4, 0.8).Rectangle(20, 20, 160, 260).Fill())
            .AddTextNote(0, 150, 250, "Import")
            .Build());
        byte[] target = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] Import() => new PdfIncrementalPageEditor(PdfDocument.Open(target))
            .AddImportedPage(source, 0).Build();

        Assert.Equal(Import(), Import());
    }

    [Fact]
    public void Build_ImportsTheAcroFormWithACompleteSourceDocument()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(300, 400)
            .AddBlankPage(400, 500)
            .AddTextField(0, "customer.name", 20, 300, 180, 24, "Steve")
            .AddCheckBox(1, "customer.approved", 20, 400, 18, 18, isChecked: true)
            .Build());
        byte[] target = new PdfDocumentBuilder().AddBlankPage(100, 100).Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(source)
                .Build());
        (PdfIndirectReference _, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) =
            FlatPages(reopened);
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary acroForm = catalog[Name("AcroForm")] is PdfIndirectReference acroFormReference
            ? ResolveDictionary(reopened, acroFormReference)
            : Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfArray fields = Assert.IsType<PdfArray>(acroForm[Name("Fields")]);
        PdfArray textAnnotations = Assert.IsType<PdfArray>(pages[1][Name("Annots")]);
        PdfArray checkAnnotations = Assert.IsType<PdfArray>(pages[2][Name("Annots")]);
        PdfDictionary textWidget = ResolveDictionary(reopened, textAnnotations[0]);
        PdfDictionary checkWidget = ResolveDictionary(reopened, checkAnnotations[0]);

        Assert.Equal(3, pages.Length);
        PdfIndirectReference parentReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(fields));
        PdfDictionary parent = ResolveDictionary(reopened, parentReference);
        PdfArray children = Assert.IsType<PdfArray>(parent[Name("Kids")]);
        Assert.Equal(2, children.Count);
        Assert.Equal(Assert.IsType<PdfIndirectReference>(children[0]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(textAnnotations[0]).ObjectNumber);
        Assert.Equal(Assert.IsType<PdfIndirectReference>(children[1]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(checkAnnotations[0]).ObjectNumber);
        Assert.Equal(parentReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(textWidget[Name("Parent")]).ObjectNumber);
        Assert.Equal(parentReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(checkWidget[Name("Parent")]).ObjectNumber);
        Assert.Equal(pageReferences[1].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(textWidget[Name("P")]).ObjectNumber);
        Assert.Equal(pageReferences[2].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(checkWidget[Name("P")]).ObjectNumber);
        Assert.IsType<PdfDictionary>(textWidget[Name("AP")]);
        Assert.IsType<PdfDictionary>(checkWidget[Name("AP")]);
    }

    [Fact]
    public void SelectedFormImports_PruneAuthoredQualifiedFieldHierarchies()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .AddTextField(0, "customer.name", 20, 20, 120, 20, "Steve")
            .AddCheckBox(1, "customer.approved", 20, 20, 20, 20, isChecked: true)
            .Build());
        PdfDictionary sourceCatalog = ResolveDictionary(
            source, source.Trailer[Name("Root")]);
        PdfDictionary sourceForm = DictionaryValue(
            source, sourceCatalog[Name("AcroForm")]);
        PdfIndirectReference sourceParentReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(sourceForm[Name("Fields")])));
        PdfDictionary sourceParent = ResolveDictionary(source, sourceParentReference);
        PdfIndirectReference sourceWidgetReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(sourceParent[Name("Kids")])[0]);
        (_, PdfIndirectReference[] sourcePageReferences, PdfDictionary[] sourcePages) =
            FlatPages(source);
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference parentAlias = sourceUpdate.AddObject(sourceParentReference);
        PdfIndirectReference parentOuterAlias = sourceUpdate.AddObject(parentAlias);
        PdfIndirectReference widgetAlias = sourceUpdate.AddObject(sourceWidgetReference);
        PdfIndirectReference widgetOuterAlias = sourceUpdate.AddObject(widgetAlias);
        PdfDictionary sourceWidget = ResolveDictionary(source, sourceWidgetReference);
        PdfIndirectReference widgetSubtype = sourceUpdate.AddObject(Name("Widget"));
        PdfIndirectReference widgetSubtypeAlias = sourceUpdate.AddObject(widgetSubtype);
        sourceUpdate.ReplaceObject(sourceWidgetReference.ObjectNumber,
            new PdfDictionary(sourceWidget.Select(entry =>
                entry.Key.Equals(Name("Subtype"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, widgetSubtypeAlias)
                    : entry)));
        sourceUpdate.ReplaceObject(sourceParentReference.ObjectNumber,
            new PdfDictionary(sourceParent.Select(entry =>
                entry.Key.Equals(Name("Kids"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                        new PdfArray([widgetOuterAlias,
                            Assert.IsType<PdfArray>(sourceParent[Name("Kids")])[1]]))
                    : entry)));
        sourceUpdate.ReplaceObject(sourcePageReferences[0].ObjectNumber,
            new PdfDictionary(sourcePages[0].Select(entry =>
                entry.Key.Equals(Name("Annots"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                        new PdfArray([widgetOuterAlias]))
                    : entry)));
        PdfIndirectReference sourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        sourceUpdate.ReplaceObject(sourceCatalogReference.ObjectNumber,
            new PdfDictionary(sourceCatalog.Select(entry =>
                entry.Key.Equals(Name("AcroForm"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                        new PdfDictionary(sourceForm.Select(formEntry =>
                            formEntry.Key.Equals(Name("Fields"))
                                ? new KeyValuePair<PdfName, PdfObject>(formEntry.Key,
                                    new PdfArray([parentOuterAlias]))
                                : formEntry)))
                    : entry)));
        source = PdfDocument.Open(sourceUpdate.Build());
        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        PdfDictionary catalog = ResolveDictionary(selected, selected.Trailer[Name("Root")]);
        PdfDictionary form = DictionaryValue(selected, catalog[Name("AcroForm")]);
        PdfIndirectReference parentReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(form[Name("Fields")])));
        PdfDictionary parent = ResolveDictionary(selected, parentReference);
        PdfIndirectReference widgetReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(parent[Name("Kids")])));
        PdfDictionary widget = ResolveDictionary(selected, widgetReference);

        Assert.Equal("customer", DecodeUnicode(Assert.IsType<PdfString>(parent[Name("T")])));
        Assert.Equal("name", DecodeUnicode(Assert.IsType<PdfString>(widget[Name("T")])));
        PdfObject resolvedParent = selected.Resolve(parentReference);
        Assert.IsType<PdfIndirectReference>(resolvedParent);
        PdfIndirectReference finalParentReference = parentReference;
        while (resolvedParent is PdfIndirectReference reference)
        {
            finalParentReference = reference;
            resolvedParent = selected.Resolve(reference);
        }
        Assert.Equal(finalParentReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(widget[Name("Parent")]).ObjectNumber);
        Assert.IsType<PdfIndirectReference>(selected.Resolve(widgetReference));
    }

    [Fact]
    public void SelectedFormImports_DoNotApplyPrunedOverridesToStaleGenerations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .AddTextField(0, "customer.name", 20, 20, 120, 20, "Steve")
            .AddCheckBox(1, "customer.approved", 20, 20, 20, 20, isChecked: true)
            .Build());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfDictionary form = DictionaryValue(source, catalog[Name("AcroForm")]);
        PdfIndirectReference parentReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(form[Name("Fields")])));
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(pageReferences[0].ObjectNumber, new PdfDictionary(
            pages[0].Append(new KeyValuePair<PdfName, PdfObject>(
                Name("StaleField"), new PdfIndirectReference(
                    parentReference.ObjectNumber, parentReference.Generation + 1)))));
        source = PdfDocument.Open(update.Build());

        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        (_, _, PdfDictionary[] selectedPages) = FlatPages(selected);

        Assert.IsType<PdfNull>(selectedPages[0][Name("StaleField")]);
    }

    [Fact]
    public void SelectedPageImports_DoNotTreatStalePageGenerationsAsOmittedPages()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(pageReferences[0].ObjectNumber, new PdfDictionary(
            pages[0].Append(new KeyValuePair<PdfName, PdfObject>(
                Name("StalePage"), new PdfIndirectReference(
                    pageReferences[1].ObjectNumber, pageReferences[1].Generation + 1)))));
        source = PdfDocument.Open(update.Build());

        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        (_, _, PdfDictionary[] selectedPages) = FlatPages(selected);

        Assert.IsType<PdfNull>(selectedPages[0][Name("StalePage")]);
    }

    [Fact]
    public void SelectedPageImports_RemoveStaleAnnotationReferences()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(pageReferences[0].ObjectNumber, new PdfDictionary(
            pages[0].Append(new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                new PdfArray([new PdfIndirectReference(
                    pageReferences[1].ObjectNumber,
                    pageReferences[1].Generation + 1)])))));
        source = PdfDocument.Open(update.Build());

        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        (_, _, PdfDictionary[] selectedPages) = FlatPages(selected);

        Assert.False(selectedPages[0].ContainsKey(Name("Annots")));
    }

    [Fact]
    public void SelectedPageImports_RemoveStaleAssociatedFileReferences()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(pageReferences[0].ObjectNumber, new PdfDictionary(
            pages[0].Append(new KeyValuePair<PdfName, PdfObject>(Name("AF"),
                new PdfArray([new PdfIndirectReference(
                    pageReferences[1].ObjectNumber,
                    pageReferences[1].Generation + 1)])))));
        source = PdfDocument.Open(update.Build());

        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        (_, _, PdfDictionary[] selectedPages) = FlatPages(selected);

        Assert.False(selectedPages[0].ContainsKey(Name("AF")));
    }

    [Fact]
    public void SelectedPageImports_RejectInvalidAssociatedFileSpecifications()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(source);
        PdfDictionary invalidFile = new([
            new(Name("Type"), Name("Wrong")),
            new(Name("F"), new PdfString("invalid.txt"u8, PdfStringForm.Literal))
        ]);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(pageReferences[0].ObjectNumber, new PdfDictionary(
                pages[0].Append(new KeyValuePair<PdfName, PdfObject>(Name("AF"),
                    new PdfArray([invalidFile])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 0).Build());

        Assert.Contains("imported page /AF entry has an invalid /Type value",
            error.Message, StringComparison.Ordinal);

        PdfDocument embeddedSource = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());
        (_, PdfIndirectReference[] embeddedReferences,
            PdfDictionary[] embeddedPages) = FlatPages(embeddedSource);
        var embeddedUpdate = new PdfIncrementalUpdateBuilder(embeddedSource);
        PdfIndirectReference invalidStream = embeddedUpdate.AddObject(
            new PdfStream(new PdfDictionary([
                new(Name("Type"), Name("Wrong"))
            ]), []));
        PdfDictionary invalidEmbeddedFile = new([
            new(Name("Type"), Name("Filespec")),
            new(Name("F"), new PdfString("invalid.txt"u8, PdfStringForm.Literal)),
            new(Name("EF"), new PdfDictionary([
                new(Name("F"), invalidStream)
            ]))
        ]);
        embeddedUpdate.ReplaceObject(embeddedReferences[0].ObjectNumber,
            new PdfDictionary(embeddedPages[0].Append(
                new KeyValuePair<PdfName, PdfObject>(Name("AF"),
                    new PdfArray([invalidEmbeddedFile])))));
        PdfDocument invalidEmbeddedSource = PdfDocument.Open(embeddedUpdate.Build());
        InvalidOperationException embeddedError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(invalidEmbeddedSource, 0).Build());
        Assert.Contains("/EF /F stream has an invalid /Type value",
            embeddedError.Message, StringComparison.Ordinal);

        PdfDocument checksumSource = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());
        (_, PdfIndirectReference[] checksumReferences,
            PdfDictionary[] checksumPages) = FlatPages(checksumSource);
        var checksumUpdate = new PdfIncrementalUpdateBuilder(checksumSource);
        PdfIndirectReference checksumStream = checksumUpdate.AddObject(
            new PdfStream(new PdfDictionary([
                new(Name("Type"), Name("EmbeddedFile")),
                new(Name("Subtype"), Name("application/octet-stream")),
                new(Name("Params"), new PdfDictionary([
                    new(Name("CheckSum"), new PdfString([1], PdfStringForm.Hexadecimal))
                ]))
            ]), []));
        PdfDictionary checksumFile = new([
            new(Name("Type"), Name("Filespec")),
            new(Name("F"), new PdfString("invalid.txt"u8, PdfStringForm.Literal)),
            new(Name("EF"), new PdfDictionary([
                new(Name("F"), checksumStream)
            ]))
        ]);
        checksumUpdate.ReplaceObject(checksumReferences[0].ObjectNumber,
            new PdfDictionary(checksumPages[0].Append(
                new KeyValuePair<PdfName, PdfObject>(Name("AF"),
                    new PdfArray([checksumFile])))));
        PdfDocument invalidChecksumSource = PdfDocument.Open(checksumUpdate.Build());
        InvalidOperationException checksumError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(invalidChecksumSource, 0).Build());
        Assert.Contains("/Params /CheckSum is not a 16-byte string",
            checksumError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteDocumentImports_RemoveStaleCatalogAssociatedFiles()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("AF"), new PdfArray([
                new PdfIndirectReference(
                    pages[1].ObjectNumber, pages[1].Generation + 1)
            ])))));
        source = PdfDocument.Open(update.Build());
        PdfDocument target = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(source)
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);

        Assert.False(mergedCatalog.ContainsKey(Name("AF")));
    }

    [Fact]
    public void CompleteDocumentImports_RemoveStaleEmbeddedFileRegistrations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfString key = new("ghost.txt"u8, PdfStringForm.Literal);
        PdfDictionary names = new([
            new(Name("EmbeddedFiles"), new PdfDictionary([
                new(Name("Names"), new PdfArray([
                    key,
                    new PdfIndirectReference(
                        pages[0].ObjectNumber, pages[0].Generation + 1)
                ]))
            ]))
        ]);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Names"), names))));
        source = PdfDocument.Open(update.Build());
        PdfDocument target = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(source)
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);

        Assert.False(mergedCatalog.ContainsKey(Name("Names")));
    }

    [Fact]
    public void CompleteDocumentImports_RemoveStaleNamedDestinations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfString key = new("ghost"u8, PdfStringForm.Literal);
        PdfDictionary names = new([
            new(Name("Dests"), new PdfDictionary([
                new(Name("Names"), new PdfArray([
                    key,
                    new PdfIndirectReference(
                        pages[0].ObjectNumber, pages[0].Generation + 1)
                ]))
            ]))
        ]);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Names"), names))));
        source = PdfDocument.Open(update.Build());
        PdfDocument target = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(source)
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);

        Assert.False(mergedCatalog.ContainsKey(Name("Names")));
    }

    [Fact]
    public void CompleteDocumentImports_RemoveStaleLegacyNamedDestinations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfDictionary destinations = new([
            new(Name("ghost"), new PdfIndirectReference(
                pages[0].ObjectNumber, pages[0].Generation + 1))
        ]);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Dests"), destinations))));
        source = PdfDocument.Open(update.Build());
        PdfDocument target = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(source)
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);

        Assert.False(mergedCatalog.ContainsKey(Name("Dests")));
    }

    [Fact]
    public void CompleteDocumentImports_RejectMalformedNamedDestinations()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(original, catalogReference);
        PdfIndirectReference page = FlatPages(original).References[0];
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());

        PdfDocument invalidLegacy = WithCatalogEntry("Dests", new PdfDictionary([
            new(Name("bad"), new PdfArray([
                page, Name("FitR"), new PdfInteger(0)
            ]))
        ]));
        InvalidOperationException legacyError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(invalidLegacy)
                .Build());
        Assert.Contains("legacy named destination /FitR has an invalid operand count",
            legacyError.Message, StringComparison.Ordinal);

        PdfDictionary destinationTree = new([
            new(Name("Names"), new PdfArray([
                new PdfString("bad"u8, PdfStringForm.Literal),
                new PdfArray([page, Name("Somewhere")])
            ]))
        ]);
        PdfDocument invalidTree = WithCatalogEntry("Names", new PdfDictionary([
            new(Name("Dests"), destinationTree)
        ]));
        InvalidOperationException treeError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(invalidTree)
                .Build());
        Assert.Contains("fit mode /Somewhere is not defined", treeError.Message,
            StringComparison.Ordinal);
        return;

        PdfDocument WithCatalogEntry(string key, PdfObject value)
        {
            PdfName name = Name(key);
            var update = new PdfIncrementalUpdateBuilder(original);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(name))
                .Append(new KeyValuePair<PdfName, PdfObject>(name, value))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void TaggedDocumentMerges_RemoveStaleStructureRootCollections()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("AF"), new PdfArray([
                new PdfIndirectReference(
                    pages[0].ObjectNumber, pages[0].Generation + 1)
            ])))));
        source = PdfDocument.Open(update.Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(BuildTaggedDocument()))
            .AddImportedDocument(source)
            .Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);
        PdfDictionary mergedRoot = ResolveDictionary(
            merged, mergedCatalog[Name("StructTreeRoot")]);

        Assert.False(mergedRoot.ContainsKey(Name("AF")));
    }

    [Fact]
    public void TaggedDocumentMerges_RejectStaleRoleMapValues()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfDictionary staleRoleMap = new([
            new(Name("CustomRole"), new PdfIndirectReference(
                pages[0].ObjectNumber, pages[0].Generation + 1))
        ]);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("RoleMap"), staleRoleMap))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source).Build());

        Assert.Contains("/RoleMap /CustomRole value is not a name or resolves to null",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedDocumentMerges_RejectStaleMarkInfo()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfIndirectReference sourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary sourceCatalog = ResolveDictionary(source, sourceCatalogReference);
        (_, PdfIndirectReference[] sourcePages, _) = FlatPages(source);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(sourceCatalogReference.ObjectNumber,
                new PdfDictionary(sourceCatalog
                    .Where(entry => !entry.Key.Equals(Name("MarkInfo")))
                    .Append(new KeyValuePair<PdfName, PdfObject>(Name("MarkInfo"),
                        new PdfIndirectReference(sourcePages[0].ObjectNumber,
                            sourcePages[0].Generation + 1)))))
            .Build());
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfIndirectReference targetCatalogReference = Assert.IsType<PdfIndirectReference>(
            target.Trailer[Name("Root")]);
        PdfDictionary targetCatalog = ResolveDictionary(target, targetCatalogReference);
        target = PdfDocument.Open(new PdfIncrementalUpdateBuilder(target)
            .ReplaceObject(targetCatalogReference.ObjectNumber,
                new PdfDictionary(targetCatalog.Where(
                    entry => !entry.Key.Equals(Name("MarkInfo")))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(source).Build());

        Assert.Contains("source catalog /MarkInfo value is not a dictionary",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedDocumentMerges_RejectInvalidStructureRootKids()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary sourceCatalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference sourceRootReference = Assert.IsType<PdfIndirectReference>(
            sourceCatalog[Name("StructTreeRoot")]);
        PdfDictionary sourceRoot = ResolveDictionary(source, sourceRootReference);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(sourceRootReference.ObjectNumber, new PdfDictionary(sourceRoot
                .Where(entry => !entry.Key.Equals(Name("K")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("K"),
                    new PdfInteger(7)))))
            .Build());
        PdfDocument target = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary targetCatalog = ResolveDictionary(target, target.Trailer[Name("Root")]);
        PdfIndirectReference targetRootReference = Assert.IsType<PdfIndirectReference>(
            targetCatalog[Name("StructTreeRoot")]);
        PdfDictionary targetRoot = ResolveDictionary(target, targetRootReference);
        target = PdfDocument.Open(new PdfIncrementalUpdateBuilder(target)
            .ReplaceObject(targetRootReference.ObjectNumber, new PdfDictionary(
                targetRoot.Where(entry => !entry.Key.Equals(Name("K")))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(source).Build());

        Assert.Contains("structure-root kids value contains a non-structure-element",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedDocumentMerges_RejectInvalidDocumentElementKids()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(source, catalog[Name("StructTreeRoot")]);
        PdfIndirectReference documentReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(root[Name("K")])[0]);
        PdfDictionary documentElement = ResolveDictionary(source, documentReference);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(documentReference.ObjectNumber, new PdfDictionary(documentElement
                .Where(entry => !entry.Key.Equals(Name("K")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("K"),
                    new PdfDictionary([])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source).Build());

        Assert.Contains("Document-element kids value contains a structure element without a role",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedDocumentMerges_RejectInvalidStructureRootType()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root
                .Where(entry => !entry.Key.Equals(Name("Type")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Type"), Name("Wrong")))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source).Build());

        Assert.Contains("source /StructTreeRoot /Type value is not /StructTreeRoot",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedDocumentMerges_RejectInvalidStructureNamespaces()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        PdfDictionary invalidNamespace = new([
            new(Name("Type"), Name("Namespace")),
            new(Name("NS"), new PdfInteger(7))
        ]);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root
                .Where(entry => !entry.Key.Equals(Name("Namespaces")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Namespaces"),
                    new PdfArray([invalidNamespace])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source).Build());

        Assert.Contains("source structure namespace has no valid /NS string",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedDocumentMerges_RejectStaleRequiredParentTreeValues()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(source, catalog[Name("StructTreeRoot")]);
        PdfObject parentTreeValue = root[Name("ParentTree")];
        PdfDictionary parentTree = DictionaryValue(source, parentTreeValue);
        PdfArray numbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        var malformedNumbers = new List<PdfObject>(numbers);
        malformedNumbers[1] = new PdfIndirectReference(
            pages[0].ObjectNumber, pages[0].Generation + 1);
        PdfDictionary malformedParentTree = new(parentTree
            .Where(entry => !entry.Key.Equals(Name("Nums")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Nums"),
                new PdfArray(malformedNumbers))));
        var update = new PdfIncrementalUpdateBuilder(source);
        if (parentTreeValue is PdfIndirectReference parentTreeReference)
            update.ReplaceObject(parentTreeReference.ObjectNumber, malformedParentTree);
        else
        {
            PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
                catalog[Name("StructTreeRoot")]);
            update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root
                .Where(entry => !entry.Key.Equals(Name("ParentTree")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("ParentTree"), malformedParentTree))));
        }
        source = PdfDocument.Open(update.Build());

        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source)
                .Build());

        Assert.Contains("missing from the source ParentTree", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TaggedDocumentMerges_RejectStaleParentTreeArrayEntries()
    {
        PdfDocument source = PdfDocument.Open(BuildTaggedDocument());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        PdfDictionary parentTree = DictionaryValue(source, root[Name("ParentTree")]);
        PdfArray numbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);
        PdfObject mappingValue = numbers[1] is PdfIndirectReference mappingReference
            ? source.Resolve(mappingReference) : numbers[1];
        PdfArray mapping = Assert.IsType<PdfArray>(mappingValue);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfArray staleMapping = new([
            new PdfIndirectReference(
                pages[0].ObjectNumber, pages[0].Generation + 1),
            .. mapping.Skip(1)
        ]);
        var rewrittenNumbers = new List<PdfObject>(numbers) { [1] = staleMapping };
        PdfDictionary staleParentTree = new(parentTree
            .Where(entry => !entry.Key.Equals(Name("Nums")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Nums"),
                new PdfArray(rewrittenNumbers))));
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(root
                .Where(entry => !entry.Key.Equals(Name("ParentTree")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("ParentTree"), staleParentTree))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(BuildTaggedDocument()))
                .AddImportedDocument(source).Build());

        Assert.Contains("array entry that is neither an explicit null nor a structure element",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedFormImports_RemoveStaleCalculationOrderGenerations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .AddTextField(0, "customer.name", 20, 20, 120, 20, "Steve")
            .AddCheckBox(1, "customer.approved", 20, 20, 20, 20, isChecked: true)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary form = DictionaryValue(source, catalog[Name("AcroForm")]);
        (_, _, PdfDictionary[] pages) = FlatPages(source);
        PdfIndirectReference widgetReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(pages[0][Name("Annots")])));
        PdfDictionary formWithStaleOrder = new(form.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("CO"), new PdfArray([
                new PdfIndirectReference(
                    widgetReference.ObjectNumber, widgetReference.Generation + 1)
            ]))));
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
            .Where(entry => !entry.Key.Equals(Name("AcroForm")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("AcroForm"), formWithStaleOrder))));
        source = PdfDocument.Open(update.Build());

        PdfDocument selected = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().Build()))
            .AddImportedPage(source, 0)
            .Build());
        PdfDictionary selectedCatalog = ResolveDictionary(
            selected, selected.Trailer[Name("Root")]);
        PdfDictionary selectedForm = DictionaryValue(
            selected, selectedCatalog[Name("AcroForm")]);

        Assert.False(selectedForm.ContainsKey(Name("CO")));
    }

    [Fact]
    public void CompleteFormMerges_RemoveStaleCalculationOrderGenerations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "source", 20, 20, 120, 20, "Source")
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary form = DictionaryValue(source, catalog[Name("AcroForm")]);
        (_, _, PdfDictionary[] pages) = FlatPages(source);
        PdfIndirectReference widgetReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(pages[0][Name("Annots")])));
        PdfDictionary staleForm = new(form.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("CO"), new PdfArray([
                new PdfIndirectReference(
                    widgetReference.ObjectNumber, widgetReference.Generation + 1)
            ]))));
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
            .Where(entry => !entry.Key.Equals(Name("AcroForm")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("AcroForm"), staleForm))));
        source = PdfDocument.Open(update.Build());
        PdfDocument[] targets =
        [
            PdfDocument.Open(new PdfDocumentBuilder().Build()),
            PdfDocument.Open(new PdfDocumentBuilder()
                .AddBlankPage()
                .AddTextField(0, "target", 20, 20, 120, 20, "Target")
                .Build())
        ];

        foreach (PdfDocument target in targets)
        {
            PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
                .AddImportedDocument(source)
                .Build());
            PdfDictionary mergedCatalog = ResolveDictionary(
                merged, merged.Trailer[Name("Root")]);
            PdfDictionary mergedForm = DictionaryValue(
                merged, mergedCatalog[Name("AcroForm")]);

            Assert.False(mergedForm.ContainsKey(Name("CO")));
        }
    }

    [Fact]
    public void SelectedFormImports_PruneHierarchiesCalculationOrderAndOmittedFields()
    {
        PdfDocument hierarchical = PdfDocument.Open(
            BuildHierarchicalAcroFormDocument("customer", 1, 1));
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());
        PdfDocument selectedHierarchy = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(hierarchical, 0).Build());
        PdfDictionary hierarchyCatalog = ResolveDictionary(
            selectedHierarchy, selectedHierarchy.Trailer[Name("Root")]);
        PdfDictionary hierarchyForm = DictionaryValue(
            selectedHierarchy, hierarchyCatalog[Name("AcroForm")]);
        PdfIndirectReference parentReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(hierarchyForm[Name("Fields")] )));
        PdfDictionary parent = ResolveDictionary(selectedHierarchy, parentReference);
        PdfIndirectReference widgetReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(parent[Name("Kids")] )));
        PdfIndirectReference calculationReference = Assert.IsType<PdfIndirectReference>(
            Assert.Single(Assert.IsType<PdfArray>(hierarchyForm[Name("CO")] )));
        Assert.Equal(widgetReference.ObjectNumber, calculationReference.ObjectNumber);
        Assert.Equal(parentReference.ObjectNumber, Assert.IsType<PdfIndirectReference>(
            ResolveDictionary(selectedHierarchy, widgetReference)[Name("Parent")]).ObjectNumber);

        PdfDictionary sourceCatalog = ResolveDictionary(
            hierarchical, hierarchical.Trailer[Name("Root")]);
        PdfDictionary sourceForm = DictionaryValue(
            hierarchical, sourceCatalog[Name("AcroForm")]);
        var procSetUpdate = new PdfIncrementalUpdateBuilder(hierarchical);
        PdfIndirectReference pdfProcedureSet = procSetUpdate.AddObject(Name("PDF"));
        PdfIndirectReference pdfProcedureSetAlias = procSetUpdate.AddObject(pdfProcedureSet);
        PdfDictionary formWithProcSet = new(sourceForm
            .Where(entry => !entry.Key.Equals(Name("DR")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("DR"),
                new PdfDictionary([new(Name("ProcSet"),
                    new PdfArray([pdfProcedureSetAlias, Name("Text")]))]))));
        procSetUpdate.ReplaceObject(
            Assert.IsType<PdfIndirectReference>(hierarchical.Trailer[Name("Root")]).ObjectNumber,
            new PdfDictionary(sourceCatalog
                .Where(entry => !entry.Key.Equals(Name("AcroForm")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("AcroForm"), formWithProcSet))));
        PdfDocument selectedProcSet = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty)
                .AddImportedPage(PdfDocument.Open(procSetUpdate.Build()), 0).Build());
        PdfDictionary procSetCatalog = ResolveDictionary(
            selectedProcSet, selectedProcSet.Trailer[Name("Root")]);
        PdfDictionary procSetForm = DictionaryValue(
            selectedProcSet, procSetCatalog[Name("AcroForm")]);
        PdfDictionary defaultResources = DictionaryValue(
            selectedProcSet, procSetForm[Name("DR")]);
        Assert.Equal(["PDF", "Text"], Assert.IsType<PdfArray>(
                defaultResources[Name("ProcSet")])
            .Select(item =>
            {
                PdfObject value = item;
                while (value is PdfIndirectReference reference)
                    value = selectedProcSet.Resolve(reference);
                return Assert.IsType<PdfName>(value).ValueAsLatin1();
            }));

        PdfDocument multiple = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddTextField(0, "first", 10, 10, 100, 20)
            .AddTextField(1, "second", 10, 10, 100, 20)
            .Build());
        PdfDocument selectedSecond = PdfDocument.Open(
            new PdfIncrementalPageEditor(empty).AddImportedPage(multiple, 1).Build());
        PdfDictionary secondCatalog = ResolveDictionary(
            selectedSecond, selectedSecond.Trailer[Name("Root")]);
        PdfDictionary secondForm = DictionaryValue(
            selectedSecond, secondCatalog[Name("AcroForm")]);
        PdfDictionary secondField = ResolveDictionary(selectedSecond,
            Assert.Single(Assert.IsType<PdfArray>(secondForm[Name("Fields")] )));
        Assert.Equal("second",
            DecodeUnicode(Assert.IsType<PdfString>(secondField[Name("T")] )));
    }

    [Fact]
    public void SelectedFormImports_CrossEncryptionKeysAndIgnoreOmittedNameCollisions()
    {
        byte[] sourceBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "source-user", OwnerPassword = "source-owner"
            })
            .AddBlankPage().AddBlankPage()
            .AddTextField(0, "target", 10, 10, 100, 20)
            .AddTextField(1, "selected", 10, 10, 100, 20, "secret value")
            .Build();
        byte[] targetBytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "target-user", OwnerPassword = "target-owner"
            })
            .AddBlankPage().AddTextField(0, "target", 10, 10, 100, 20)
            .Build();

        byte[] result = new PdfIncrementalPageEditor(
                PdfDocument.Open(targetBytes, "target-owner"))
            .AddImportedPage(PdfDocument.Open(sourceBytes, "source-user"), 1)
            .Build();
        PdfDocument reopened = PdfDocument.Open(result, "target-user");
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary form = DictionaryValue(reopened, catalog[Name("AcroForm")]);
        PdfArray fields = Assert.IsType<PdfArray>(form[Name("Fields")]);

        Assert.Equal(2, fields.Count);
        Assert.Equal(["selected", "target"], fields.Select(field => DecodeUnicode(
                Assert.IsType<PdfString>(ResolveDictionary(reopened, field)[Name("T")])) )
            .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(-1, result.AsSpan().IndexOf("secret value"u8));
    }

    [Fact]
    public void Build_MergesAcroFormsAndRejectsFieldNameCollisions()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddCheckBox(0, "source", 20, 20, 18, 18).Build());
        PdfDocument authoredTarget = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddCheckBox(0, "target", 20, 20, 18, 18).Build());
        PdfDictionary authoredCatalog = ResolveDictionary(
            authoredTarget, authoredTarget.Trailer[Name("Root")]);
        PdfDictionary authoredForm = DictionaryValue(
            authoredTarget, authoredCatalog[Name("AcroForm")]);
        var targetUpdate = new PdfIncrementalUpdateBuilder(authoredTarget);
        PdfIndirectReference formReference = targetUpdate.AddObject(authoredForm);
        PdfIndirectReference formAlias = targetUpdate.AddObject(formReference);
        PdfIndirectReference formOuterAlias = targetUpdate.AddObject(formAlias);
        PdfIndirectReference authoredCatalogReference = Assert.IsType<PdfIndirectReference>(
            authoredTarget.Trailer[Name("Root")]);
        targetUpdate.ReplaceObject(authoredCatalogReference.ObjectNumber,
            new PdfDictionary(authoredCatalog.Select(entry =>
                entry.Key.Equals(Name("AcroForm"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, formOuterAlias)
                    : entry)));
        byte[] targetWithForm = targetUpdate.Build();
        byte[] emptyTarget = new PdfDocumentBuilder().Build();

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(targetWithForm))
                .AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary form = DictionaryValue(merged, catalog[Name("AcroForm")]);
        Assert.Equal(2, Assert.IsType<PdfArray>(form[Name("Fields")]).Count);
        Assert.Equal(formOuterAlias.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(catalog[Name("AcroForm")]).ObjectNumber);

        PdfDocument secondSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddCheckBox(0, "second", 20, 20, 18, 18).Build());
        PdfDocument combinedSources = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(emptyTarget))
                .AddImportedDocument(source)
                .AddImportedDocument(secondSource)
                .Build());
        PdfDictionary combinedCatalog = ResolveDictionary(
            combinedSources, combinedSources.Trailer[Name("Root")]);
        PdfDictionary combinedForm = DictionaryValue(
            combinedSources, combinedCatalog[Name("AcroForm")]);
        Assert.Equal(2, Assert.IsType<PdfArray>(combinedForm[Name("Fields")]).Count);

        PdfDocument ordinaryRemainder = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(emptyTarget))
                .AddImportedDocument(source).RemovePage(0).Build());
        PdfDictionary remainderCatalog = ResolveDictionary(
            ordinaryRemainder, ordinaryRemainder.Trailer[Name("Root")]);
        Assert.False(remainderCatalog.ContainsKey(Name("AcroForm")));

        PdfDocument collision = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddCheckBox(0, "target", 20, 20, 18, 18).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(targetWithForm))
                .AddImportedDocument(collision).Build());
    }

    [Fact]
    public void Build_PreservesDistinctAcroFormExtensionsButRejectsXfaMerges()
    {
        PdfDocument target = AddFormExtension(
            PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage()
                .AddCheckBox(0, "target", 10, 10, 18, 18).Build()),
            "TargetExt", new PdfString("target"u8, PdfStringForm.Literal));
        PdfDocument source = AddFormExtension(
            PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage()
                .AddCheckBox(0, "source", 10, 10, 18, 18).Build()),
            "SourceExt", new PdfString("source"u8, PdfStringForm.Literal));

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build());
        PdfDictionary mergedCatalog = ResolveDictionary(
            merged, merged.Trailer[Name("Root")]);
        PdfDictionary mergedForm = DictionaryValue(
            merged, mergedCatalog[Name("AcroForm")]);

        Assert.Equal("target", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(mergedForm[Name("TargetExt")]).Bytes.Span));
        Assert.Equal("source", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(mergedForm[Name("SourceExt")]).Bytes.Span));

        PdfDocument xfa = AddNeedsRendering(
            AddFormExtension(source, "XFA", new PdfArray([])));
        PdfDocument xfaTransplant = PdfDocument.Open(new PdfIncrementalPageEditor(
                PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build()))
            .AddImportedDocument(xfa).Build());
        PdfDictionary xfaCatalog = ResolveDictionary(
            xfaTransplant, xfaTransplant.Trailer[Name("Root")]);
        PdfDictionary transplantedForm = DictionaryValue(
            xfaTransplant, xfaCatalog[Name("AcroForm")]);
        Assert.True(transplantedForm.ContainsKey(Name("XFA")));
        Assert.True(Assert.IsType<PdfBoolean>(
            xfaCatalog[Name("NeedsRendering")]).Value);
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(target).AddImportedDocument(xfa).Build());

        (_, PdfIndirectReference[] xfaPages, _) = FlatPages(source);
        PdfDocument staleXfa = AddFormExtension(source, "XFA",
            new PdfIndirectReference(
                xfaPages[0].ObjectNumber, xfaPages[0].Generation + 1));
        InvalidOperationException staleError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().AddBlankPage().Build()))
                .AddImportedDocument(staleXfa).Build());
        Assert.Contains("/XFA value is not a stream or packet array",
            staleError.Message, StringComparison.Ordinal);

        PdfDocument staleExtension = AddFormExtension(source, "StaleExt",
            new PdfIndirectReference(
                xfaPages[0].ObjectNumber, xfaPages[0].Generation + 1));
        InvalidOperationException extensionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(staleExtension).Build());
        Assert.Contains("source /AcroForm /StaleExt extension resolves to null",
            extensionError.Message, StringComparison.Ordinal);

        PdfIndirectReference xfaCatalogReference = Assert.IsType<PdfIndirectReference>(
            xfa.Trailer[Name("Root")]);
        PdfDictionary originalXfaCatalog = ResolveDictionary(xfa, xfaCatalogReference);
        PdfDocument staleNeedsRendering = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(xfa)
                .ReplaceObject(xfaCatalogReference.ObjectNumber,
                    new PdfDictionary(originalXfaCatalog
                        .Where(entry => !entry.Key.Equals(Name("NeedsRendering")))
                        .Append(new KeyValuePair<PdfName, PdfObject>(
                            Name("NeedsRendering"), new PdfIndirectReference(
                                xfaPages[0].ObjectNumber,
                                xfaPages[0].Generation + 1)))))
                .Build());
        InvalidOperationException renderingError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().AddBlankPage().Build()))
                .AddImportedDocument(staleNeedsRendering).Build());
        Assert.Contains("/NeedsRendering value is not boolean or resolves to null",
            renderingError.Message, StringComparison.Ordinal);

        static PdfDocument AddNeedsRendering(PdfDocument document)
        {
            PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
                document.Trailer[Name("Root")]);
            PdfDictionary catalog = ResolveDictionary(document, catalogReference);
            var update = new PdfIncrementalUpdateBuilder(document);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
                new KeyValuePair<PdfName, PdfObject>(
                    Name("NeedsRendering"), new PdfBoolean(true)))));
            return PdfDocument.Open(update.Build());
        }

        static PdfDocument AddFormExtension(
            PdfDocument document, string key, PdfObject value)
        {
            PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
                document.Trailer[Name("Root")]);
            PdfDictionary catalog = ResolveDictionary(document, catalogReference);
            PdfDictionary form = DictionaryValue(document, catalog[Name("AcroForm")]);
            PdfDictionary extended = new(form
                .Where(entry => !entry.Key.Equals(Name(key)))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name(key), value)));
            var update = new PdfIncrementalUpdateBuilder(document);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("AcroForm")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("AcroForm"), extended))));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void Build_RenamesMergedAcroFormDefaultResources()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddTextField(0, "source", 20, 20, 120, 24, "Source").Build());
        byte[] target = new PdfDocumentBuilder()
            .AddBlankPage().AddTextField(0, "target", 20, 20, 120, 24, "Target").Build();

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary form = DictionaryValue(merged, catalog[Name("AcroForm")]);
        PdfDictionary resources = DictionaryValue(merged, form[Name("DR")]);
        PdfDictionary fonts = DictionaryValue(merged, resources[Name("Font")]);
        PdfArray fields = Assert.IsType<PdfArray>(form[Name("Fields")]);
        PdfDictionary importedField = ResolveDictionary(merged, fields[1]);
        string appearance = Encoding.Latin1.GetString(
            Assert.IsType<PdfString>(importedField[Name("DA")]).Bytes.Span);

        Assert.Equal(2, fonts.Count);
        Assert.Contains("/KPF", appearance, StringComparison.Ordinal);
        Assert.DoesNotContain("/Helv ", appearance, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteFormMerges_RejectStaleDefaultResources()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "source", 20, 20, 120, 20, "Source")
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary form = DictionaryValue(source, catalog[Name("AcroForm")]);
        PdfDictionary resources = DictionaryValue(source, form[Name("DR")]);
        PdfDictionary fonts = DictionaryValue(source, resources[Name("Font")]);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfName fontName = Assert.Single(fonts.Keys);
        PdfDictionary staleFonts = new(fonts
            .Where(entry => !entry.Key.Equals(fontName))
            .Append(new KeyValuePair<PdfName, PdfObject>(fontName,
                new PdfIndirectReference(
                    pages[0].ObjectNumber, pages[0].Generation + 1))));
        PdfDictionary staleResources = new(resources
            .Where(entry => !entry.Key.Equals(Name("Font")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Font"), staleFonts)));
        PdfDictionary staleForm = new(form
            .Where(entry => !entry.Key.Equals(Name("DR")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("DR"), staleResources)));
        var update = new PdfIncrementalUpdateBuilder(source);
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
            .Where(entry => !entry.Key.Equals(Name("AcroForm")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("AcroForm"), staleForm))));
        source = PdfDocument.Open(update.Build());
        PdfDocument[] targets =
        [
            PdfDocument.Open(new PdfDocumentBuilder().Build()),
            PdfDocument.Open(new PdfDocumentBuilder()
                .AddBlankPage()
                .AddTextField(0, "target", 20, 20, 120, 20, "Target")
                .Build())
        ];

        foreach (PdfDocument target in targets)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(target)
                    .AddImportedDocument(source)
                    .Build());

            Assert.Contains("resource resolves to null", error.Message,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Build_RenamesEscapedAcroFormDefaultResourceNames()
    {
        PdfDocument source = PdfDocument.Open(BuildEscapedAcroFormResourceDocument());
        PdfDictionary sourceCatalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfDictionary sourceForm = Assert.IsType<PdfDictionary>(
            sourceCatalog[Name("AcroForm")]);
        PdfIndirectReference sourceFieldReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(sourceForm[Name("Fields")])[0]);
        PdfDictionary sourceField = ResolveDictionary(source, sourceFieldReference);
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference typeReference = sourceUpdate.AddObject(Name("Tx"));
        PdfIndirectReference appearanceReference = sourceUpdate.AddObject(
            sourceField[Name("DA")]);
        var sourceFieldEntries = sourceField.ToDictionary(
            entry => entry.Key, entry => entry.Value);
        sourceFieldEntries[Name("FT")] = typeReference;
        sourceFieldEntries[Name("DA")] = appearanceReference;
        sourceUpdate.ReplaceObject(sourceFieldReference.ObjectNumber,
            new PdfDictionary(sourceFieldEntries));
        source = PdfDocument.Open(sourceUpdate.Build());
        byte[] target = new PdfDocumentBuilder()
            .AddBlankPage().AddTextField(0, "target", 10, 10, 100, 20).Build();

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(source)
                .Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary form = DictionaryValue(merged, catalog[Name("AcroForm")]);
        PdfDictionary importedField = ResolveDictionary(merged,
            Assert.IsType<PdfArray>(form[Name("Fields")])[1]);
        string appearance = Encoding.Latin1.GetString(
            Assert.IsType<PdfString>(importedField[Name("DA")]).Bytes.Span);

        Assert.Contains("/KPF", appearance, StringComparison.Ordinal);
        Assert.DoesNotContain("/F#20One", appearance, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_MergesAcroFormFlagsCalculationOrderAndHierarchicalFieldNames()
    {
        PdfDocument target = WithIndirectFormScalars(PdfDocument.Open(
            BuildHierarchicalAcroFormDocument("target", 1, 1)), 1, 1, false);
        PdfDocument source = WithIndirectFormScalars(PdfDocument.Open(
            BuildHierarchicalAcroFormDocument("source", 2, 2)), 2, 2, true);

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(source)
                .Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary form = DictionaryValue(merged, catalog[Name("AcroForm")]);
        PdfArray fields = Assert.IsType<PdfArray>(form[Name("Fields")]);
        PdfArray calculationOrder = Assert.IsType<PdfArray>(form[Name("CO")]);
        PdfDictionary targetParent = ResolveDictionary(merged, fields[0]);
        PdfDictionary sourceParent = ResolveDictionary(merged, fields[1]);
        PdfIndirectReference targetChildReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(targetParent[Name("Kids")])[0]);
        PdfIndirectReference sourceChildReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(sourceParent[Name("Kids")])[0]);
        PdfDictionary sourceChild = ResolveDictionary(merged, sourceChildReference);

        Assert.Equal(2, fields.Count);
        Assert.Equal(3, Assert.IsType<PdfInteger>(form[Name("SigFlags")]).Value);
        Assert.True(Assert.IsType<PdfBoolean>(form[Name("NeedAppearances")]).Value);
        Assert.Equal([targetChildReference.ObjectNumber, sourceChildReference.ObjectNumber],
            calculationOrder.Select(value =>
                Assert.IsType<PdfIndirectReference>(value).ObjectNumber));
        Assert.Equal(2, Assert.IsType<PdfInteger>(sourceChild[Name("Q")]).Value);

        static PdfDocument WithIndirectFormScalars(
            PdfDocument document, long signatureFlags, long quadding, bool needAppearances)
        {
            PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
                document.Trailer[Name("Root")]);
            PdfDictionary catalog = ResolveDictionary(document, catalogReference);
            PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfIndirectReference flagsReference = update.AddObject(new PdfInteger(signatureFlags));
            PdfIndirectReference quaddingReference = update.AddObject(new PdfInteger(quadding));
            PdfIndirectReference appearancesReference = update.AddObject(
                new PdfBoolean(needAppearances));
            var formEntries = form.ToDictionary(entry => entry.Key, entry => entry.Value);
            formEntries[Name("SigFlags")] = flagsReference;
            formEntries[Name("Q")] = quaddingReference;
            formEntries[Name("NeedAppearances")] = appearancesReference;
            var catalogEntries = catalog.ToDictionary(entry => entry.Key, entry => entry.Value);
            catalogEntries[Name("AcroForm")] = new PdfDictionary(formEntries);
            update.ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalogEntries));
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void Build_MergesNamedDestinationsAndKeepsImportedNamedLinksValid()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300).AddBlankPage(300, 400)
            .AddNamedDestination("source-target", 1)
            .AddNamedDestinationLink(0, 10, 10, 50, 20, "source-target")
            .Build());
        PdfDocument authoredTarget = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(100, 100)
            .AddNamedDestination("target-start", 0)
            .AddAttachment("target.txt", "target"u8.ToArray(), "text/plain")
            .Build());
        PdfDictionary authoredCatalog = ResolveDictionary(
            authoredTarget, authoredTarget.Trailer[Name("Root")]);
        PdfDictionary authoredNames = DictionaryValue(
            authoredTarget, authoredCatalog[Name("Names")]);
        var targetUpdate = new PdfIncrementalUpdateBuilder(authoredTarget);
        PdfIndirectReference namesReference = targetUpdate.AddObject(authoredNames);
        PdfIndirectReference namesAlias = targetUpdate.AddObject(namesReference);
        PdfIndirectReference namesOuterAlias = targetUpdate.AddObject(namesAlias);
        PdfIndirectReference authoredCatalogReference = Assert.IsType<PdfIndirectReference>(
            authoredTarget.Trailer[Name("Root")]);
        targetUpdate.ReplaceObject(authoredCatalogReference.ObjectNumber,
            new PdfDictionary(authoredCatalog.Select(entry =>
                entry.Key.Equals(Name("Names"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, namesOuterAlias)
                    : entry)));
        byte[] target = targetUpdate.Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(source)
                .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(reopened);
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary names = ResolveDictionary(reopened, catalog[Name("Names")]);
        PdfDictionary destinations = Assert.IsType<PdfDictionary>(names[Name("Dests")]);
        PdfArray destinationNames = Assert.IsType<PdfArray>(destinations[Name("Names")]);
        var values = Enumerable.Range(0, destinationNames.Count / 2).ToDictionary(
            index => DecodeUnicode(Assert.IsType<PdfString>(destinationNames[index * 2])),
            index => Assert.IsType<PdfArray>(destinationNames[index * 2 + 1]),
            StringComparer.Ordinal);
        PdfDictionary importedLink = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[1][Name("Annots")])[0]);

        Assert.True(names.ContainsKey(Name("EmbeddedFiles")));
        Assert.Equal(namesOuterAlias.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(catalog[Name("Names")]).ObjectNumber);
        Assert.Equal(pageReferences[0].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(values["target-start"][0]).ObjectNumber);
        Assert.Equal(pageReferences[2].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(values["source-target"][0]).ObjectNumber);
        Assert.Equal("source-target", DecodeUnicode(
            Assert.IsType<PdfString>(importedLink[Name("Dest")])));
    }

    [Fact]
    public void Build_RenamesNamedDestinationCollisionsAndRejectsExternalSplitTargets()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddNamedDestination("shared", 1)
            .AddNamedDestinationLink(0, 10, 10, 50, 20, "shared")
            .Build());
        (_, _, PdfDictionary[] sourcePages) = FlatPages(source);
        PdfIndirectReference sourceLinkReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(sourcePages[0][Name("Annots")])[0]);
        PdfDictionary sourceLink = ResolveDictionary(source, sourceLinkReference);
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference destinationReference = sourceUpdate.AddObject(
            sourceLink[Name("Dest")]);
        sourceUpdate.ReplaceObject(sourceLinkReference.ObjectNumber, new PdfDictionary(
            sourceLink.Where(entry => !entry.Key.Equals(Name("Dest"))).Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Dest"), destinationReference))));
        source = PdfDocument.Open(sourceUpdate.Build());
        byte[] collidingTarget = new PdfDocumentBuilder()
            .AddBlankPage().AddNamedDestination("shared", 0).Build();
        byte[] emptyTarget = new PdfDocumentBuilder().Build();

        PdfDocument renamed = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(collidingTarget))
                .AddImportedDocument(source).Build());
        (_, _, PdfDictionary[] renamedPages) = FlatPages(renamed);
        PdfDictionary renamedCatalog = ResolveDictionary(renamed, renamed.Trailer[Name("Root")]);
        PdfDictionary renamedNames = DictionaryValue(renamed, renamedCatalog[Name("Names")]);
        PdfDictionary renamedDestinations = DictionaryValue(renamed, renamedNames[Name("Dests")]);
        PdfArray renamedEntries = Assert.IsType<PdfArray>(renamedDestinations[Name("Names")]);
        PdfDictionary renamedLink = ResolveDictionary(renamed,
            Assert.IsType<PdfArray>(renamedPages[1][Name("Annots")])[0]);
        Assert.Equal(["shared", "shared (2)"], Enumerable.Range(0, renamedEntries.Count / 2)
            .Select(index => DecodeUnicode(Assert.IsType<PdfString>(renamedEntries[index * 2]))));
        Assert.Equal("shared (2)", DecodeUnicode(
            Assert.IsType<PdfString>(renamedLink[Name("Dest")])));

        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(emptyTarget))
                .AddImportedPage(source, 0).Build());

        PdfDocument retainedDestination = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddNamedDestination("retained", 0)
            .AddNamedDestinationLink(0, 10, 10, 50, 20, "retained")
            .Build());
        PdfDocument split = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(emptyTarget))
                .AddImportedPage(retainedDestination, 0)
                .Build());
        PdfDictionary splitCatalog = ResolveDictionary(split, split.Trailer[Name("Root")]);
        PdfDictionary splitNames = DictionaryValue(split, splitCatalog[Name("Names")]);
        PdfDictionary splitDestinations = DictionaryValue(split, splitNames[Name("Dests")]);
        Assert.Equal(2, Assert.IsType<PdfArray>(splitDestinations[Name("Names")]).Count);
    }

    [Fact]
    public void SelectedPageImport_IgnoresUnrelatedOmittedNamedDestinationsButRejectsUsedOnes()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage().AddBlankPage()
            .AddNamedDestination("retained", 0)
            .AddNamedDestination("omitted", 2)
            .AddNamedDestinationLink(0, 10, 10, 50, 20, "retained")
            .Build());
        PdfDocument target = PdfDocument.Open(new PdfDocumentBuilder().Build());

        PdfDocument imported = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedPages(source, [1, 0])
            .Build());
        PdfDictionary catalog = ResolveDictionary(imported, imported.Trailer[Name("Root")]);
        PdfDictionary names = DictionaryValue(imported, catalog[Name("Names")]);
        PdfDictionary destinations = DictionaryValue(imported, names[Name("Dests")]);
        PdfArray entries = Assert.IsType<PdfArray>(destinations[Name("Names")]);

        Assert.Equal(2, entries.Count);
        Assert.Equal("retained", DecodeUnicode(Assert.IsType<PdfString>(entries[0])));

        PdfDocument unsafeSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage().AddBlankPage()
            .AddNamedDestination("omitted", 2)
            .AddNamedDestinationLink(0, 10, 10, 50, 20, "omitted")
            .Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedPages(unsafeSource, [0, 1])
                .Build());
    }

    [Fact]
    public void Build_PreservesLegacyNamedDestinationsDuringCompleteImports()
    {
        PdfDocument source = PdfDocument.Open(BuildLegacyDestinationDocument());
        (_, _, PdfDictionary[] sourcePages) = FlatPages(source);
        PdfIndirectReference sourceLinkReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(sourcePages[0][Name("Annots")])[0]);
        PdfDictionary sourceLink = ResolveDictionary(source, sourceLinkReference);
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference destinationReference = sourceUpdate.AddObject(
            sourceLink[Name("Dest")]);
        sourceUpdate.ReplaceObject(sourceLinkReference.ObjectNumber, new PdfDictionary(
            sourceLink.Where(entry => !entry.Key.Equals(Name("Dest"))).Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Dest"), destinationReference))));
        source = PdfDocument.Open(sourceUpdate.Build());
        byte[] target = new PdfDocumentBuilder().AddBlankPage().Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(source)
                .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(reopened);
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary destinations = DictionaryValue(reopened, catalog[Name("Dests")]);
        PdfArray chapter = Assert.IsType<PdfArray>(destinations[Name("chapter")]);
        PdfDictionary link = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[1][Name("Annots")])[0]);

        Assert.Equal(pageReferences[2].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(chapter[0]).ObjectNumber);
        Assert.Equal("chapter", Assert.IsType<PdfName>(
            reopened.Resolve(Assert.IsType<PdfIndirectReference>(
                link[Name("Dest")]))).ValueAsLatin1());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedPage(source, 0)
                .Build());

        PdfDocument collisionTarget = PdfDocument.Open(BuildLegacyDestinationDocument());
        PdfDocument collisionMerged = PdfDocument.Open(
            new PdfIncrementalPageEditor(collisionTarget)
                .AddImportedDocument(source)
                .Build());
        (_, _, PdfDictionary[] collisionPages) = FlatPages(collisionMerged);
        PdfDictionary collisionCatalog = ResolveDictionary(
            collisionMerged, collisionMerged.Trailer[Name("Root")]);
        PdfDictionary collisionDestinations = DictionaryValue(
            collisionMerged, collisionCatalog[Name("Dests")]);
        PdfDictionary collisionLink = ResolveDictionary(collisionMerged,
            Assert.IsType<PdfArray>(collisionPages[2][Name("Annots")])[0]);
        Assert.True(collisionDestinations.ContainsKey(Name("chapter")));
        Assert.True(collisionDestinations.ContainsKey(Name("chapter~2")));
        Assert.Equal("chapter~2",
            Assert.IsType<PdfName>(collisionLink[Name("Dest")]).ValueAsLatin1());
    }

    [Fact]
    public void Build_PreservesBookmarksDuringACompleteDocumentImport()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddBookmark("Second page", 1)
            .Build());
        PdfIndirectReference sourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary sourceCatalog = ResolveDictionary(source, sourceCatalogReference);
        PdfIndirectReference sourceRootReference = Assert.IsType<PdfIndirectReference>(
            sourceCatalog[Name("Outlines")]);
        PdfDictionary sourceRoot = ResolveDictionary(source, sourceRootReference);
        PdfIndirectReference sourceItemReference = Assert.IsType<PdfIndirectReference>(
            sourceRoot[Name("First")]);
        PdfDictionary sourceItem = ResolveDictionary(source, sourceItemReference);
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfObject Indirect(PdfObject value) => sourceUpdate.AddObject(
            sourceUpdate.AddObject(value));
        var sourceRootEntries = sourceRoot.ToDictionary(entry => entry.Key, entry => entry.Value);
        sourceRootEntries[Name("Type")] = Indirect(Name("Outlines"));
        sourceRootEntries[Name("Count")] = Indirect(new PdfInteger(1));
        sourceUpdate.ReplaceObject(sourceRootReference.ObjectNumber,
            new PdfDictionary(sourceRootEntries));
        var sourceItemEntries = sourceItem.ToDictionary(entry => entry.Key, entry => entry.Value);
        sourceItemEntries[Name("Title")] = Indirect(sourceItem[Name("Title")]);
        sourceItemEntries[Name("F")] = Indirect(new PdfInteger(0));
        sourceItemEntries[Name("C")] = new PdfArray([
            Indirect(new PdfInteger(0)), Indirect(new PdfReal(0.5)),
            Indirect(new PdfInteger(1))]);
        sourceUpdate.ReplaceObject(sourceItemReference.ObjectNumber,
            new PdfDictionary(sourceItemEntries));
        var sourceCatalogEntries = sourceCatalog.ToDictionary(
            entry => entry.Key, entry => entry.Value);
        sourceCatalogEntries[Name("PageMode")] = Indirect(Name("UseOutlines"));
        sourceUpdate.ReplaceObject(sourceCatalogReference.ObjectNumber,
            new PdfDictionary(sourceCatalogEntries));
        source = PdfDocument.Open(sourceUpdate.Build());
        byte[] target = new PdfDocumentBuilder().AddBlankPage().Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(source)
                .Build());
        (_, PdfIndirectReference[] pageReferences, _) = FlatPages(reopened);
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary outlines = DictionaryValue(reopened, catalog[Name("Outlines")]);
        PdfDictionary first = ResolveDictionary(reopened, outlines[Name("First")]);
        PdfArray destination = Assert.IsType<PdfArray>(first[Name("Dest")]);

        Assert.Equal("Second page", DecodeUnicode(Assert.IsType<PdfString>(
            ResolveScalar(reopened, first[Name("Title")]))));
        Assert.Equal(pageReferences[2].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(destination[0]).ObjectNumber);
        Assert.Equal("UseOutlines",
            Assert.IsType<PdfName>(ResolveScalar(
                reopened, catalog[Name("PageMode")])).ValueAsLatin1());

        static PdfObject ResolveScalar(PdfDocument document, PdfObject value)
        {
            while (value is PdfIndirectReference reference)
                value = document.Resolve(reference);
            return value;
        }
    }

    [Fact]
    public void CompleteOutlineImports_RejectStalePageMode()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBookmark("Source", 0)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("PageMode")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("PageMode"),
                    new PdfIndirectReference(
                        pages[0].ObjectNumber, pages[0].Generation + 1)))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().AddBlankPage().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("catalog /PageMode value is not a name or resolves to null",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteOutlineImports_RejectMalformedDestinationsAndActions()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBookmark("Source", 0)
            .Build());
        PdfDictionary catalog = ResolveDictionary(original, original.Trailer[Name("Root")]);
        PdfDictionary root = DictionaryValue(original, catalog[Name("Outlines")]);
        PdfIndirectReference itemReference = Assert.IsType<PdfIndirectReference>(
            root[Name("First")]);
        PdfDictionary item = ResolveDictionary(original, itemReference);
        PdfIndirectReference page = FlatPages(original).References[0];
        PdfDocument target = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument invalidDestination = ReplaceItem(new PdfDictionary(item
            .Where(entry => !entry.Key.Equals(Name("Dest")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Dest"),
                new PdfArray([page, Name("FitH")])))));
        InvalidOperationException destinationError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidDestination)
                .Build());
        Assert.Contains("bookmark /Dest value /FitH has an invalid operand count",
            destinationError.Message, StringComparison.Ordinal);

        PdfDocument invalidAction = ReplaceItem(new PdfDictionary(item
            .Where(entry => !entry.Key.Equals(Name("Dest")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("A"),
                new PdfDictionary([new(Name("Type"), Name("Action"))])))));
        InvalidOperationException actionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidAction)
                .Build());
        Assert.Contains("bookmark /A value has no valid /S name",
            actionError.Message, StringComparison.Ordinal);
        return;

        PdfDocument ReplaceItem(PdfDictionary replacement)
        {
            var update = new PdfIncrementalUpdateBuilder(original);
            update.ReplaceObject(itemReference.ObjectNumber, replacement);
            return PdfDocument.Open(update.Build());
        }
    }

    [Fact]
    public void CompleteOutlineImports_RejectStaleChildLists()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBookmark("Source", 0)
            .Build());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfDictionary root = DictionaryValue(source, catalog[Name("Outlines")]);
        PdfIndirectReference itemReference = Assert.IsType<PdfIndirectReference>(
            root[Name("First")]);
        PdfDictionary item = ResolveDictionary(source, itemReference);
        (_, PdfIndirectReference[] pages, _) = FlatPages(source);
        PdfIndirectReference stale = new(
            pages[0].ObjectNumber, pages[0].Generation + 1);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(itemReference.ObjectNumber, new PdfDictionary(item
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("First"), stale))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Last"), stale))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().AddBlankPage().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("source bookmark item is not a dictionary",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteOutlineImports_RejectNonreciprocalListLinks()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBookmark("First", 0)
            .AddBookmark("Second", 0)
            .Build());
        PdfDictionary catalog = ResolveDictionary(original, original.Trailer[Name("Root")]);
        PdfDictionary root = ResolveDictionary(original, catalog[Name("Outlines")]);
        PdfIndirectReference firstReference = Assert.IsType<PdfIndirectReference>(
            root[Name("First")]);
        PdfIndirectReference lastReference = Assert.IsType<PdfIndirectReference>(
            root[Name("Last")]);
        PdfDictionary first = ResolveDictionary(original, firstReference);
        PdfDictionary last = ResolveDictionary(original, lastReference);
        PdfIndirectReference pageReference = FlatPages(original).References[0];
        PdfDocument target = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        InvalidOperationException parentError = Assert.Throws<InvalidOperationException>(() =>
            Import(Replace(firstReference, ReplaceEntry(first, "Parent", pageReference))));
        InvalidOperationException previousError = Assert.Throws<InvalidOperationException>(() =>
            Import(Replace(lastReference, ReplaceEntry(last, "Prev", lastReference))));
        InvalidOperationException endpointError = Assert.Throws<InvalidOperationException>(() =>
            Import(Replace(lastReference, ReplaceEntry(last, "Next", firstReference))));

        Assert.Contains("nonreciprocal /Parent", parentError.Message,
            StringComparison.Ordinal);
        Assert.Contains("nonreciprocal /Prev", previousError.Message,
            StringComparison.Ordinal);
        Assert.Contains("unexpected /Next", endpointError.Message,
            StringComparison.Ordinal);

        void Import(PdfDocument source) =>
            new PdfIncrementalPageEditor(target).AddImportedDocument(source).Build();

        PdfDocument Replace(PdfIndirectReference reference, PdfDictionary dictionary) =>
            PdfDocument.Open(new PdfIncrementalUpdateBuilder(original)
                .ReplaceObject(reference.ObjectNumber, dictionary).Build());

        static PdfDictionary ReplaceEntry(
            PdfDictionary dictionary, string name, PdfObject value) =>
            new(dictionary.Where(entry => !entry.Key.Equals(Name(name))).Append(
                new KeyValuePair<PdfName, PdfObject>(Name(name), value)));
    }

    [Fact]
    public void PartialPageImport_PreservesOnlyTheDestinationBookmarkTree()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddBookmark("Source bookmark", 1)
            .Build());
        byte[] target = new PdfDocumentBuilder()
            .AddBlankPage().AddBookmark("Target bookmark", 0).Build();

        byte[] importedBytes = new PdfIncrementalPageEditor(PdfDocument.Open(target))
            .AddImportedPage(source, 1)
            .Build();
        PdfDocument imported = PdfDocument.Open(importedBytes);
        PdfDictionary catalog = ResolveDictionary(imported, imported.Trailer[Name("Root")]);
        PdfDictionary outlines = DictionaryValue(imported, catalog[Name("Outlines")]);
        PdfDictionary first = ResolveDictionary(imported, outlines[Name("First")]);

        Assert.Equal("Target bookmark",
            DecodeUnicode(Assert.IsType<PdfString>(first[Name("Title")])));
        Assert.Equal(-1, importedBytes.AsSpan().IndexOf("Source bookmark"u8));
        Assert.Equal(1, Assert.IsType<PdfInteger>(outlines[Name("Count")]).Value);
    }

    [Fact]
    public void Build_MergesBookmarkTreesFromTheTargetAndMultipleSources()
    {
        PdfDocument authoredTarget = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBookmark("Target", 0).Build());
        PdfDictionary authoredCatalog = ResolveDictionary(
            authoredTarget, authoredTarget.Trailer[Name("Root")]);
        PdfIndirectReference authoredRootReference = Assert.IsType<PdfIndirectReference>(
            authoredCatalog[Name("Outlines")]);
        PdfDictionary authoredRoot = ResolveDictionary(authoredTarget, authoredRootReference);
        PdfIndirectReference authoredItemReference = Assert.IsType<PdfIndirectReference>(
            authoredRoot[Name("First")]);
        var targetUpdate = new PdfIncrementalUpdateBuilder(authoredTarget);
        PdfIndirectReference itemAlias = targetUpdate.AddObject(authoredItemReference);
        PdfIndirectReference itemOuterAlias = targetUpdate.AddObject(itemAlias);
        targetUpdate.ReplaceObject(authoredRootReference.ObjectNumber,
            new PdfDictionary(authoredRoot.Select(entry =>
                entry.Key.Equals(Name("First")) || entry.Key.Equals(Name("Last"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, itemOuterAlias)
                    : entry)));
        PdfIndirectReference rootAlias = targetUpdate.AddObject(authoredRootReference);
        PdfIndirectReference rootOuterAlias = targetUpdate.AddObject(rootAlias);
        PdfIndirectReference authoredCatalogReference = Assert.IsType<PdfIndirectReference>(
            authoredTarget.Trailer[Name("Root")]);
        targetUpdate.ReplaceObject(authoredCatalogReference.ObjectNumber,
            new PdfDictionary(authoredCatalog.Select(entry =>
                entry.Key.Equals(Name("Outlines"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key, rootOuterAlias)
                    : entry)));
        byte[] target = targetUpdate.Build();
        PdfDocument firstSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddBookmark("First source", 0)
            .AddBookmark("First source child", 1, 1)
            .Build());
        PdfDictionary firstSourceCatalog = ResolveDictionary(
            firstSource, firstSource.Trailer[Name("Root")]);
        PdfIndirectReference firstSourceRootReference = Assert.IsType<PdfIndirectReference>(
            firstSourceCatalog[Name("Outlines")]);
        PdfDictionary firstSourceRoot = ResolveDictionary(
            firstSource, firstSourceRootReference);
        PdfIndirectReference firstSourceItemReference = Assert.IsType<PdfIndirectReference>(
            firstSourceRoot[Name("First")]);
        var firstSourceUpdate = new PdfIncrementalUpdateBuilder(firstSource);
        PdfIndirectReference firstSourceItemAlias =
            firstSourceUpdate.AddObject(firstSourceItemReference);
        PdfIndirectReference firstSourceItemOuterAlias =
            firstSourceUpdate.AddObject(firstSourceItemAlias);
        firstSourceUpdate.ReplaceObject(firstSourceRootReference.ObjectNumber,
            new PdfDictionary(firstSourceRoot.Select(entry =>
                entry.Key.Equals(Name("First")) || entry.Key.Equals(Name("Last"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                        firstSourceItemOuterAlias)
                    : entry)));
        PdfIndirectReference firstSourceRootAlias =
            firstSourceUpdate.AddObject(firstSourceRootReference);
        PdfIndirectReference firstSourceRootOuterAlias =
            firstSourceUpdate.AddObject(firstSourceRootAlias);
        PdfIndirectReference firstSourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            firstSource.Trailer[Name("Root")]);
        firstSourceUpdate.ReplaceObject(firstSourceCatalogReference.ObjectNumber,
            new PdfDictionary(firstSourceCatalog.Select(entry =>
                entry.Key.Equals(Name("Outlines"))
                    ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                        firstSourceRootOuterAlias)
                    : entry)));
        firstSource = PdfDocument.Open(firstSourceUpdate.Build());
        PdfDocument secondSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBookmark("Second source", 0).Build());

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(firstSource)
                .AddImportedDocument(secondSource)
                .Build());
        (_, PdfIndirectReference[] pages, _) = FlatPages(merged);
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("Outlines")]);
        PdfDictionary root = ResolveDictionary(merged, rootReference);
        var items = new List<(PdfIndirectReference Reference, PdfDictionary Dictionary)>();
        PdfIndirectReference? current = Assert.IsType<PdfIndirectReference>(root[Name("First")]);
        while (current is not null)
        {
            PdfDictionary item = ResolveDictionary(merged, current);
            items.Add((current, item));
            current = item.TryGetValue(Name("Next"), out PdfObject? next)
                ? Assert.IsType<PdfIndirectReference>(next) : null;
        }

        Assert.Equal(4, Assert.IsType<PdfInteger>(root[Name("Count")]).Value);
        Assert.Equal(["Target", "First source", "Second source"], items.Select(item =>
            DecodeUnicode(Assert.IsType<PdfString>(item.Dictionary[Name("Title")]))));
        Assert.Equal(new[] { pages[0].ObjectNumber, pages[1].ObjectNumber, pages[3].ObjectNumber },
            items.Select(item =>
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfArray>(item.Dictionary[Name("Dest")])[0]).ObjectNumber));
        Assert.Equal(rootOuterAlias.ObjectNumber, rootReference.ObjectNumber);
        Assert.Equal(itemOuterAlias.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(root[Name("First")]).ObjectNumber);
        Assert.Equal(itemOuterAlias.ObjectNumber, items[0].Reference.ObjectNumber);
        Assert.All(items, item => Assert.Equal(authoredRootReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(item.Dictionary[Name("Parent")]).ObjectNumber));
        Assert.False(items[0].Dictionary.ContainsKey(Name("Prev")));
        Assert.Equal(items[0].Reference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(items[1].Dictionary[Name("Prev")]).ObjectNumber);
        Assert.Equal(items[1].Reference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(items[2].Dictionary[Name("Prev")]).ObjectNumber);
        Assert.False(items[2].Dictionary.ContainsKey(Name("Next")));
        PdfDictionary child = ResolveDictionary(merged,
            Assert.IsType<PdfIndirectReference>(items[1].Dictionary[Name("First")]));
        Assert.Equal(1, Assert.IsType<PdfInteger>(items[1].Dictionary[Name("Count")]).Value);
        Assert.Equal(items[1].Reference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(child[Name("Parent")]).ObjectNumber);
        Assert.Equal(pages[2].ObjectNumber, Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(child[Name("Dest")])[0]).ObjectNumber);
    }

    [Fact]
    public void Build_IndirectsDirectBookmarkRootWhenMergingTrees()
    {
        PdfDocument authoredTarget = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBookmark("Target", 0).Build());
        PdfDictionary targetCatalog = ResolveDictionary(
            authoredTarget, authoredTarget.Trailer[Name("Root")]);
        PdfDictionary directRoot = ResolveDictionary(
            authoredTarget, targetCatalog[Name("Outlines")]);
        var update = new PdfIncrementalUpdateBuilder(authoredTarget);
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            authoredTarget.Trailer[Name("Root")]);
        update.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(targetCatalog
                .Where(entry => !entry.Key.Equals(Name("Outlines")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Outlines"), directRoot))));
        PdfDocument directTarget = PdfDocument.Open(update.Build());
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBookmark("Source", 0).Build());

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(directTarget)
            .AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("Outlines")]);
        PdfDictionary root = ResolveDictionary(merged, rootReference);
        PdfDictionary first = ResolveDictionary(merged, root[Name("First")]);
        PdfDictionary second = ResolveDictionary(merged, first[Name("Next")]);

        Assert.Equal("Target", DecodeUnicode(Assert.IsType<PdfString>(first[Name("Title")])));
        Assert.Equal("Source", DecodeUnicode(Assert.IsType<PdfString>(second[Name("Title")])));
        Assert.Equal(rootReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(first[Name("Parent")]).ObjectNumber);
        Assert.Equal(rootReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(second[Name("Parent")]).ObjectNumber);
        Assert.Equal(2, Assert.IsType<PdfInteger>(root[Name("Count")]).Value);
    }

    [Fact]
    public void Build_MergesEmbeddedAndAssociatedFilesDuringCompleteImports()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddAttachment("source.txt", "source payload"u8.ToArray(), "text/plain")
            .Build());
        byte[] target = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddNamedDestination("target-page", 0)
            .AddAttachment("target.txt", "target payload"u8.ToArray(), "text/plain")
            .Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(source)
                .Build());
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary names = DictionaryValue(reopened, catalog[Name("Names")]);
        PdfDictionary embeddedFiles = DictionaryValue(reopened, names[Name("EmbeddedFiles")]);
        PdfArray fileNames = Assert.IsType<PdfArray>(embeddedFiles[Name("Names")]);
        PdfArray associatedFiles = Assert.IsType<PdfArray>(catalog[Name("AF")]);
        PdfDictionary importedFile = ResolveDictionary(reopened, fileNames[1]);
        PdfDictionary importedStreams = DictionaryValue(reopened, importedFile[Name("EF")]);
        PdfStream importedPayload = ResolveStream(reopened, importedStreams[Name("UF")]);

        Assert.True(names.ContainsKey(Name("Dests")));
        Assert.Equal(["source.txt", "target.txt"], Enumerable.Range(0, fileNames.Count / 2)
            .Select(index => DecodeUnicode(Assert.IsType<PdfString>(fileNames[index * 2]))));
        Assert.Equal(2, associatedFiles.Count);
        Assert.Equal("source payload"u8.ToArray(), importedPayload.EncodedData.ToArray());
        byte[] partialBytes = new PdfIncrementalPageEditor(PdfDocument.Open(target))
            .AddImportedPage(source, 0)
            .Build();
        PdfDocument partial = PdfDocument.Open(partialBytes);
        PdfDictionary partialCatalog = ResolveDictionary(
            partial, partial.Trailer[Name("Root")]);
        PdfDictionary partialNames = DictionaryValue(
            partial, partialCatalog[Name("Names")]);
        PdfArray partialFiles = Assert.IsType<PdfArray>(DictionaryValue(
            partial, partialNames[Name("EmbeddedFiles")])[Name("Names")]);
        Assert.Equal(-1, partialBytes.AsSpan().IndexOf("source payload"u8));
        Assert.Equal(["target.txt"], Enumerable.Range(0, partialFiles.Count / 2)
            .Select(index => DecodeUnicode(Assert.IsType<PdfString>(partialFiles[index * 2]))));
        Assert.Single(Assert.IsType<PdfArray>(partialCatalog[Name("AF")]));

        PdfDocument collision = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddAttachment("target.txt", ReadOnlyMemory<byte>.Empty).Build());
        PdfDocument collisionMerged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(collision).Build());
        PdfDictionary collisionCatalog = ResolveDictionary(
            collisionMerged, collisionMerged.Trailer[Name("Root")]);
        PdfDictionary collisionNames = DictionaryValue(
            collisionMerged, collisionCatalog[Name("Names")]);
        PdfArray collisionFiles = Assert.IsType<PdfArray>(DictionaryValue(
            collisionMerged, collisionNames[Name("EmbeddedFiles")])[Name("Names")]);
        Assert.Equal(["target.txt", "target.txt (2)"],
            Enumerable.Range(0, collisionFiles.Count / 2).Select(index =>
                DecodeUnicode(Assert.IsType<PdfString>(collisionFiles[index * 2]))));
    }

    [Fact]
    public void PartialPageImport_PreservesLocalFileAttachmentWithoutGlobalRegistration()
    {
        byte[] payload = "selected attachment payload"u8.ToArray();
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddAttachment("selected.txt", payload, "text/plain")
            .AddFileAttachmentAnnotation(0, 20, 20, 24, "selected.txt")
            .Build());
        PdfDocument target = PdfDocument.Open(new PdfDocumentBuilder().Build());

        byte[] importedBytes = new PdfIncrementalPageEditor(target)
            .AddImportedPage(source, 0)
            .Build();
        PdfDocument imported = PdfDocument.Open(importedBytes);
        (_, _, PdfDictionary[] pages) = FlatPages(imported);
        PdfDictionary annotation = ResolveDictionary(imported,
            Assert.IsType<PdfArray>(pages[0][Name("Annots")])[0]);
        PdfDictionary file = ResolveDictionary(imported, annotation[Name("FS")]);
        PdfDictionary streams = DictionaryValue(imported, file[Name("EF")]);
        PdfStream embedded = ResolveStream(imported, streams[Name("UF")]);
        PdfDictionary catalog = ResolveDictionary(imported, imported.Trailer[Name("Root")]);

        Assert.Equal(payload, embedded.EncodedData.ToArray());
        Assert.False(catalog.ContainsKey(Name("Names")));
        Assert.False(catalog.ContainsKey(Name("AF")));

        byte[] unrelatedPage = new PdfIncrementalPageEditor(target)
            .AddImportedPage(source, 1)
            .Build();
        Assert.Equal(-1, unrelatedPage.AsSpan().IndexOf(payload));
    }

    [Fact]
    public void Build_PreservesAdditionalCatalogNameTreeCategories()
    {
        PdfDocument target = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(), "JavaScript", "target", "target script");
        PdfDocument source = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().AddBlankPage().Build(),
            "JavaScript", "source", "source script");

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary names = DictionaryValue(merged, catalog[Name("Names")]);
        PdfArray values = Assert.IsType<PdfArray>(
            DictionaryValue(merged, names[Name("JavaScript")])[Name("Names")]);

        Assert.Equal(["source", "target"], Enumerable.Range(0, values.Count / 2)
            .Select(index => DecodeUnicode(Assert.IsType<PdfString>(values[index * 2]))));
        Assert.Equal(["source script", "target script"],
            Enumerable.Range(0, values.Count / 2).Select(index => ScriptText(
                merged, values[index * 2 + 1])));
        byte[] partialBytes = new PdfIncrementalPageEditor(target)
            .AddImportedPage(source, 0).Build();
        PdfDocument partial = PdfDocument.Open(partialBytes);
        PdfDictionary partialCatalog = ResolveDictionary(
            partial, partial.Trailer[Name("Root")]);
        PdfDictionary partialNames = DictionaryValue(partial, partialCatalog[Name("Names")]);
        PdfArray partialValues = Assert.IsType<PdfArray>(
            DictionaryValue(partial, partialNames[Name("JavaScript")])[Name("Names")]);
        Assert.Equal(["target"], Enumerable.Range(0, partialValues.Count / 2)
            .Select(index => DecodeUnicode(Assert.IsType<PdfString>(partialValues[index * 2]))));
        Assert.Equal(-1, partialBytes.AsSpan().IndexOf("source script"u8));
        PdfDocument collision = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "JavaScript", "target", "other script");
        Assert.Throws<NotSupportedException>(() => new PdfIncrementalPageEditor(target)
            .AddImportedDocument(collision).Build());

        PdfDocument invalidScript = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "JavaScript", "invalid", "bare script", validJavaScript: false);
        InvalidOperationException scriptError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidScript).Build());
        Assert.Contains("/Names /JavaScript name-tree value is not a JavaScript action dictionary",
            scriptError.Message, StringComparison.Ordinal);

        PdfDocument invalidContentSet = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "IDS", "identifier", "not a content set");
        InvalidOperationException contentSetError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidContentSet).Build());
        Assert.Contains("/Names /IDS name-tree value is not a Web Capture content-set dictionary",
            contentSetError.Message, StringComparison.Ordinal);

        PdfDocument invalidPresentation = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "AlternatePresentations", "show", "not a slideshow");
        InvalidOperationException presentationError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(target)
                    .AddImportedDocument(invalidPresentation).Build());
        Assert.Contains("/Names /AlternatePresentations name-tree value is not a slideshow dictionary",
            presentationError.Message, StringComparison.Ordinal);

        PdfDocument invalidRendition = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "Renditions", "media", "not a rendition");
        InvalidOperationException renditionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidRendition).Build());
        Assert.Contains("/Names /Renditions name-tree value is not a rendition dictionary",
            renditionError.Message, StringComparison.Ordinal);

        PdfDocument incompleteRendition = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "Renditions", "incomplete", "unused", rawValue: new PdfDictionary([
                new(Name("Type"), Name("Rendition")),
                new(Name("S"), Name("MR"))
            ]));
        InvalidOperationException incompleteRenditionError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(target)
                    .AddImportedDocument(incompleteRendition).Build());
        Assert.Contains("media rendition has neither /C nor /P dictionary",
            incompleteRenditionError.Message, StringComparison.Ordinal);

        PdfDocument invalidPlayParameters = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "Renditions", "bad volume", "unused", rawValue: new PdfDictionary([
                new(Name("Type"), Name("Rendition")),
                new(Name("S"), Name("MR")),
                new(Name("P"), new PdfDictionary([
                    new(Name("Type"), Name("MediaPlayParams")),
                    new(Name("MH"), new PdfDictionary([
                        new(Name("V"), new PdfInteger(-1))
                    ]))
                ]))
            ]));
        InvalidOperationException playError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidPlayParameters).Build());
        Assert.Contains("/P /MH /V value is not a nonnegative integer",
            playError.Message, StringComparison.Ordinal);

        PdfDocument invalidDuration = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "Renditions", "negative duration", "unused", rawValue: new PdfDictionary([
                new(Name("Type"), Name("Rendition")),
                new(Name("S"), Name("MR")),
                new(Name("P"), new PdfDictionary([
                    new(Name("MH"), new PdfDictionary([
                        new(Name("D"), new PdfDictionary([
                            new(Name("Type"), Name("MediaDuration")),
                            new(Name("S"), Name("T")),
                            new(Name("T"), new PdfDictionary([
                                new(Name("Type"), Name("Timespan")),
                                new(Name("S"), Name("S")),
                                new(Name("V"), new PdfInteger(-1))
                            ]))
                        ]))
                    ]))
                ]))
            ]));
        InvalidOperationException durationError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidDuration).Build());
        Assert.Contains("has no valid finite /V seconds value",
            durationError.Message, StringComparison.Ordinal);

        PdfDocument invalidPlayer = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "Renditions", "missing player ID", "unused", rawValue: new PdfDictionary([
                new(Name("Type"), Name("Rendition")),
                new(Name("S"), Name("MR")),
                new(Name("P"), new PdfDictionary([
                    new(Name("PL"), new PdfDictionary([
                        new(Name("Type"), Name("MediaPlayers")),
                        new(Name("MU"), new PdfArray([
                            new PdfDictionary([
                                new(Name("Type"), Name("MediaPlayerInfo"))
                            ])
                        ]))
                    ]))
                ]))
            ]));
        InvalidOperationException playerError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidPlayer).Build());
        Assert.Contains("/PL value /MU entry has no /PID string",
            playerError.Message, StringComparison.Ordinal);

        PdfDocument invalidPermission = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "Renditions", "invalid permission", "unused", rawValue: new PdfDictionary([
                new(Name("Type"), Name("Rendition")),
                new(Name("S"), Name("MR")),
                new(Name("C"), new PdfDictionary([
                    new(Name("Type"), Name("MediaClip")),
                    new(Name("S"), Name("MCD")),
                    new(Name("D"), new PdfDictionary([
                        new(Name("F"), new PdfString("clip.mov"u8, PdfStringForm.Literal))
                    ])),
                    new(Name("P"), new PdfDictionary([
                        new(Name("Type"), Name("MediaPermissions")),
                        new(Name("TF"), Name("FOREVER"))
                    ]))
                ]))
            ]));
        InvalidOperationException permissionError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalidPermission).Build());
        Assert.Contains("/C /P value /TF value is not a defined temporary-access name",
            permissionError.Message, StringComparison.Ordinal);

        PdfDocument invalidAlternateDescription = WithNameTree(
            new PdfDocumentBuilder().AddBlankPage().Build(),
            "Renditions", "invalid alternate description", "unused", rawValue: new PdfDictionary([
                new(Name("Type"), Name("Rendition")),
                new(Name("S"), Name("MR")),
                new(Name("C"), new PdfDictionary([
                    new(Name("Type"), Name("MediaClip")),
                    new(Name("S"), Name("MCD")),
                    new(Name("D"), new PdfDictionary([
                        new(Name("F"), new PdfString("clip.mov"u8, PdfStringForm.Literal))
                    ])),
                    new(Name("Alt"), new PdfArray([
                        new PdfString("en-US"u8, PdfStringForm.Literal)
                    ]))
                ]))
            ]));
        InvalidOperationException alternateDescriptionError =
            Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalPageEditor(target)
                    .AddImportedDocument(invalidAlternateDescription).Build());
        Assert.Contains("/C /Alt value is not a language and text string-pair array",
            alternateDescriptionError.Message, StringComparison.Ordinal);

        PdfDocument missingSourceUrl = WithContentSetMissingSourceUrl();
        InvalidOperationException sourceUrlError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(missingSourceUrl).Build());
        Assert.Contains("/Names /IDS name-tree value /SI entry has no /AU URL value",
            sourceUrlError.Message, StringComparison.Ordinal);

        PdfIndirectReference sourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary sourceCatalog = ResolveDictionary(source, sourceCatalogReference);
        PdfDictionary sourceNames = DictionaryValue(source, sourceCatalog[Name("Names")]);
        PdfDictionary scripts = DictionaryValue(source, sourceNames[Name("JavaScript")]);
        PdfArray scriptValues = Assert.IsType<PdfArray>(scripts[Name("Names")]);
        (_, PdfIndirectReference[] sourcePages, _) = FlatPages(source);
        PdfDictionary staleScripts = new(scripts
            .Where(entry => !entry.Key.Equals(Name("Names")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Names"), new PdfArray([
                scriptValues[0], new PdfIndirectReference(
                    sourcePages[0].ObjectNumber, sourcePages[0].Generation + 1)
            ]))));
        PdfDictionary staleNames = new(sourceNames
            .Where(entry => !entry.Key.Equals(Name("JavaScript")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("JavaScript"), staleScripts)));
        PdfDocument staleSource = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(source)
                .ReplaceObject(sourceCatalogReference.ObjectNumber,
                    new PdfDictionary(sourceCatalog
                        .Where(entry => !entry.Key.Equals(Name("Names")))
                        .Append(new KeyValuePair<PdfName, PdfObject>(
                            Name("Names"), staleNames))))
                .Build());
        InvalidOperationException staleError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(staleSource).Build());
        Assert.Contains("source /Names /JavaScript name tree contains a stale value reference",
            staleError.Message, StringComparison.Ordinal);

        var indirectNamesUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference indirectNameKey = indirectNamesUpdate.AddObject(scriptValues[0]);
        PdfIndirectReference indirectNameKeyAlias =
            indirectNamesUpdate.AddObject(indirectNameKey);
        PdfIndirectReference indirectNamesArray = indirectNamesUpdate.AddObject(
            new PdfArray([indirectNameKeyAlias, scriptValues[1]]));
        PdfIndirectReference indirectNamesArrayAlias =
            indirectNamesUpdate.AddObject(indirectNamesArray);
        PdfIndirectReference indirectNameLimits = indirectNamesUpdate.AddObject(
            new PdfArray([indirectNameKeyAlias, indirectNameKeyAlias]));
        PdfIndirectReference indirectNameLimitsAlias =
            indirectNamesUpdate.AddObject(indirectNameLimits);
        PdfIndirectReference indirectNameLeaf = indirectNamesUpdate.AddObject(
            new PdfDictionary([
                new(Name("Names"), indirectNamesArrayAlias),
                new(Name("Limits"), indirectNameLimitsAlias)
            ]));
        PdfIndirectReference indirectNameKids = indirectNamesUpdate.AddObject(
            new PdfArray([indirectNameLeaf]));
        PdfDictionary indirectScripts = new([
            new(Name("Kids"), indirectNameKids),
            new(Name("Limits"), indirectNameLimitsAlias)
        ]);
        PdfDictionary indirectNames = new(sourceNames
            .Where(entry => !entry.Key.Equals(Name("JavaScript")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("JavaScript"), indirectScripts)));
        indirectNamesUpdate.ReplaceObject(sourceCatalogReference.ObjectNumber,
            new PdfDictionary(sourceCatalog
                .Where(entry => !entry.Key.Equals(Name("Names")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Names"), indirectNames))));
        PdfDocument indirectNamesSource = PdfDocument.Open(indirectNamesUpdate.Build());
        PdfDocument indirectNamesMerged = PdfDocument.Open(
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(indirectNamesSource).Build());
        PdfDictionary indirectMergedCatalog = ResolveDictionary(
            indirectNamesMerged, indirectNamesMerged.Trailer[Name("Root")]);
        Assert.True(indirectMergedCatalog.ContainsKey(Name("Names")));

        PdfDictionary unorderedScripts = new(scripts
            .Where(entry => !entry.Key.Equals(Name("Names")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Names"), new PdfArray([
                Unicode("z"), scriptValues[1], Unicode("a"), scriptValues[1]
            ]))));
        PdfDictionary unorderedNames = new(sourceNames
            .Where(entry => !entry.Key.Equals(Name("JavaScript")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("JavaScript"), unorderedScripts)));
        PdfDocument unorderedSource = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(source)
                .ReplaceObject(sourceCatalogReference.ObjectNumber,
                    new PdfDictionary(sourceCatalog
                        .Where(entry => !entry.Key.Equals(Name("Names")))
                        .Append(new KeyValuePair<PdfName, PdfObject>(
                            Name("Names"), unorderedNames))))
                .Build());
        InvalidOperationException unorderedError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(unorderedSource).Build());
        Assert.Contains("name tree contains keys that are not strictly ordered",
            unorderedError.Message, StringComparison.Ordinal);

        PdfDictionary badLimitsScripts = new(scripts
            .Where(entry => !entry.Key.Equals(Name("Limits")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Limits"), new PdfArray([
                Unicode("wrong"), Unicode("source")
            ]))));
        PdfDictionary badLimitsNames = new(sourceNames
            .Where(entry => !entry.Key.Equals(Name("JavaScript")))
            .Append(new KeyValuePair<PdfName, PdfObject>(
                Name("JavaScript"), badLimitsScripts)));
        PdfDocument badLimitsSource = PdfDocument.Open(
            new PdfIncrementalUpdateBuilder(source)
                .ReplaceObject(sourceCatalogReference.ObjectNumber,
                    new PdfDictionary(sourceCatalog
                        .Where(entry => !entry.Key.Equals(Name("Names")))
                        .Append(new KeyValuePair<PdfName, PdfObject>(
                            Name("Names"), badLimitsNames))))
                .Build());
        InvalidOperationException badLimitsError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(badLimitsSource).Build());
        Assert.Contains("name-tree /Limits value does not match",
            badLimitsError.Message, StringComparison.Ordinal);

        var missingLimitsUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference missingLimitsLeaf = missingLimitsUpdate.AddObject(
            new PdfDictionary([new(Name("Names"), scriptValues)]));
        PdfDictionary missingLimitsNames = new(sourceNames
            .Where(entry => !entry.Key.Equals(Name("JavaScript")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("JavaScript"),
                new PdfDictionary([
                    new(Name("Kids"), new PdfArray([missingLimitsLeaf]))
                ]))));
        missingLimitsUpdate.ReplaceObject(sourceCatalogReference.ObjectNumber,
            new PdfDictionary(sourceCatalog
                .Where(entry => !entry.Key.Equals(Name("Names")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("Names"), missingLimitsNames))));
        PdfDocument missingLimitsSource = PdfDocument.Open(missingLimitsUpdate.Build());
        InvalidOperationException missingLimitsError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(missingLimitsSource).Build());
        Assert.Contains("non-root name-tree node has no /Limits",
            missingLimitsError.Message, StringComparison.Ordinal);

        static PdfDocument WithNameTree(
            byte[] bytes, string category, string key, string value,
            bool validJavaScript = true, PdfObject? rawValue = null)
        {
            PdfDocument document = PdfDocument.Open(bytes);
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfIndirectReference valueReference = update.AddObject(
                rawValue ?? (category == "JavaScript" && validJavaScript
                    ? new PdfDictionary([
                        new(Name("Type"), Name("Action")),
                        new(Name("S"), Name("JavaScript")),
                        new(Name("JS"), Unicode(value))
                    ])
                    : Unicode(value)));
            PdfDictionary catalog = ResolveDictionary(
                document, document.Trailer[Name("Root")]);
            PdfDictionary replacement = new(catalog
                .Where(entry => !entry.Key.Equals(Name("Names")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Names"),
                    new PdfDictionary([
                        new(Name(category), new PdfDictionary([
                            new(Name("Names"), new PdfArray([
                                Unicode(key),
                                valueReference
                            ]))
                        ]))
                    ]))));
            update.ReplaceObject(
                Assert.IsType<PdfIndirectReference>(document.Trailer[Name("Root")]).ObjectNumber,
                replacement);
            return PdfDocument.Open(update.Build());
        }

        static PdfDocument WithContentSetMissingSourceUrl()
        {
            PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
                .AddBlankPage().Build());
            (_, PdfIndirectReference[] pages, _) = FlatPages(document);
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfIndirectReference contentSet = update.AddObject(new PdfDictionary([
                new(Name("Type"), Name("SpiderContentSet")),
                new(Name("S"), Name("SPS")),
                new(Name("ID"), new PdfString("id"u8, PdfStringForm.Hexadecimal)),
                new(Name("O"), new PdfArray([pages[0]])),
                new(Name("SI"), new PdfDictionary([]))
            ]));
            PdfDictionary catalog = ResolveDictionary(
                document, document.Trailer[Name("Root")]);
            update.ReplaceObject(
                Assert.IsType<PdfIndirectReference>(
                    document.Trailer[Name("Root")]).ObjectNumber,
                new PdfDictionary(catalog.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Names"),
                        new PdfDictionary([
                            new(Name("IDS"), new PdfDictionary([
                                new(Name("Names"), new PdfArray([
                                    Unicode("id"), contentSet
                                ]))
                            ]))
                        ])))));
            return PdfDocument.Open(update.Build());
        }

        static string ScriptText(PdfDocument document, PdfObject value)
        {
            PdfDictionary action = ResolveDictionary(document, value);
            return DecodeUnicode(Assert.IsType<PdfString>(action[Name("JS")]));
        }

        static PdfString Unicode(string value) => new(
            [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(value)],
            PdfStringForm.Hexadecimal);
    }

    [Fact]
    public void Build_MergesDistinctCatalogExtensionNamespaces()
    {
        PdfDocument target = WithExtension(
            new PdfDocumentBuilder().AddBlankPage().Build(), "ADBE", 3);
        PdfDocument source = WithExtension(
            new PdfDocumentBuilder().AddBlankPage().AddBlankPage().Build(), "ISO_", 8);

        PdfDocument merged = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary extensions = DictionaryValue(merged, catalog[Name("Extensions")]);

        Assert.Equal(3, Assert.IsType<PdfInteger>(DictionaryValue(
            merged, extensions[Name("ADBE")])[Name("ExtensionLevel")]).Value);
        Assert.Equal(8, Assert.IsType<PdfInteger>(DictionaryValue(
            merged, extensions[Name("ISO_")])[Name("ExtensionLevel")]).Value);
        PdfDocument partial = PdfDocument.Open(new PdfIncrementalPageEditor(target)
            .AddImportedPage(source, 0).Build());
        PdfDictionary partialCatalog = ResolveDictionary(
            partial, partial.Trailer[Name("Root")]);
        PdfDictionary partialExtensions = DictionaryValue(
            partial, partialCatalog[Name("Extensions")]);
        Assert.True(partialExtensions.ContainsKey(Name("ADBE")));
        Assert.False(partialExtensions.ContainsKey(Name("ISO_")));
        PdfDocument collision = WithExtension(
            new PdfDocumentBuilder().AddBlankPage().Build(), "ADBE", 9);
        Assert.Throws<NotSupportedException>(() => new PdfIncrementalPageEditor(target)
            .AddImportedDocument(collision).Build());

        PdfDocument invalid = WithExtension(
            new PdfDocumentBuilder().AddBlankPage().Build(), "BAD", 1, valid: false);
        InvalidOperationException invalidError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(target)
                .AddImportedDocument(invalid).Build());
        Assert.Contains("no valid /BaseVersion name", invalidError.Message,
            StringComparison.Ordinal);

        PdfDocument nullOnly = WithNullExtension(
            new PdfDocumentBuilder().AddBlankPage().Build());
        PdfDocument nullOnlyMerged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().Build()))
                .AddImportedDocument(nullOnly).Build());
        PdfDictionary nullOnlyCatalog = ResolveDictionary(
            nullOnlyMerged, nullOnlyMerged.Trailer[Name("Root")]);
        Assert.False(nullOnlyCatalog.ContainsKey(Name("Extensions")));

        static PdfDocument WithExtension(
            byte[] bytes, string name, int level, bool valid = true)
        {
            PdfDocument document = PdfDocument.Open(bytes);
            var update = new PdfIncrementalUpdateBuilder(document);
            PdfIndirectReference extension = update.AddObject(new PdfDictionary([
                new(Name("BaseVersion"), valid ? Name("2.0") : new PdfInteger(2)),
                new(Name("ExtensionLevel"), new PdfInteger(level))
            ]));
            PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
                document.Trailer[Name("Root")]);
            PdfDictionary catalog = ResolveDictionary(document, catalogReference);
            update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("Extensions")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Extensions"),
                    new PdfDictionary([new(Name(name), extension)])))));
            return PdfDocument.Open(update.Build());
        }

        static PdfDocument WithNullExtension(byte[] bytes)
        {
            PdfDocument document = PdfDocument.Open(bytes);
            PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
                document.Trailer[Name("Root")]);
            PdfDictionary catalog = ResolveDictionary(document, catalogReference);
            return PdfDocument.Open(new PdfIncrementalUpdateBuilder(document)
                .ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                    .Append(new KeyValuePair<PdfName, PdfObject>(Name("Extensions"),
                        new PdfDictionary([new(Name("NULL"), PdfNull.Instance)])))))
                .Build());
        }
    }

    [Fact]
    public void Build_PreservesEffectivePageLabelsThroughPageOperations()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage().AddBlankPage().AddBlankPage()
            .AddPageLabelRange(0, PdfPageLabelStyle.LowerRoman)
            .AddPageLabelRange(2, PdfPageLabelStyle.Decimal, "A-", 3)
            .Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .MovePage(3, 0)
                .InsertBlankPage(2)
                .RemovePage(4)
                .Build());
        (long PageIndex, PdfDictionary Label)[] ranges = PageLabelRanges(reopened);

        Assert.Equal([0L, 1L, 2L, 3L], ranges.Select(range => range.PageIndex));
        AssertLabel(ranges[0].Label, "D", "A-", 4);
        AssertLabel(ranges[1].Label, "r", null, 1);
        AssertLabel(ranges[2].Label, "D", null, 3);
        AssertLabel(ranges[3].Label, "r", null, 2);
    }

    [Fact]
    public void Build_MergesAndCompressesPageLabelsFromImportedDocuments()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddPageLabelRange(0, PdfPageLabelStyle.LowerRoman)
            .Build());
        PdfIndirectReference sourceCatalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary sourceCatalog = ResolveDictionary(source, sourceCatalogReference);
        PdfDictionary sourceLabels = Assert.IsType<PdfDictionary>(
            sourceCatalog[Name("PageLabels")]);
        PdfArray sourceNumbers = Assert.IsType<PdfArray>(sourceLabels[Name("Nums")]);
        PdfDictionary sourceLabel = Assert.IsType<PdfDictionary>(sourceNumbers[1]);
        var sourceUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfObject Indirect(PdfObject value) => sourceUpdate.AddObject(
            sourceUpdate.AddObject(value));
        var sourceLabelEntries = sourceLabel.ToDictionary(
            entry => entry.Key, entry => entry.Value);
        sourceLabelEntries[Name("S")] = Indirect(Name("r"));
        sourceLabelEntries[Name("P")] = Indirect(
            new PdfString([0xFE, 0xFF, 0x00, 0x53, 0x00, 0x2D],
                PdfStringForm.Hexadecimal));
        sourceLabelEntries[Name("St")] = Indirect(new PdfInteger(3));
        var sourceLabelsEntries = sourceLabels.ToDictionary(
            entry => entry.Key, entry => entry.Value);
        sourceLabelsEntries[Name("Nums")] = new PdfArray([
            sourceNumbers[0], new PdfDictionary(sourceLabelEntries)]);
        var sourceCatalogEntries = sourceCatalog.ToDictionary(
            entry => entry.Key, entry => entry.Value);
        sourceCatalogEntries[Name("PageLabels")] = new PdfDictionary(sourceLabelsEntries);
        sourceUpdate.ReplaceObject(sourceCatalogReference.ObjectNumber,
            new PdfDictionary(sourceCatalogEntries));
        source = PdfDocument.Open(sourceUpdate.Build());
        byte[] target = new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddPageLabelRange(0, PdfPageLabelStyle.Decimal, "T-")
            .Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .InsertImportedDocument(1, source)
                .Build());
        (long PageIndex, PdfDictionary Label)[] ranges = PageLabelRanges(reopened);

        Assert.Equal([0L, 1L, 3L], ranges.Select(range => range.PageIndex));
        AssertLabel(ranges[0].Label, "D", "T-", 1);
        AssertLabel(ranges[1].Label, "r", "S-", 3);
        AssertLabel(ranges[2].Label, "D", "T-", 2);

        PdfDocument split = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 1)
                .Build());
        (long PageIndex, PdfDictionary Label)[] splitRanges = PageLabelRanges(split);
        Assert.Single(splitRanges);
        Assert.Equal(0, splitRanges[0].PageIndex);
        AssertLabel(splitRanges[0].Label, "r", "S-", 4);
    }

    [Fact]
    public void Build_RejectsUnorderedImportedPageLabelNumberTrees()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddPageLabelRange(0, PdfPageLabelStyle.LowerRoman)
            .AddPageLabelRange(1, PdfPageLabelStyle.Decimal)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary labels = DictionaryValue(source, catalog[Name("PageLabels")]);
        PdfArray numbers = Assert.IsType<PdfArray>(labels[Name("Nums")]);
        var indirectNumbersUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference indirectFirstNumber = indirectNumbersUpdate.AddObject(numbers[0]);
        PdfIndirectReference indirectLastNumber = indirectNumbersUpdate.AddObject(numbers[2]);
        PdfIndirectReference indirectFirstNumberAlias =
            indirectNumbersUpdate.AddObject(indirectFirstNumber);
        PdfIndirectReference indirectLastNumberAlias =
            indirectNumbersUpdate.AddObject(indirectLastNumber);
        PdfIndirectReference indirectNumbersArray = indirectNumbersUpdate.AddObject(
            new PdfArray([
                indirectFirstNumberAlias, numbers[1], indirectLastNumberAlias, numbers[3]
            ]));
        PdfIndirectReference indirectNumbersArrayAlias =
            indirectNumbersUpdate.AddObject(indirectNumbersArray);
        PdfIndirectReference indirectNumberLimits = indirectNumbersUpdate.AddObject(
            new PdfArray([indirectFirstNumberAlias, indirectLastNumberAlias]));
        PdfIndirectReference indirectNumberLimitsAlias =
            indirectNumbersUpdate.AddObject(indirectNumberLimits);
        PdfIndirectReference indirectNumberLeaf = indirectNumbersUpdate.AddObject(
            new PdfDictionary([
                new(Name("Nums"), indirectNumbersArrayAlias),
                new(Name("Limits"), indirectNumberLimitsAlias)
            ]));
        PdfIndirectReference indirectNumberKids = indirectNumbersUpdate.AddObject(
            new PdfArray([indirectNumberLeaf]));
        PdfDictionary indirectLabels = new([
            new(Name("Kids"), indirectNumberKids),
            new(Name("Limits"), indirectNumberLimitsAlias)
        ]);
        indirectNumbersUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("PageLabels")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("PageLabels"), indirectLabels))));
        PdfDocument indirectNumbers = PdfDocument.Open(indirectNumbersUpdate.Build());
        PdfDocument importedIndirectNumbers = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(indirectNumbers).Build());
        Assert.Equal([0L, 1L], PageLabelRanges(importedIndirectNumbers)
            .Select(range => range.PageIndex));

        PdfDictionary unorderedLabels = new(labels
            .Where(entry => !entry.Key.Equals(Name("Nums")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Nums"), new PdfArray([
                numbers[2], numbers[3], numbers[0], numbers[1]
            ]))));
        PdfDocument unordered = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("PageLabels")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("PageLabels"), unorderedLabels))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(unordered).Build());

        Assert.Contains("number tree contains keys that are not strictly ordered",
            error.Message, StringComparison.Ordinal);

        PdfDictionary badLimitsLabels = new(labels.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Limits"), new PdfArray([
                new PdfInteger(0), new PdfInteger(9)
            ]))));
        PdfDocument badLimits = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("PageLabels")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("PageLabels"), badLimitsLabels))))
            .Build());
        InvalidOperationException limitsError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(badLimits).Build());
        Assert.Contains("number-tree /Limits value does not match",
            limitsError.Message, StringComparison.Ordinal);

        var missingLimitsUpdate = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference missingLimitsLeaf = missingLimitsUpdate.AddObject(
            new PdfDictionary([new(Name("Nums"), numbers)]));
        PdfDictionary missingLimitsLabels = new([
            new(Name("Kids"), new PdfArray([missingLimitsLeaf]))
        ]);
        missingLimitsUpdate.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog
                .Where(entry => !entry.Key.Equals(Name("PageLabels")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("PageLabels"), missingLimitsLabels))));
        PdfDocument missingLimits = PdfDocument.Open(missingLimitsUpdate.Build());
        InvalidOperationException missingLimitsError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedDocument(missingLimits).Build());
        Assert.Contains("non-root number-tree node has no /Limits",
            missingLimitsError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedImportedPageLabelPrefixes()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddPageLabelRange(0, PdfPageLabelStyle.Decimal, "Valid")
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary labels = Assert.IsType<PdfDictionary>(catalog[Name("PageLabels")]);
        PdfArray numbers = Assert.IsType<PdfArray>(labels[Name("Nums")]);
        PdfDictionary label = Assert.IsType<PdfDictionary>(numbers[1]);
        var labelEntries = label.ToDictionary(entry => entry.Key, entry => entry.Value);
        labelEntries[Name("P")] = new PdfString(
            [0xFE, 0xFF, 0xD8, 0x00], PdfStringForm.Hexadecimal);
        var labelRootEntries = labels.ToDictionary(entry => entry.Key, entry => entry.Value);
        labelRootEntries[Name("Nums")] = new PdfArray([
            numbers[0], new PdfDictionary(labelEntries)]);
        var catalogEntries = catalog.ToDictionary(entry => entry.Key, entry => entry.Value);
        catalogEntries[Name("PageLabels")] = new PdfDictionary(labelRootEntries);
        source = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(catalogReference.ObjectNumber,
                new PdfDictionary(catalogEntries)).Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(
                    new PdfDocumentBuilder().AddBlankPage().Build()))
                .AddImportedDocument(source).Build());

        Assert.Contains("page-label /P value", error.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RemovesPageLabelsWhenEveryPageIsDeleted()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddPageLabelRange(0, PdfPageLabelStyle.UpperRoman)
            .Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source)).RemovePage(0).Build());
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);

        Assert.False(catalog.ContainsKey(Name("PageLabels")));
    }

    [Fact]
    public void ArgumentsAndEmptyUpdates_AreRejected()
    {
        var editor = new PdfIncrementalPageEditor(PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build()));

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.MovePage(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.MovePage(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.RemovePage(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.InsertBlankPage(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddBlankPage(double.PositiveInfinity, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddImportedPage(
            PdfDocument.Open(new PdfDocumentBuilder().Build()), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetRotation(0, 45));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetMediaBox(0, 0, 0, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetCropBox(0, 0, 0, 100, double.NaN));
        Assert.Throws<InvalidOperationException>(() => editor.Build());
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().AddBlankPage().Build();
        byte[] Edit() => new PdfIncrementalPageEditor(PdfDocument.Open(source))
            .MovePage(0, 1)
            .InsertBlankPage(1, 320, 240)
            .SetRotation(0, 270)
            .Build();

        Assert.Equal(Edit(), Edit());
    }

    private static (PdfIndirectReference Root, PdfIndirectReference[] References,
        PdfDictionary[] Pages) FlatPages(PdfDocument document)
    {
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var rootReference = Assert.IsType<PdfIndirectReference>(catalog[Name("Pages")]);
        PdfDictionary root = ResolveDictionary(document, rootReference);
        PdfIndirectReference[] references = Assert.IsType<PdfArray>(root[Name("Kids")])
            .Select(Assert.IsType<PdfIndirectReference>).ToArray();
        return (rootReference, references,
            references.Select(reference => ResolveDictionary(document, reference)).ToArray());
    }

    private static void AssertStructureTreeDoesNotReferencePage(
        PdfDocument document, PdfObject value, int removedPageNumber)
    {
        var visited = new HashSet<int>();
        Visit(value);

        void Visit(PdfObject current)
        {
            if (current is PdfIndirectReference reference)
            {
                if (!visited.Add(reference.ObjectNumber)) return;
                current = document.Resolve(reference);
            }
            if (current is PdfArray array)
            {
                foreach (PdfObject item in array) Visit(item);
                return;
            }
            if (current is not PdfDictionary dictionary) return;
            if (dictionary.TryGetValue(Name("Pg"), out PdfObject? page)
                && page is PdfIndirectReference pageReference)
                Assert.NotEqual(removedPageNumber, pageReference.ObjectNumber);
            foreach (string key in new[] { "K", "ParentTree", "Nums", "Kids" })
                if (dictionary.TryGetValue(Name(key), out PdfObject? child)) Visit(child);
        }
    }

    private static double BoxWidth(PdfDictionary page)
    {
        PdfArray box = Assert.IsType<PdfArray>(page[Name("MediaBox")]);
        return Number(box[2]) - Number(box[0]);
    }

    private static double[] Box(PdfDictionary page, string name) =>
        Assert.IsType<PdfArray>(page[Name(name)]).Select(Number).ToArray();

    private static (long PageIndex, PdfDictionary Label)[] PageLabelRanges(PdfDocument document)
    {
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary labels = DictionaryValue(document, catalog[Name("PageLabels")]);
        PdfArray numbers = Assert.IsType<PdfArray>(labels[Name("Nums")]);
        return Enumerable.Range(0, numbers.Count / 2)
            .Select(index => (
                Assert.IsType<PdfInteger>(numbers[index * 2]).Value,
                DictionaryValue(document, numbers[index * 2 + 1])))
            .ToArray();
    }

    private static void AssertLabel(
        PdfDictionary label, string style, string? prefix, long start)
    {
        Assert.Equal(style, Assert.IsType<PdfName>(label[Name("S")]).ValueAsLatin1());
        if (prefix is null)
            Assert.False(label.ContainsKey(Name("P")));
        else
            Assert.Equal(prefix, DecodeUnicode(Assert.IsType<PdfString>(label[Name("P")])));
        Assert.Equal(start, label.TryGetValue(Name("St"), out PdfObject? value)
            ? Assert.IsType<PdfInteger>(value).Value
            : 1);
    }

    private static double Number(PdfObject value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => throw new Xunit.Sdk.XunitException("Expected a PDF number.")
    };

    private static byte[] BuildTaggedDocument(
        string? userPassword = null, string? ownerPassword = null)
    {
        static PdfContentStreamBuilder TaggedPage() => new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Figure, 0)
            .Rectangle(10, 10, 20, 20).Fill()
            .EndMarkedContent();

        var builder = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Tagged import test",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddPage(100, 100, TaggedPage())
            .AddPage(100, 100, TaggedPage())
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
                alternateDescription: "First square")
            .AddStructureElement(PdfStructureType.Figure, 1, 0, 1,
                alternateDescription: "Selected square");
        if (userPassword is not null || ownerPassword is not null)
            builder.SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = userPassword ?? string.Empty,
                OwnerPassword = ownerPassword ?? string.Empty
            });
        return builder.Build();
    }

    private static byte[] BuildLayeredDocument()
    {
        var layer = new PdfOptionalContentGroup("Review layer", initiallyVisible: false);
        PdfContentStreamBuilder Content() => new PdfContentStreamBuilder()
            .BeginOptionalContent(layer)
            .Rectangle(10, 10, 20, 20).Stroke()
            .EndMarkedContent();

        return new PdfDocumentBuilder()
            .AddPage(100, 100, Content())
            .AddPage(100, 100, Content())
            .Build();
    }

    private static byte[] BuildRgbProfile()
    {
        byte[] result = new byte[132];
        BinaryPrimitives.WriteUInt32BigEndian(result, 132);
        "RGB "u8.CopyTo(result.AsSpan(16, 4));
        "acsp"u8.CopyTo(result.AsSpan(36, 4));
        return result;
    }

    private static byte[] BuildNestedPageTree()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        var offsets = new int[6];
        Add(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 2 >>");
        Add(3, "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 " +
            "/MediaBox [0 0 200 300] /CropBox [10 10 190 290] /Resources <<>> /Rotate 90 >>");
        Add(4, "<< /Type /Page /Parent 3 0 R >>");
        Add(5, "<< /Type /Page /Parent 3 0 R >>");
        int xrefOffset = source.Length;
        source.Append("xref\n0 6\n0000000000 65535 f \n");
        for (int index = 1; index <= 5; index++)
            source.Append(offsets[index].ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        source.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());

        void Add(int number, string value)
        {
            offsets[number] = source.Length;
            source.Append(number).Append(" 0 obj\n").Append(value).Append("\nendobj\n");
        }
    }

    private static byte[] BuildLegacyDestinationDocument()
    {
        var source = new StringBuilder("%PDF-1.7\n");
        var offsets = new int[6];
        Add(1, "<< /Type /Catalog /Pages 2 0 R /Dests << /chapter [4 0 R /Fit] >> >>");
        Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>");
        Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] " +
            "/Resources <<>> /Annots [5 0 R] >>");
        Add(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Resources <<>> >>");
        Add(5, "<< /Type /Annot /Subtype /Link /Rect [10 10 50 30] " +
            "/Border [0 0 0] /Dest /chapter /P 3 0 R >>");
        int xrefOffset = source.Length;
        source.Append("xref\n0 6\n0000000000 65535 f \n");
        for (int index = 1; index <= 5; index++)
            source.Append(offsets[index].ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        source.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());

        void Add(int number, string value)
        {
            offsets[number] = source.Length;
            source.Append(number).Append(" 0 obj\n").Append(value).Append("\nendobj\n");
        }
    }

    private static byte[] BuildHierarchicalAcroFormDocument(
        string parentName, int signatureFlags, int defaultQuadding)
    {
        var source = new StringBuilder("%PDF-2.0\n");
        var offsets = new int[6];
        Add(1, $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] " +
            $"/SigFlags {signatureFlags} /CO [5 0 R] /Q {defaultQuadding} >> >>");
        Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] " +
            "/Resources <<>> /Annots [5 0 R] >>");
        Add(4, $"<< /T ({parentName}) /Kids [5 0 R] >>");
        Add(5, "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Parent 4 0 R " +
            "/P 3 0 R /Rect [10 10 100 30] >>");
        int xrefOffset = source.Length;
        source.Append("xref\n0 6\n0000000000 65535 f \n");
        for (int index = 1; index <= 5; index++)
            source.Append(offsets[index].ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        source.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());

        void Add(int number, string value)
        {
            offsets[number] = source.Length;
            source.Append(number).Append(" 0 obj\n").Append(value).Append("\nendobj\n");
        }
    }

    private static byte[] BuildEscapedAcroFormResourceDocument()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        var offsets = new int[7];
        Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] " +
            "/DR << /Font << /F#20One 6 0 R >> >> /DA (/F#20One 12 Tf 0 g) >> >>");
        Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] " +
            "/Resources <<>> /Annots [4 0 R] >>");
        Add(4, "<< /Type /Annot /Subtype /Widget /FT /Tx /T (source) " +
            "/DA (/F#20One 12 Tf 0 g) /P 3 0 R /Rect [10 10 100 30] >>");
        Add(5, "null");
        Add(6, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        int xrefOffset = source.Length;
        source.Append("xref\n0 7\n0000000000 65535 f \n");
        for (int index = 1; index <= 6; index++)
            source.Append(offsets[index].ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        source.Append("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());

        void Add(int number, string value)
        {
            offsets[number] = source.Length;
            source.Append(number).Append(" 0 obj\n").Append(value).Append("\nendobj\n");
        }
    }

    private static byte[] AppendCatalogRootAliases(
        byte[] source, PdfIndirectReference catalogReference, bool cycle = false)
    {
        PdfDocument document = PdfDocument.Open(source);
        Assert.True(document.CrossReferences.TryGetTrailerValue(
            Name("Size"), out PdfObject sizeValue));
        int firstObjectNumber = checked((int)Assert.IsType<PdfInteger>(sizeValue).Value);
        int secondObjectNumber = checked(firstObjectNumber + 1);
        long previousXref = PdfStartXref.Find(source).Offset;
        using var output = new MemoryStream();
        output.Write(source);
        int firstOffset = checked((int)output.Position);
        Write($"{firstObjectNumber} 0 obj {secondObjectNumber} 0 R endobj\n");
        int secondOffset = checked((int)output.Position);
        Write(cycle
            ? $"{secondObjectNumber} 0 obj {firstObjectNumber} 0 R endobj\n"
            : $"{secondObjectNumber} 0 obj {catalogReference.ObjectNumber} "
                + $"{catalogReference.Generation} R endobj\n");
        int xrefOffset = checked((int)output.Position);
        Write($"xref\n{firstObjectNumber} 2\n");
        Write($"{firstOffset:0000000000} 00000 n \n");
        Write($"{secondOffset:0000000000} 00000 n \n");
        Write($"trailer << /Size {secondObjectNumber + 1} /Prev {previousXref} "
            + $"/Root {firstObjectNumber} 0 R >>\n");
        Write($"startxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();

        void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));
    }

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value)
    {
        while (value is PdfIndirectReference reference)
            value = document.Resolve(reference);
        return Assert.IsType<PdfDictionary>(value);
    }
    private static PdfDictionary DictionaryValue(PdfDocument document, PdfObject value) =>
        value is PdfIndirectReference ? ResolveDictionary(document, value) : Assert.IsType<PdfDictionary>(value);
    private static PdfStream ResolveStream(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfStream>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static byte[] CertifiedSource(int permission)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            document.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(document, catalogReference);
        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference parameters = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("TransformParams")),
            new(Name("P"), new PdfInteger(permission))
        ]));
        PdfIndirectReference transform = update.AddObject(new PdfDictionary([
            new(Name("TransformMethod"), Name("DocMDP")),
            new(Name("TransformParams"), parameters)
        ]));
        PdfIndirectReference signature = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Sig")),
            new(Name("Reference"), new PdfArray([transform]))
        ]));
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Perms"), new PdfDictionary([
                new(Name("DocMDP"), signature)
            ])))));
        return update.Build();
    }
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
