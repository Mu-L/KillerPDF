using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Security;
using KillerPdf.Engine.Tests.Fonts;
using KillerPdf.Engine.Writing;
using Xunit;

namespace KillerPdf.Engine.Tests.Editing;

public sealed class PdfIncrementalAnnotationEditorTests
{
    [Fact]
    public void Build_RejectsExhaustedStructureParentKeySpace()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Exhausted parent keys",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddBlankPage()
            .AddStructureContainer(PdfStructureType.Document)
            .Build());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        var setup = new PdfIncrementalUpdateBuilder(source);
        var rootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        rootEntries[Name("ParentTreeNextKey")] = new PdfInteger(long.MaxValue);
        setup.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(rootEntries));
        PdfDocument exhausted = PdfDocument.Open(setup.Build());

        Assert.Throws<OverflowException>(() =>
            new PdfIncrementalAnnotationEditor(exhausted)
                .AddTextNote(0, 20, 20, "Cannot allocate")
                .Build());
    }

    [Fact]
    public void Build_RejectsNegativeStructureParentTreeKey()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddStructureContainer(PdfStructureType.Document)
            .Build());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        var setup = new PdfIncrementalUpdateBuilder(source);
        var rootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        rootEntries[Name("ParentTree")] = new PdfDictionary([
            new(Name("Nums"), new PdfArray([new PdfInteger(-1), PdfNull.Instance]))
        ]);
        setup.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(rootEntries));
        PdfDocument malformed = PdfDocument.Open(setup.Build());

        Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "Cannot allocate")
                .Build());

        var scalarSetup = new PdfIncrementalUpdateBuilder(source);
        var scalarRootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        scalarRootEntries[Name("ParentTree")] = new PdfDictionary([
            new(Name("Nums"), new PdfArray([
                new PdfInteger(0), new PdfInteger(17)
            ]))
        ]);
        scalarSetup.ReplaceObject(rootReference.ObjectNumber,
            new PdfDictionary(scalarRootEntries));
        PdfDocument scalarValue = PdfDocument.Open(scalarSetup.Build());
        InvalidOperationException scalarError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(scalarValue)
                .AddTextNote(0, 20, 20, "Cannot retain scalar mapping")
                .Build());
        Assert.Contains("is not a structure element or array",
            scalarError.Message, StringComparison.Ordinal);

        PdfObject documentValue = root[Name("K")];
        if (documentValue is PdfArray documentArray)
            documentValue = documentArray[0];
        PdfIndirectReference documentReference = Assert.IsType<PdfIndirectReference>(
            documentValue);
        PdfDictionary documentElement = ResolveDictionary(source, documentReference);
        var staleKidsEntries = documentElement.ToDictionary(
            entry => entry.Key, entry => entry.Value);
        staleKidsEntries[Name("K")] = new PdfIndirectReference(999, 0);
        PdfDocument staleKids = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(documentReference.ObjectNumber,
                new PdfDictionary(staleKidsEntries))
            .Build());
        InvalidOperationException staleKidsError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(staleKids)
                .AddTextNote(0, 20, 20, "Cannot retain stale child")
                .Build());
        Assert.Contains("/K value contains a stale indirect reference",
            staleKidsError.Message, StringComparison.Ordinal);

        var emptyKidEntries = documentElement.ToDictionary(
            entry => entry.Key, entry => entry.Value);
        emptyKidEntries[Name("K")] = new PdfDictionary([]);
        PdfDocument emptyKid = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(documentReference.ObjectNumber,
                new PdfDictionary(emptyKidEntries))
            .Build());
        InvalidOperationException emptyKidError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(emptyKid)
                .AddTextNote(0, 20, 20, "Cannot retain empty child")
                .Build());
        Assert.Contains("/K value contains an invalid child",
            emptyKidError.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void Build_NormalizesDirectStructureTreeRootForTaggedAnnotation()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Direct structure root",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddBlankPage()
            .AddTextNote(0, 10, 10, "Existing note")
            .AddStructureContainer(PdfStructureType.Document)
            .Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            source.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(source, catalogReference);
        PdfDictionary root = ResolveDictionary(source, catalog[Name("StructTreeRoot")]);
        PdfObject documentValue = Assert.IsType<PdfArray>(root[Name("K")])[0];
        PdfDictionary documentElement = ResolveDictionary(source, documentValue);
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfObject existingDocumentKids = documentElement[Name("K")];
        PdfIndirectReference documentKids = setup.AddObject(
            existingDocumentKids is PdfArray existingArray
                ? existingArray : new PdfArray([existingDocumentKids]));
        var documentEntries = documentElement.ToDictionary(
            entry => entry.Key, entry => entry.Value);
        documentEntries[Name("K")] = documentKids;
        documentElement = new PdfDictionary(documentEntries);
        PdfIndirectReference directKids = setup.AddObject(
            new PdfArray([documentElement]));
        var directRootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        directRootEntries[Name("K")] = directKids;
        root = new PdfDictionary(directRootEntries);
        setup.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog
            .Where(entry => !entry.Key.Equals(Name("StructTreeRoot")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("StructTreeRoot"), root))));
        PdfDocument direct = PdfDocument.Open(setup.Build());

        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(direct)
            .AddTextNote(0, 20, 20, "Accessible note")
            .Build());
        PdfDictionary reopenedCatalog = ResolveDictionary(
            reopened, reopened.Trailer[Name("Root")]);
        PdfIndirectReference reopenedRootReference = Assert.IsType<PdfIndirectReference>(
            reopenedCatalog[Name("StructTreeRoot")]);
        PdfDictionary reopenedRoot = ResolveDictionary(reopened, reopenedRootReference);
        PdfDictionary reopenedDocument = ResolveDictionary(
            reopened, reopenedRoot[Name("K")]);
        PdfIndirectReference reopenedDocumentReference = Assert.IsType<PdfIndirectReference>(
            reopenedRoot[Name("K")]);
        PdfArray parentNumbers = Assert.IsType<PdfArray>(ResolveDictionary(
            reopened, reopenedRoot[Name("ParentTree")])[Name("Nums")]);

        PdfArray reopenedDocumentKids = Assert.IsType<PdfArray>(
            reopenedDocument[Name("K")]);
        Assert.Equal(2, reopenedDocumentKids.Count);
        Assert.All(reopenedDocumentKids, child => Assert.Equal(
            reopenedDocumentReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(
                ResolveDictionary(reopened, child)[Name("P")]).ObjectNumber));
        Assert.Contains(parentNumbers, item => item is PdfInteger integer && integer.Value == 1);
        Assert.Equal(2, Assert.IsType<PdfInteger>(
            reopenedRoot[Name("ParentTreeNextKey")]).Value);
    }

    [Fact]
    public void Build_PreservesEncryptedPdfUaStructureWithoutLeakingText()
    {
        byte[] source = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Encrypted accessible annotations",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "user",
                OwnerPassword = "owner",
                AllowAccessibilityExtraction = true,
                AllowAnnotationModification = true
            })
            .AddBlankPage()
            .AddTextNote(0, 72, 700, "Existing encrypted note")
            .AddStructureContainer(PdfStructureType.Document)
            .Build();
        const string appendedText = "Confidential accessible highlight";
        byte[] output = new PdfIncrementalAnnotationEditor(
                PdfDocument.Open(source, "owner"))
            .AddHighlight(0, 72, 650, 160, 20, appendedText)
            .Build(new PdfIncrementalUpdateWriteOptions
            {
                CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
                UseObjectStreams = true,
                CompressObjectStreams = true,
                CompressCrossReferenceStream = true
            });
        PdfDocument reopened = PdfDocument.Open(output, "owner");
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary structureRoot = ResolveDictionary(reopened, catalog[Name("StructTreeRoot")]);
        PdfDictionary parentTree = ResolveDictionary(reopened, structureRoot[Name("ParentTree")]);

        Assert.Equal(4, Assert.IsType<PdfArray>(parentTree[Name("Nums")]).Count);
        Assert.Equal(2, Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]).Count);
        Assert.True(output.AsSpan(0, source.Length).SequenceEqual(source));
        Assert.DoesNotContain(appendedText, Encoding.Latin1.GetString(output));
    }

    [Fact]
    public void Build_PreservesTaggedStructureAndAssociatesNewAnnotation()
    {
        byte[] source = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Accessible incremental annotations",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddBlankPage()
            .AddTextNote(0, 72, 700, "Existing review note")
            .AddStructureContainer(PdfStructureType.Document)
            .Build();
        PdfDocument initial = PdfDocument.Open(source);
        PdfDictionary initialCatalog = ResolveDictionary(initial, initial.Trailer[Name("Root")]);
        PdfIndirectReference initialRootReference = Assert.IsType<PdfIndirectReference>(
            initialCatalog[Name("StructTreeRoot")]);
        PdfDictionary initialRoot = ResolveDictionary(initial, initialRootReference);
        PdfArray initialNamespaces = Assert.IsType<PdfArray>(initialRoot[Name("Namespaces")]);
        var setup = new PdfIncrementalUpdateBuilder(initial);
        PdfIndirectReference pdf2NamespaceReference = Assert.IsType<PdfIndirectReference>(
            initialNamespaces[0]);
        PdfDictionary pdf2Namespace = ResolveDictionary(initial, pdf2NamespaceReference);
        PdfIndirectReference indirectPdf2Uri = setup.AddObject(new PdfString(
            [0xEF, 0xBB, 0xBF, .. "http://iso.org/pdf2/ssn"u8.ToArray()],
            PdfStringForm.Hexadecimal));
        PdfIndirectReference indirectPdf2UriAlias = setup.AddObject(indirectPdf2Uri);
        setup.ReplaceObject(pdf2NamespaceReference.ObjectNumber,
            new PdfDictionary(pdf2Namespace
                .Where(entry => !entry.Key.Equals(Name("NS")))
                .Append(new KeyValuePair<PdfName, PdfObject>(
                    Name("NS"), indirectPdf2UriAlias))));
        PdfIndirectReference customNamespace = setup.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Namespace")),
            new(Name("NS"), new PdfString(
                Encoding.ASCII.GetBytes("https://example.test/custom-structure"),
                PdfStringForm.Literal))
        ]));
        var alteredRoot = initialRoot.ToDictionary(entry => entry.Key, entry => entry.Value);
        PdfIndirectReference namespaceArray = setup.AddObject(new PdfArray(
            [customNamespace, .. initialNamespaces]));
        PdfIndirectReference namespaceArrayAlias = setup.AddObject(namespaceArray);
        alteredRoot[Name("Namespaces")] = namespaceArrayAlias;
        PdfIndirectReference nextKey = setup.AddObject(new PdfInteger(0));
        alteredRoot[Name("ParentTreeNextKey")] = setup.AddObject(nextKey);
        setup.ReplaceObject(initialRootReference.ObjectNumber, new PdfDictionary(alteredRoot));
        source = setup.Build();
        byte[] output = new PdfIncrementalAnnotationEditor(
                PdfDocument.Open(source))
            .AddHighlight(0, 72, 650, 160, 20, "New review highlight")
            .Build();
        PdfDocument document = PdfDocument.Open(output);
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary structureRoot = ResolveDictionary(document, catalog[Name("StructTreeRoot")]);
        PdfDictionary parentTree = ResolveDictionary(document, structureRoot[Name("ParentTree")]);
        PdfArray numbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(document)[0].Page[Name("Annots")]);

        Assert.Equal(2, annotations.Count);
        Assert.All(annotations, value => Assert.True(
            ResolveDictionary(document, value).ContainsKey(Name("StructParent"))));
        Assert.Equal(4, numbers.Count);
        Assert.Equal(0, Assert.IsType<PdfInteger>(numbers[0]).Value);
        Assert.Equal(1, Assert.IsType<PdfInteger>(numbers[2]).Value);
        Assert.All(new[] { numbers[1], numbers[3] }, value => Assert.Equal("Annot",
            Assert.IsType<PdfName>(ResolveDictionary(document, value)[Name("S")]).ValueAsLatin1()));
        PdfDictionary appendedElement = ResolveDictionary(document, numbers[3]);
        PdfDictionary selectedNamespace = ResolveDictionary(document, appendedElement[Name("NS")]);
        PdfObject selectedUriValue = selectedNamespace[Name("NS")];
        while (selectedUriValue is PdfIndirectReference selectedUriReference)
            selectedUriValue = document.Resolve(selectedUriReference);
        PdfString selectedUri = Assert.IsType<PdfString>(selectedUriValue);
        Assert.True(selectedUri.Bytes.Span.StartsWith(
            new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal("http://iso.org/pdf2/ssn", Encoding.UTF8.GetString(
            selectedUri.Bytes.Span[3..]));
        Assert.True(output.AsSpan(0, source.Length).SequenceEqual(source));
    }

    [Fact]
    public void Build_RejectsDirectStructureNamespaceEntries()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Malformed namespace",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddBlankPage()
            .AddStructureContainer(PdfStructureType.Document)
            .Build());
        PdfDictionary catalog = ResolveDictionary(source, source.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("StructTreeRoot")]);
        PdfDictionary root = ResolveDictionary(source, rootReference);
        var rootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        rootEntries[Name("Namespaces")] = new PdfArray([new PdfDictionary([
            new(Name("Type"), Name("Namespace")),
            new(Name("NS"), new PdfString(
                "http://iso.org/pdf2/ssn"u8, PdfStringForm.Literal))
        ])]);
        PdfDocument malformed = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(rootEntries))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "Accessible note")
                .Build());

        Assert.Contains("structure namespace is not an indirect reference",
            error.Message, StringComparison.Ordinal);

        PdfArray originalNamespaces = Assert.IsType<PdfArray>(root[Name("Namespaces")]);
        PdfIndirectReference namespaceReference = Assert.IsType<PdfIndirectReference>(
            originalNamespaces[0]);
        PdfDocument untyped = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(namespaceReference.ObjectNumber, new PdfDictionary([
                new(Name("NS"), new PdfString(
                    "http://iso.org/pdf2/ssn"u8, PdfStringForm.Literal))
            ]))
            .Build());
        InvalidOperationException typeError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(untyped)
                .AddTextNote(0, 20, 20, "Accessible note")
                .Build());
        Assert.Contains("structure namespace has no /Type /Namespace value",
            typeError.Message, StringComparison.Ordinal);

        var duplicateSetup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference duplicateNamespace = duplicateSetup.AddObject(
            ResolveDictionary(source, namespaceReference));
        var duplicateRoot = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        duplicateRoot[Name("Namespaces")] = new PdfArray([
            namespaceReference, duplicateNamespace
        ]);
        duplicateSetup.ReplaceObject(rootReference.ObjectNumber,
            new PdfDictionary(duplicateRoot));
        PdfDocument duplicated = PdfDocument.Open(duplicateSetup.Build());
        InvalidOperationException duplicateError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(duplicated)
                .AddTextNote(0, 20, 20, "Accessible note")
                .Build());
        Assert.Contains("contains duplicate PDF 2.0 namespaces",
            duplicateError.Message, StringComparison.Ordinal);

        PdfObject topLevelValue = root[Name("K")];
        if (topLevelValue is PdfArray topLevelArray)
            topLevelValue = topLevelArray[0];
        PdfIndirectReference topLevelReference = Assert.IsType<PdfIndirectReference>(
            topLevelValue);
        PdfDictionary topLevel = ResolveDictionary(source, topLevelReference);
        PdfDocument mistypedRole = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(topLevelReference.ObjectNumber,
                new PdfDictionary(topLevel
                    .Where(entry => !entry.Key.Equals(Name("S")))
                    .Append(new KeyValuePair<PdfName, PdfObject>(
                        Name("S"), new PdfInteger(17)))))
            .Build());
        InvalidOperationException roleError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(mistypedRole)
                .AddTextNote(0, 20, 20, "Accessible note")
                .Build());
        Assert.Contains("top-level structure element /S value is not a name",
            roleError.Message, StringComparison.Ordinal);

        PdfDocument wrongParent = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(topLevelReference.ObjectNumber,
                new PdfDictionary(topLevel
                    .Where(entry => !entry.Key.Equals(Name("P")))
                    .Append(new KeyValuePair<PdfName, PdfObject>(
                        Name("P"), topLevelReference))))
            .Build());
        InvalidOperationException parentError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(wrongParent)
                .AddTextNote(0, 20, 20, "Accessible note")
                .Build());
        Assert.Contains("no reciprocal /P link to the structure-tree root",
            parentError.Message, StringComparison.Ordinal);

        var mistypedRootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        mistypedRootEntries[Name("Type")] = Name("Unexpected");
        PdfDocument mistypedRoot = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(rootReference.ObjectNumber,
                new PdfDictionary(mistypedRootEntries))
            .Build());
        InvalidOperationException rootTypeError = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(mistypedRoot)
                .AddTextNote(0, 20, 20, "Accessible note")
                .Build());
        Assert.Contains("structure-tree root has an invalid /Type value",
            rootTypeError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsUnpairedSurrogateInAnnotationText()
    {
        PdfDocument document = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());
        var editor = new PdfIncrementalAnnotationEditor(document)
            .AddTextNote(0, 10, 10, "bad\uD800text");

        Assert.Throws<ArgumentException>(() => editor.Build());
    }

    [Fact]
    public void Build_RejectsStaleExistingPageAnnotations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        PdfDictionary stalePage = new(page.Page.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                new PdfArray([new PdfIndirectReference(999, 0)]))));
        PdfDocument malformed = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(page.Reference.ObjectNumber, stalePage)
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("/Annots contains a stale or non-dictionary entry",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDirectExistingPageAnnotations()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var annotation = new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ]))
        ]);
        PdfDocument malformed = PdfDocument.Open(new PdfIncrementalUpdateBuilder(source)
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([annotation])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("/Annots contains a direct annotation entry",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDuplicateExistingPageAnnotationReferences()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference annotation = setup.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), page.Reference)
        ]));
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([annotation, annotation])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("/Annots contains a duplicate annotation reference",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDuplicateExistingPageAnnotationNames()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfDictionary Annotation() => new([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), page.Reference),
            new(Name("NM"), new PdfString("duplicate"u8, PdfStringForm.Literal))
        ]);
        PdfIndirectReference first = setup.AddObject(Annotation());
        PdfIndirectReference second = setup.AddObject(Annotation());
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([first, second])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("/Annots contains duplicate /NM values",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsGeneratedAnnotationNameCollision()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference existing = setup.ReserveObject();
        string collidingName = $"KillerPDF-Note-{existing.ObjectNumber + 1}";
        setup.SetObject(existing, new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), page.Reference),
            new(Name("NM"), new PdfString(
                Encoding.Latin1.GetBytes(collidingName), PdfStringForm.Literal))
        ]));
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([existing])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains($"already contains annotation /NM value '{collidingName}'",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedExistingAnnotationText()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference existing = setup.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), page.Reference),
            new(Name("Contents"), new PdfString(
                new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0x28 },
                PdfStringForm.Hexadecimal))
        ]));
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([existing])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("annotation /Contents value contains malformed UTF-8 text",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedExistingAnnotationCommonFields()
    {
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("M"),
                new PdfString("D:20250230"u8, PdfStringForm.Literal)),
            "annotation /M value is not a valid PDF date");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("F"),
                new PdfInteger(-1)),
            "annotation /F value is not a nonnegative integer");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("CA"),
                new PdfReal(1.1)),
            "annotation /CA value is not a number from 0 through 1");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("C"),
                new PdfArray([new PdfReal(1.1)])),
            "annotation /C value is not a valid color array");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("Border"),
                new PdfArray([
                    new PdfInteger(0), new PdfInteger(0), new PdfInteger(-1)
                ])),
            "annotation /Border value has invalid radii or width");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("BS"),
                new PdfDictionary([new(Name("S"), Name("Unexpected"))])),
            "annotation /BS /S value /Unexpected is not defined");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("QuadPoints"),
                new PdfArray([new PdfInteger(0), new PdfInteger(0)])),
            "annotation /QuadPoints value is not a nonempty sequence");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("Lang"),
                new PdfString("not_valid"u8, PdfStringForm.Literal)),
            "annotation /Lang value is not a valid BCP 47 language tag");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("RT"),
                Name("Unexpected")),
            "annotation /RT value /Unexpected is not defined");
        AssertInvalid(new KeyValuePair<PdfName, PdfObject>(Name("State"),
                new PdfString("Unexpected"u8, PdfStringForm.Literal)),
            "annotation /State and /StateModel must both be strings");

        static void AssertInvalid(
            KeyValuePair<PdfName, PdfObject> malformedEntry, string expectedMessage)
        {
            PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
                .AddBlankPage()
                .Build());
            (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
            var setup = new PdfIncrementalUpdateBuilder(source);
            PdfIndirectReference existing = setup.AddObject(new PdfDictionary(new[]
            {
                new KeyValuePair<PdfName, PdfObject>(Name("Type"), Name("Annot")),
                new KeyValuePair<PdfName, PdfObject>(Name("Subtype"), Name("Text")),
                new KeyValuePair<PdfName, PdfObject>(Name("Rect"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])),
                new KeyValuePair<PdfName, PdfObject>(Name("P"), page.Reference),
                malformedEntry
            }));
            PdfDocument malformed = PdfDocument.Open(setup
                .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([existing])))))
                .Build());

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                new PdfIncrementalAnnotationEditor(malformed)
                    .AddTextNote(0, 20, 20, "New note")
                    .Build());
            Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Build_RejectsExistingReplyTargetNotRegisteredOnPage()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference target = setup.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text"))
        ]));
        PdfIndirectReference reply = setup.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), page.Reference),
            new(Name("IRT"), target)
        ]));
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([reply])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("annotation /IRT target is not registered on the page",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsExistingPopupWithMismatchedParent()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference other = setup.ReserveObject();
        PdfIndirectReference popup = setup.ReserveObject();
        PdfIndirectReference markup = setup.ReserveObject();
        PdfDictionary Markup(PdfObject? popupValue = null)
        {
            var entries = new List<KeyValuePair<PdfName, PdfObject>>
            {
                new(Name("Type"), Name("Annot")),
                new(Name("Subtype"), Name("Text")),
                new(Name("Rect"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ])),
                new(Name("P"), page.Reference)
            };
            if (popupValue is not null)
                entries.Add(new KeyValuePair<PdfName, PdfObject>(Name("Popup"), popupValue));
            return new PdfDictionary(entries);
        }
        setup.SetObject(other, Markup());
        setup.SetObject(popup, new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Popup")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), page.Reference),
            new(Name("Parent"), other)
        ]));
        setup.SetObject(markup, Markup(popup));
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([markup, popup, other])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("annotation /Popup target does not link back through /Parent",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedExistingAnnotationAppearanceState()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference appearance = setup.AddObject(new PdfStream(
            new PdfDictionary([
                new(Name("Type"), Name("XObject")),
                new(Name("Subtype"), Name("Form")),
                new(Name("BBox"), new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    new PdfInteger(10), new PdfInteger(10)
                ]))
            ]), []));
        PdfIndirectReference existing = setup.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), page.Reference),
            new(Name("AS"), Name("Off")),
            new(Name("AP"), new PdfDictionary([
                new(Name("N"), new PdfDictionary([
                    new(Name("On"), appearance)
                ]))
            ]))
        ]));
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([existing])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("/AS value has no matching normal appearance state",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsMalformedExistingAnnotationAppearanceStream()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .Build());
        (PdfIndirectReference Reference, PdfDictionary Page) page = Pages(source)[0];
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference appearance = setup.AddObject(new PdfStream(
            new PdfDictionary([]), []));
        PdfIndirectReference existing = setup.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), page.Reference),
            new(Name("AP"), new PdfDictionary([new(Name("N"), appearance)]))
        ]));
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(page.Reference.ObjectNumber, new PdfDictionary(page.Page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                    new PdfArray([existing])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("appearance has no /Subtype /Form value",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DetachesSharedPageAnnotationArraysBeforeAppending()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .Build());
        IReadOnlyList<(PdfIndirectReference Reference, PdfDictionary Page)> pages = Pages(source);
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference sharedAnnotations = setup.AddObject(new PdfArray([]));
        foreach ((PdfIndirectReference reference, PdfDictionary page) in pages)
            setup.ReplaceObject(reference.ObjectNumber, new PdfDictionary(page.Append(
                new KeyValuePair<PdfName, PdfObject>(Name("Annots"), sharedAnnotations))));
        PdfDocument shared = PdfDocument.Open(setup.Build());

        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(shared)
            .AddTextNote(0, 20, 20, "Page one only")
            .Build());
        IReadOnlyList<(PdfIndirectReference Reference, PdfDictionary Page)> reopenedPages =
            Pages(reopened);
        PdfIndirectReference firstArrayReference = Assert.IsType<PdfIndirectReference>(
            reopenedPages[0].Page[Name("Annots")]);
        PdfIndirectReference secondArrayReference = Assert.IsType<PdfIndirectReference>(
            reopenedPages[1].Page[Name("Annots")]);

        Assert.NotEqual(firstArrayReference.ObjectNumber, secondArrayReference.ObjectNumber);
        Assert.Single(Assert.IsType<PdfArray>(reopened.Resolve(firstArrayReference)));
        Assert.Empty(Assert.IsType<PdfArray>(reopened.Resolve(secondArrayReference)));
    }

    [Fact]
    public void Build_RejectsExistingAnnotationOwnedByAnotherPage()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .Build());
        IReadOnlyList<(PdfIndirectReference Reference, PdfDictionary Page)> pages = Pages(source);
        var annotation = new PdfDictionary([
            new(Name("Type"), Name("Annot")),
            new(Name("Subtype"), Name("Text")),
            new(Name("Rect"), new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(10), new PdfInteger(10)
            ])),
            new(Name("P"), pages[1].Reference)
        ]);
        var setup = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference annotationReference = setup.AddObject(annotation);
        PdfDocument malformed = PdfDocument.Open(setup
            .ReplaceObject(pages[0].Reference.ObjectNumber,
                new PdfDictionary(pages[0].Page.Append(
                    new KeyValuePair<PdfName, PdfObject>(Name("Annots"),
                        new PdfArray([annotationReference])))))
            .Build());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalAnnotationEditor(malformed)
                .AddTextNote(0, 20, 20, "New note")
                .Build());

        Assert.Contains("/P value identifies another page",
            error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Build_HonorsAnnotationCertificationPermission(int permission, bool allowed)
    {
        var editor = new PdfIncrementalAnnotationEditor(
            PdfDocument.Open(CertifiedSource(permission)))
            .AddTextNote(0, 20, 700, "review");

        if (allowed)
            Assert.NotEmpty(editor.Build());
        else
            Assert.Throws<InvalidOperationException>(() => editor.Build());
    }

    [Fact]
    public void Build_CanEmitCompressedStructuralRevision()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();

        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(
                PdfDocument.Open(source))
            .AddTextNote(0, 72, 650, "Compressed note")
            .Build(new PdfIncrementalUpdateWriteOptions
            {
                CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
                UseObjectStreams = true,
                CompressObjectStreams = true,
                CompressCrossReferenceStream = true
            }));

        Assert.True(reopened.CrossReferences.Sections[0].IsStream);
        Assert.Contains(reopened.CrossReferences.Sections[0].Values,
            entry => entry.Type == PdfCrossReferenceEntryType.Compressed);
        Assert.Single(Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]));
    }

    [Fact]
    public void Build_CanEmitEncryptedCompressedStructuralRevision()
    {
        byte[] source = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "user", OwnerPassword = "owner"
            })
            .AddBlankPage().Build();

        byte[] bytes = new PdfIncrementalAnnotationEditor(
                PdfDocument.Open(source, "owner"))
            .AddTextNote(0, 72, 650, "Encrypted compressed note")
            .Build(new PdfIncrementalUpdateWriteOptions
            {
                CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
                UseObjectStreams = true,
                CompressObjectStreams = true,
                CompressCrossReferenceStream = true
            });
        PdfDocument reopened = PdfDocument.Open(bytes, "user");
        PdfDictionary note = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")])[0]);

        PdfString contents = Assert.IsType<PdfString>(note[Name("Contents")]);
        Assert.Equal("Encrypted compressed note",
            Encoding.BigEndianUnicode.GetString(contents.Bytes.Span[2..]));
        Assert.Equal(-1, bytes.AsSpan().IndexOf("Encrypted compressed note"u8));
        Assert.Contains(reopened.CrossReferences.Sections[0].Values,
            entry => entry.Type == PdfCrossReferenceEntryType.Compressed);
    }

    [Fact]
    public void Build_AppendsAnnotationsToTheSelectedExistingPage()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().AddBlankPage().Build();
        PdfDocument original = PdfDocument.Open(source);
        var editor = new PdfIncrementalAnnotationEditor(original);

        byte[] result = editor
            .AddTextNote(1, 72, 650, "Review résumé", open: true)
            .AddHighlight(1, 72, 600, 200, 20, "Important", opacity: 0.4)
            .Build();
        PdfDocument reopened = PdfDocument.Open(result);
        IReadOnlyList<(PdfIndirectReference Reference, PdfDictionary Page)> pages = Pages(reopened);
        var annotations = Assert.IsType<PdfArray>(pages[1].Page[Name("Annots")]);
        PdfDictionary note = ResolveDictionary(reopened, annotations[0]);
        PdfDictionary highlight = ResolveDictionary(reopened, annotations[1]);

        Assert.Equal(2, editor.PageCount);
        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
        Assert.False(pages[0].Page.ContainsKey(Name("Annots")));
        Assert.Equal("Text", Assert.IsType<PdfName>(note[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal("Highlight", Assert.IsType<PdfName>(highlight[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(pages[1].Reference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(note[Name("P")]).ObjectNumber);
        Assert.IsType<PdfStream>(reopened.Resolve(Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfDictionary>(highlight[Name("AP")])[Name("N")])));
    }

    [Fact]
    public void Build_PreservesExistingDirectAnnotationArray()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextNote(0, 20, 700, "Existing")
            .Build();
        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(source))
            .AddHighlight(0, 20, 650, 100, 15)
            .Build());
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);

        Assert.Equal(2, annotations.Count);
        Assert.Equal("Text", Assert.IsType<PdfName>(
            ResolveDictionary(reopened, annotations[0])[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal("Highlight", Assert.IsType<PdfName>(
            ResolveDictionary(reopened, annotations[1])[Name("Subtype")]).ValueAsLatin1());
    }

    [Fact]
    public void Build_DetachesAnExistingIndirectAnnotationArray()
    {
        byte[] initial = new PdfDocumentBuilder().AddBlankPage().Build();
        PdfDocument firstDocument = PdfDocument.Open(initial);
        (PdfIndirectReference pageReference, PdfDictionary page) = Pages(firstDocument)[0];
        var setup = new PdfIncrementalUpdateBuilder(firstDocument);
        PdfIndirectReference arrayReference = setup.AddObject(new PdfArray([]));
        PdfIndirectReference arrayAlias = setup.AddObject(arrayReference);
        setup.ReplaceObject(pageReference.ObjectNumber, Replace(
            page, Name("Annots"), arrayAlias));
        byte[] source = setup.Build();

        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(source))
            .AddTextNote(0, 30, 700, "Indirect array")
            .Build());
        PdfDictionary reopenedPage = Pages(reopened)[0].Page;
        PdfIndirectReference reopenedArrayReference = Assert.IsType<PdfIndirectReference>(
            reopenedPage[Name("Annots")]);
        PdfArray annotations = Assert.IsType<PdfArray>(reopened.Resolve(reopenedArrayReference));

        Assert.NotEqual(arrayReference.ObjectNumber, reopenedArrayReference.ObjectNumber);
        Assert.Empty(Assert.IsType<PdfArray>(reopened.Resolve(arrayReference)));
        Assert.Single(annotations);
        Assert.Equal(3, reopened.CrossReferences.Sections.Count);
    }

    [Fact]
    public void ArgumentsAndEmptyUpdates_AreRejected()
    {
        var editor = new PdfIncrementalAnnotationEditor(PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build()));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddTextNote(1, 0, 0, "bad"));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddHighlight(0, 0, 0, 10, 10, opacity: -1));
        Assert.Throws<InvalidOperationException>(() => editor.Build());
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        static byte[] Edit(PdfDocument document) => new PdfIncrementalAnnotationEditor(document)
            .AddTextNote(0, 20, 700, "Note")
            .AddHighlight(0, 20, 650, 100, 15)
            .Build();

        Assert.Equal(Edit(source), Edit(source));
    }

    [Theory]
    [InlineData("Underline")]
    [InlineData("StrikeOut")]
    [InlineData("Squiggly")]
    public void Build_AppendsEveryStandardTextMarkupType(string subtype)
    {
        var editor = new PdfIncrementalAnnotationEditor(PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build()));
        _ = subtype switch
        {
            "Underline" => editor.AddUnderline(0, 20, 600, 100, 15),
            "StrikeOut" => editor.AddStrikeOut(0, 20, 600, 100, 15),
            "Squiggly" => editor.AddSquiggly(0, 20, 600, 100, 15),
            _ => throw new ArgumentOutOfRangeException(nameof(subtype))
        };
        PdfDocument reopened = PdfDocument.Open(editor.Build());
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);
        PdfDictionary annotation = ResolveDictionary(reopened, annotations[0]);

        Assert.Equal(subtype, Assert.IsType<PdfName>(annotation[Name("Subtype")]).ValueAsLatin1());
        Assert.IsType<PdfStream>(reopened.Resolve(Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfDictionary>(annotation[Name("AP")])[Name("N")])));
    }

    [Fact]
    public void Build_AppendsEmbeddedFreeTextAndEveryVisualAnnotationType()
    {
        TrueTypeFont font = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(format12: false));
        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(
                new PdfDocumentBuilder().AddBlankPage().Build()))
            .AddFreeText(0, 20, 650, 160, 60, "A\nA", font, fillColor: PdfRgbColor.Yellow)
            .AddLine(0, new PdfPoint(20, 600), new PdfPoint(180, 570), lineWidth: 3)
            .AddRectangle(0, 20, 500, 70, 40, fillColor: PdfRgbColor.Yellow)
            .AddEllipse(0, 110, 500, 70, 40, fillColor: PdfRgbColor.Yellow)
            .AddInk(0,
            [
                [new PdfPoint(20, 450), new PdfPoint(50, 470)],
                [new PdfPoint(80, 450), new PdfPoint(110, 470)]
            ])
            .Build());
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);
        string[] subtypes = annotations.Select(value => Assert.IsType<PdfName>(
            ResolveDictionary(reopened, value)[Name("Subtype")]).ValueAsLatin1()).ToArray();
        PdfDictionary freeText = ResolveDictionary(reopened, annotations[0]);
        PdfStream freeTextAppearance = Assert.IsType<PdfStream>(reopened.Resolve(
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfDictionary>(freeText[Name("AP")])[Name("N")])));
        PdfDictionary fontResources = Assert.IsType<PdfDictionary>(
            Assert.IsType<PdfDictionary>(freeTextAppearance.Dictionary[Name("Resources")])[Name("Font")]);
        PdfDictionary type0 = ResolveDictionary(reopened, fontResources[Name("KpF1")]);
        PdfDictionary ink = ResolveDictionary(reopened, annotations[4]);

        Assert.Equal(["FreeText", "Line", "Square", "Circle", "Ink"], subtypes);
        Assert.Equal("Type0", Assert.IsType<PdfName>(type0[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(2, Assert.IsType<PdfArray>(ink[Name("InkList")]).Count);
    }

    [Fact]
    public void FreeText_DistinguishesBaseAndVariationSequenceSharingOneGlyph()
    {
        TrueTypeFont font = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(
            format12: false, cmap: TrueTypeFontTests.Cmap14()));
        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(
                new PdfDocumentBuilder().AddBlankPage().Build()))
            .AddFreeText(0, 20, 650, 160, 60, "AA\uFE0F", font)
            .Build());
        PdfDictionary annotation = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")])[0]);
        PdfStream appearance = Assert.IsType<PdfStream>(reopened.Resolve(
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfDictionary>(annotation[Name("AP")])[Name("N")])));
        PdfDictionary type0 = ResolveDictionary(reopened,
            Assert.IsType<PdfDictionary>(
                Assert.IsType<PdfDictionary>(appearance.Dictionary[Name("Resources")])[Name("Font")])
                [Name("KpF1")]);
        PdfStream toUnicode = Assert.IsType<PdfStream>(reopened.Resolve(
            Assert.IsType<PdfIndirectReference>(type0[Name("ToUnicode")])));

        Assert.Contains("<00010002> Tj", Encoding.ASCII.GetString(appearance.EncodedData.Span));
        Assert.Contains("<0001> <0041>", Encoding.ASCII.GetString(toUnicode.EncodedData.Span));
        Assert.Contains("<0002> <0041FE0F>", Encoding.ASCII.GetString(toUnicode.EncodedData.Span));
    }

    [Fact]
    public void VisualAnnotationArguments_AreRejectedBeforeWriting()
    {
        var editor = new PdfIncrementalAnnotationEditor(PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build()));
        Assert.Throws<ArgumentException>(() => editor.AddLine(
            0, new PdfPoint(1, 1), new PdfPoint(1, 1)));
        Assert.Throws<ArgumentException>(() => editor.AddInk(0, Array.Empty<PdfPoint>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddRectangle(
            0, 0, 0, 10, 10, lineWidth: 0));
    }

    [Fact]
    public void FreeTextAnnotations_ShareOneDeterministicEmbeddedFontSubset()
    {
        TrueTypeFont font = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(format12: false));
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] Build() => new PdfIncrementalAnnotationEditor(PdfDocument.Open(source))
            .AddFreeText(0, 20, 650, 100, 40, "A", font)
            .AddFreeText(0, 140, 650, 100, 40, "AA", font)
            .Build();

        byte[] first = Build();
        PdfDocument reopened = PdfDocument.Open(first);
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);
        PdfIndirectReference FontReference(PdfObject annotationReference)
        {
            PdfDictionary annotation = ResolveDictionary(reopened, annotationReference);
            PdfStream appearance = Assert.IsType<PdfStream>(reopened.Resolve(
                Assert.IsType<PdfIndirectReference>(
                    Assert.IsType<PdfDictionary>(annotation[Name("AP")])[Name("N")])));
            PdfDictionary fonts = Assert.IsType<PdfDictionary>(
                Assert.IsType<PdfDictionary>(appearance.Dictionary[Name("Resources")])[Name("Font")]);
            return Assert.IsType<PdfIndirectReference>(fonts[Name("KpF1")]);
        }

        Assert.Equal(FontReference(annotations[0]).ObjectNumber, FontReference(annotations[1]).ObjectNumber);
        Assert.Equal(first, Build());
    }

    [Fact]
    public void ImageStamps_ShareImageAndSoftMaskObjects()
    {
        PdfImage image = PdfImage.FromRgba(1, 1, new byte[] { 20, 40, 60, 96 });
        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(
                new PdfDocumentBuilder().AddBlankPage().Build()))
            .AddImageStamp(0, 20, 600, 100, 50, image)
            .AddImageStamp(0, 140, 600, 100, 50, image)
            .Build());
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);
        PdfIndirectReference ImageReference(PdfObject annotationReference)
        {
            PdfDictionary annotation = ResolveDictionary(reopened, annotationReference);
            PdfStream appearance = Assert.IsType<PdfStream>(reopened.Resolve(
                Assert.IsType<PdfIndirectReference>(
                    Assert.IsType<PdfDictionary>(annotation[Name("AP")])[Name("N")])));
            PdfDictionary xobjects = Assert.IsType<PdfDictionary>(
                Assert.IsType<PdfDictionary>(appearance.Dictionary[Name("Resources")])[Name("XObject")]);
            return Assert.IsType<PdfIndirectReference>(xobjects[Name("Im1")]);
        }
        PdfIndirectReference firstImage = ImageReference(annotations[0]);
        PdfIndirectReference secondImage = ImageReference(annotations[1]);
        PdfStream imageStream = Assert.IsType<PdfStream>(reopened.Resolve(firstImage));

        Assert.Equal(firstImage.ObjectNumber, secondImage.ObjectNumber);
        Assert.IsType<PdfIndirectReference>(imageStream.Dictionary[Name("SMask")]);
    }

    private static IReadOnlyList<(PdfIndirectReference Reference, PdfDictionary Page)> Pages(
        PdfDocument document)
    {
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary pages = ResolveDictionary(document, catalog[Name("Pages")]);
        return Assert.IsType<PdfArray>(pages[Name("Kids")]).Select(value =>
        {
            var reference = Assert.IsType<PdfIndirectReference>(value);
            return (reference, ResolveDictionary(document, reference));
        }).ToArray();
    }

    private static PdfDictionary Replace(PdfDictionary source, PdfName name, PdfObject value) =>
        new(source.Where(entry => !entry.Key.Equals(name))
            .Append(new KeyValuePair<PdfName, PdfObject>(name, value)));
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static byte[] CertifiedSource(int permission)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            document.Trailer[Name("Root")]);
        PdfDictionary catalog = ResolveDictionary(document, catalogReference);
        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference parameters = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("TransformParams")),
            new(Name("P"), new PdfInteger(permission)),
            new(Name("V"), Name("1.2"))
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
