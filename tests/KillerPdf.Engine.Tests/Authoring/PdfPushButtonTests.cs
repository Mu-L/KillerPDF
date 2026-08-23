using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfPushButtonTests
{
    [Fact]
    public void AddUriPushButton_WritesActionLabelFlagsMetadataAndAppearance()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddUriPushButton(0, "website", 20, 30, 140, 28,
                "Open KillerPDF", "https://killerpdf.com/docs",
                fieldMetadata: new PdfFormFieldMetadata
                {
                    Tooltip = "Open the documentation",
                    MappingName = "website_action"
                },
                fieldOptions: new PdfFormFieldOptions { ReadOnly = true, NoExport = true })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfDictionary action = Assert.IsType<PdfDictionary>(field[Name("A")]);
        PdfDictionary characteristics = Assert.IsType<PdfDictionary>(field[Name("MK")]);

        Assert.Equal("Btn", Assert.IsType<PdfName>(field[Name("FT")]).ValueAsLatin1());
        Assert.Equal((1 << 16) | 5, Assert.IsType<PdfInteger>(field[Name("Ff")]).Value);
        Assert.Equal("URI", Assert.IsType<PdfName>(action[Name("S")]).ValueAsLatin1());
        Assert.Equal("https://killerpdf.com/docs", DecodeUnicode(Assert.IsType<PdfString>(action[Name("URI")])));
        Assert.Equal("Open KillerPDF", DecodeUnicode(Assert.IsType<PdfString>(characteristics[Name("CA")])));
        Assert.Equal("Open the documentation", DecodeUnicode(Assert.IsType<PdfString>(field[Name("TU")])));
        PdfDictionary normalAppearances = Assert.IsType<PdfDictionary>(
            Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")]);
        PdfStream appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(normalAppearances[Name("Normal")])));
        Assert.Contains("(Open KillerPDF) Tj", Encoding.ASCII.GetString(appearance.EncodedData.Span));
    }

    [Theory]
    [InlineData("file:///tmp/report.pdf")]
    [InlineData("javascript:alert(1)")]
    [InlineData("relative/path")]
    public void AddUriPushButton_RejectsUnsafeUri(string uri)
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddUriPushButton(0, "unsafe", 0, 0, 100, 20, "Open", uri));
    }

    [Fact]
    public void AddUriPushButton_RequiresEmbeddedFontForUnicodeLabel()
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddUriPushButton(0, "unicode", 0, 0, 100, 20, "Ouvrir Ω", "https://example.com"));
    }

    [Fact]
    public void PdfA4_RejectsUriPushButtonActions()
    {
        Assert.Throws<InvalidOperationException>(() => new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata { Title = "Buttons", Language = "en-US" })
            .EnablePdfA4Conformance()
            .AddBlankPage()
            .AddUriPushButton(0, "website", 0, 0, 100, 20, "Open", "https://example.com")
            .Build());
    }

    [Fact]
    public void AddPagePushButton_WritesPreciseGoToDestination()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddPagePushButton(0, "next", 10, 10, 100, 20, "Next", 1,
                PdfDestination.At(12, 700, 1.5))
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfDictionary action = Assert.IsType<PdfDictionary>(field[Name("A")]);
        PdfArray destination = Assert.IsType<PdfArray>(action[Name("D")]);

        Assert.Equal("GoTo", Assert.IsType<PdfName>(action[Name("S")]).ValueAsLatin1());
        Assert.IsType<PdfIndirectReference>(destination[0]);
        Assert.Equal("XYZ", Assert.IsType<PdfName>(destination[1]).ValueAsLatin1());
        Assert.Equal(12, Assert.IsType<PdfInteger>(destination[2]).Value);
        Assert.Equal(700, Assert.IsType<PdfInteger>(destination[3]).Value);
        Assert.Equal(1.5, Assert.IsType<PdfReal>(destination[4]).Value);
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
