using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfStructureTests
{
    [Fact]
    public void Build_WritesNestedStructureTreeAndParentTreeMappings()
    {
        var content = new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Paragraph, 0)
            .BeginText().SetFont(PdfStandardFont.Helvetica, 12)
            .ShowLatin1Text("KillerPDF").EndText()
            .EndMarkedContent()
            .BeginMarkedContent(PdfStructureType.Figure, 2)
            .Rectangle(10, 10, 20, 20).Fill()
            .EndMarkedContent();
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata { Language = "en-US" })
            .AddPage(200, 300, content)
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Paragraph, 0, 0, 1,
                actualText: "KillerPDF")
            .AddStructureElement(PdfStructureType.Figure, 0, 2, 1,
                alternateDescription: "A square")
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary markInfo = Assert.IsType<PdfDictionary>(catalog[Name("MarkInfo")]);
        PdfDictionary structureRoot = ResolveDictionary(document, catalog[Name("StructTreeRoot")]);
        PdfDictionary parentTree = ResolveDictionary(document, structureRoot[Name("ParentTree")]);
        PdfArray topLevel = Assert.IsType<PdfArray>(structureRoot[Name("K")]);
        PdfDictionary documentElement = ResolveDictionary(document, topLevel[0]);
        PdfArray documentKids = Assert.IsType<PdfArray>(documentElement[Name("K")]);
        PdfIndirectReference paragraphReference = Assert.IsType<PdfIndirectReference>(documentKids[0]);
        PdfIndirectReference figureReference = Assert.IsType<PdfIndirectReference>(documentKids[1]);
        PdfDictionary paragraph = ResolveDictionary(document, paragraphReference);
        PdfDictionary figure = ResolveDictionary(document, figureReference);
        PdfArray parentNumbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);
        PdfArray mappings = Assert.IsType<PdfArray>(parentNumbers[1]);
        PdfDictionary pages = ResolveDictionary(document, catalog[Name("Pages")]);
        PdfDictionary page = ResolveDictionary(document, Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);

        Assert.True(Assert.IsType<PdfBoolean>(markInfo[Name("Marked")]).Value);
        Assert.Equal("Document", Assert.IsType<PdfName>(
            documentElement[Name("S")]).ValueAsLatin1());
        Assert.Equal(0, Assert.IsType<PdfInteger>(paragraph[Name("K")]).Value);
        Assert.Equal(2, Assert.IsType<PdfInteger>(figure[Name("K")]).Value);
        Assert.Equal("KillerPDF", DecodeUnicode(
            Assert.IsType<PdfString>(paragraph[Name("ActualText")])));
        Assert.Equal("A square", DecodeUnicode(
            Assert.IsType<PdfString>(figure[Name("Alt")])));
        Assert.Equal(0, Assert.IsType<PdfInteger>(page[Name("StructParents")]).Value);
        Assert.Equal(0, Assert.IsType<PdfInteger>(parentNumbers[0]).Value);
        Assert.Equal(3, mappings.Count);
        Assert.Equal(paragraphReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(mappings[0]).ObjectNumber);
        Assert.IsType<PdfNull>(mappings[1]);
        Assert.Equal(figureReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(mappings[2]).ObjectNumber);
        Assert.Equal(1, Assert.IsType<PdfInteger>(
            structureRoot[Name("ParentTreeNextKey")]).Value);
    }

    [Fact]
    public void Build_RejectsUnregisteredOrUnknownMarkedContent()
    {
        var content = new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Paragraph, 4)
            .EndMarkedContent();
        var builder = new PdfDocumentBuilder().AddPage(100, 100, content);

        Assert.Throws<ArgumentException>(() => builder.AddStructureElement(
            PdfStructureType.Paragraph, 0, 3));
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_AssignsIndependentParentTreeKeysAcrossPages()
    {
        PdfContentStreamBuilder FirstPage() => new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Paragraph, 0)
            .Rectangle(0, 0, 10, 10).Fill().EndMarkedContent();
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(100, 100, FirstPage())
            .AddPage(100, 100, FirstPage())
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Paragraph, 0, 0, 1)
            .AddStructureElement(PdfStructureType.Paragraph, 1, 0, 1)
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary pages = ResolveDictionary(document, catalog[Name("Pages")]);
        PdfDictionary[] pageDictionaries = Assert.IsType<PdfArray>(pages[Name("Kids")])
            .Select(value => ResolveDictionary(document, value)).ToArray();
        PdfDictionary root = ResolveDictionary(document, catalog[Name("StructTreeRoot")]);
        PdfDictionary parentTree = ResolveDictionary(document, root[Name("ParentTree")]);
        PdfArray numbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);

        Assert.Equal([0L, 1L], pageDictionaries.Select(page =>
            Assert.IsType<PdfInteger>(page[Name("StructParents")]).Value));
        Assert.Equal([0L, 1L], new[] { numbers[0], numbers[2] }
            .Select(value => Assert.IsType<PdfInteger>(value).Value));
        Assert.All(new[] { numbers[1], numbers[3] }, value =>
            Assert.Single(Assert.IsType<PdfArray>(value)));
    }

    [Fact]
    public void PdfUa2Mode_WritesIdentificationNamespaceAndViewerPreferences()
    {
        var content = new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Figure, 0)
            .Rectangle(10, 10, 20, 20).Fill()
            .EndMarkedContent();
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Accessible document",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddPage(100, 100, content)
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
                alternateDescription: "A square")
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary viewerPreferences = Assert.IsType<PdfDictionary>(
            catalog[Name("ViewerPreferences")]);
        PdfDictionary root = ResolveDictionary(document, catalog[Name("StructTreeRoot")]);
        PdfDictionary documentElement = ResolveDictionary(document,
            Assert.IsType<PdfArray>(root[Name("K")])[0]);
        PdfIndirectReference namespaceReference = Assert.IsType<PdfIndirectReference>(
            documentElement[Name("NS")]);
        PdfDictionary structureNamespace = ResolveDictionary(document, namespaceReference);
        PdfStream metadata = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(catalog[Name("Metadata")])));
        string xmp = Encoding.UTF8.GetString(metadata.EncodedData.Span);

        Assert.True(Assert.IsType<PdfBoolean>(
            viewerPreferences[Name("DisplayDocTitle")]).Value);
        Assert.Equal("http://iso.org/pdf2/ssn", Encoding.Latin1.GetString(
            Assert.IsType<PdfString>(structureNamespace[Name("NS")]).Bytes.Span));
        Assert.Contains("pdfuaid:part", xmp, StringComparison.Ordinal);
        Assert.Contains(">2<", xmp, StringComparison.Ordinal);
        Assert.Contains("pdfuaid:rev", xmp, StringComparison.Ordinal);
        Assert.Contains(">2024<", xmp, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfUa2Mode_RejectsIncompleteAccessibilityClaims()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PdfDocumentBuilder().EnablePdfUa2Conformance().Build());

        var untagged = new PdfContentStreamBuilder().Rectangle(0, 0, 10, 10).Fill();
        var builder = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Accessible document",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddPage(100, 100, untagged)
            .AddStructureContainer(PdfStructureType.Document);
        Assert.Throws<InvalidOperationException>(() => builder.Build());

        var text = new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Paragraph, 0)
            .BeginText().SetFont(PdfStandardFont.Helvetica, 12)
            .ShowLatin1Text("Not embedded").EndText().EndMarkedContent();
        var legacyFont = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "Accessible document",
                Language = "en-US"
            })
            .EnablePdfUa2Conformance()
            .AddPage(100, 100, text)
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Paragraph, 0, 0, 1);
        Assert.Throws<InvalidOperationException>(() => legacyFont.Build());
    }

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
