using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Editing;

public sealed class PdfIncrementalPageEditorTests
{
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
        byte[] source = BuildNestedPageTree();
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
    public void TaggedImports_RejectPartialOrCombinedPageSets()
    {
        PdfDocument tagged = PdfDocument.Open(BuildTaggedDocument());
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());
        PdfDocument occupied = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(empty).AddImportedPage(tagged, 0));
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(tagged).RemovePage(1).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(tagged).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(tagged)
                .AddImportedDocument(tagged)
                .Build());
    }

    [Fact]
    public void ExistingTaggedDocument_AllowsCompleteReorderingButRejectsPageSetChanges()
    {
        byte[] source = BuildTaggedDocument();

        PdfDocument reordered = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .MovePage(0, 1)
                .Build());
        Assert.True(ResolveDictionary(reordered, reordered.Trailer[Name("Root")])
            .ContainsKey(Name("StructTreeRoot")));
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .RemovePage(0)
                .Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .AddBlankPage()
                .Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .AddImportedDocument(PdfDocument.Open(
                    new PdfDocumentBuilder().AddBlankPage().Build()))
                .Build());
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
    public void LayeredDocuments_RejectPartialOrCombinedPageSets()
    {
        byte[] source = BuildLayeredDocument();
        PdfDocument layered = PdfDocument.Open(source);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());
        PdfDocument occupied = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(empty).AddImportedPage(layered, 0));
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(layered).RemovePage(1).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(occupied)
                .AddImportedDocument(layered).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(empty)
                .AddImportedDocument(layered)
                .AddImportedDocument(layered)
                .Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .RemovePage(0)
                .Build());

        PdfDocument reordered = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(source))
                .MovePage(0, 1)
                .Build());
        Assert.True(ResolveDictionary(reordered, reordered.Trailer[Name("Root")])
            .ContainsKey(Name("OCProperties")));
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
    public void Import_RejectsDependenciesThatNeedDocumentLevelMerging()
    {
        PdfDocument linkedPages = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage().AddPageLink(0, 0, 0, 20, 20, 1).Build());
        PdfDocument formPage = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddTextField(0, "name", 10, 10, 100, 20).Build());
        byte[] target = new PdfDocumentBuilder().Build();

        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedPage(linkedPages, 0).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedPage(formPage, 0));
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
        Assert.Equal(2, fields.Count);
        Assert.Equal(Assert.IsType<PdfIndirectReference>(fields[0]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(textAnnotations[0]).ObjectNumber);
        Assert.Equal(Assert.IsType<PdfIndirectReference>(fields[1]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(checkAnnotations[0]).ObjectNumber);
        Assert.Equal(pageReferences[1].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(textWidget[Name("P")]).ObjectNumber);
        Assert.Equal(pageReferences[2].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(checkWidget[Name("P")]).ObjectNumber);
        Assert.IsType<PdfDictionary>(textWidget[Name("AP")]);
        Assert.IsType<PdfDictionary>(checkWidget[Name("AP")]);
    }

    [Fact]
    public void Build_MergesAcroFormsAndRejectsFieldNameCollisions()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddCheckBox(0, "source", 20, 20, 18, 18).Build());
        byte[] targetWithForm = new PdfDocumentBuilder()
            .AddBlankPage().AddCheckBox(0, "target", 20, 20, 18, 18).Build();
        byte[] emptyTarget = new PdfDocumentBuilder().Build();

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(targetWithForm))
                .AddImportedDocument(source).Build());
        PdfDictionary catalog = ResolveDictionary(merged, merged.Trailer[Name("Root")]);
        PdfDictionary form = DictionaryValue(merged, catalog[Name("AcroForm")]);
        Assert.Equal(2, Assert.IsType<PdfArray>(form[Name("Fields")]).Count);

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

        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(emptyTarget))
                .AddImportedDocument(source).RemovePage(0).Build());

        PdfDocument collision = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddCheckBox(0, "target", 20, 20, 18, 18).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(targetWithForm))
                .AddImportedDocument(collision).Build());
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
    public void Build_RenamesEscapedAcroFormDefaultResourceNames()
    {
        PdfDocument source = PdfDocument.Open(BuildEscapedAcroFormResourceDocument());
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
        byte[] target = BuildHierarchicalAcroFormDocument("target", 1, 1);
        PdfDocument source = PdfDocument.Open(
            BuildHierarchicalAcroFormDocument("source", 2, 2));

        PdfDocument merged = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
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
        Assert.Equal([targetChildReference.ObjectNumber, sourceChildReference.ObjectNumber],
            calculationOrder.Select(value =>
                Assert.IsType<PdfIndirectReference>(value).ObjectNumber));
        Assert.Equal(2, Assert.IsType<PdfInteger>(sourceChild[Name("Q")]).Value);
    }

    [Fact]
    public void Build_MergesNamedDestinationsAndKeepsImportedNamedLinksValid()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(200, 300).AddBlankPage(300, 400)
            .AddNamedDestination("source-target", 1)
            .AddNamedDestinationLink(0, 10, 10, 50, 20, "source-target")
            .Build());
        byte[] target = new PdfDocumentBuilder()
            .AddBlankPage(100, 100)
            .AddNamedDestination("target-start", 0)
            .AddAttachment("target.txt", "target"u8.ToArray(), "text/plain")
            .Build();

        PdfDocument reopened = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(source)
                .Build());
        (_, PdfIndirectReference[] pageReferences, PdfDictionary[] pages) = FlatPages(reopened);
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary names = Assert.IsType<PdfDictionary>(catalog[Name("Names")]);
        PdfDictionary destinations = Assert.IsType<PdfDictionary>(names[Name("Dests")]);
        PdfArray destinationNames = Assert.IsType<PdfArray>(destinations[Name("Names")]);
        var values = Enumerable.Range(0, destinationNames.Count / 2).ToDictionary(
            index => DecodeUnicode(Assert.IsType<PdfString>(destinationNames[index * 2])),
            index => Assert.IsType<PdfArray>(destinationNames[index * 2 + 1]),
            StringComparer.Ordinal);
        PdfDictionary importedLink = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[1][Name("Annots")])[0]);

        Assert.True(names.ContainsKey(Name("EmbeddedFiles")));
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
    public void Build_PreservesLegacyNamedDestinationsDuringCompleteImports()
    {
        PdfDocument source = PdfDocument.Open(BuildLegacyDestinationDocument());
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
        Assert.Equal("chapter", Assert.IsType<PdfName>(link[Name("Dest")]).ValueAsLatin1());
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

        Assert.Equal("Second page", DecodeUnicode(Assert.IsType<PdfString>(first[Name("Title")])));
        Assert.Equal(pageReferences[2].ObjectNumber,
            Assert.IsType<PdfIndirectReference>(destination[0]).ObjectNumber);
        Assert.Equal("UseOutlines",
            Assert.IsType<PdfName>(catalog[Name("PageMode")]).ValueAsLatin1());
    }

    [Fact]
    public void Build_MergesBookmarkTreesFromTheTargetAndMultipleSources()
    {
        byte[] target = new PdfDocumentBuilder()
            .AddBlankPage().AddBookmark("Target", 0).Build();
        PdfDocument firstSource = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddBookmark("First source", 0)
            .AddBookmark("First source child", 1, 1)
            .Build());
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
        Assert.All(items, item => Assert.Equal(rootReference.ObjectNumber,
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
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedPage(source, 0)
                .Build());

        PdfDocument collision = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddAttachment("target.txt", ReadOnlyMemory<byte>.Empty).Build());
        Assert.Throws<NotSupportedException>(() =>
            new PdfIncrementalPageEditor(PdfDocument.Open(target))
                .AddImportedDocument(collision)
                .Build());
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
        AssertLabel(ranges[1].Label, "r", null, 1);
        AssertLabel(ranges[2].Label, "D", "T-", 2);

        PdfDocument split = PdfDocument.Open(
            new PdfIncrementalPageEditor(PdfDocument.Open(new PdfDocumentBuilder().Build()))
                .AddImportedPage(source, 1)
                .Build());
        (long PageIndex, PdfDictionary Label)[] splitRanges = PageLabelRanges(split);
        Assert.Single(splitRanges);
        Assert.Equal(0, splitRanges[0].PageIndex);
        AssertLabel(splitRanges[0].Label, "r", null, 2);
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

    private static byte[] BuildTaggedDocument()
    {
        static PdfContentStreamBuilder TaggedPage() => new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Figure, 0)
            .Rectangle(10, 10, 20, 20).Fill()
            .EndMarkedContent();

        return new PdfDocumentBuilder()
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
                alternateDescription: "Second square")
            .Build();
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
        byte[] result = new byte[128];
        BinaryPrimitives.WriteUInt32BigEndian(result, 128);
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

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfDictionary DictionaryValue(PdfDocument document, PdfObject value) =>
        value is PdfIndirectReference ? ResolveDictionary(document, value) : Assert.IsType<PdfDictionary>(value);
    private static PdfStream ResolveStream(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfStream>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
