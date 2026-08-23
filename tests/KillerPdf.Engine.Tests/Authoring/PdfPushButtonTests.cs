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

    [Fact]
    public void AddNamedDestinationPushButton_WritesSharedUnicodeTarget()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddNamedDestination("Résumé", 1, PdfDestination.FitWidth(700))
            .AddNamedDestinationPushButton(0, "resume", 10, 10, 100, 20,
                "Résumé", "Résumé")
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfDictionary action = Assert.IsType<PdfDictionary>(field[Name("A")]);

        Assert.Equal("GoTo", Assert.IsType<PdfName>(action[Name("S")]).ValueAsLatin1());
        Assert.Equal("Résumé", DecodeUnicode(Assert.IsType<PdfString>(action[Name("D")])));
    }

    [Fact]
    public void AddNamedDestinationPushButton_RequiresExistingTarget()
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddNamedDestinationPushButton(0, "missing", 0, 0, 100, 20, "Missing", "missing"));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void AddResetFormPushButton_WritesSelectedFieldsAndExclusionFlag(
        bool excludeFields, int expectedFlags)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "name", 0, 0, 100, 20)
            .AddCheckBox(0, "approved", 0, 30, 20, 20)
            .AddResetFormPushButton(0, "reset", 0, 60, 100, 20, "Reset",
                ["name", "approved"], excludeFields)
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfArray fields = Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")]);
        PdfDictionary action = Assert.IsType<PdfDictionary>(ResolveDictionary(document, fields[2])[Name("A")]);

        Assert.Equal("ResetForm", Assert.IsType<PdfName>(action[Name("S")]).ValueAsLatin1());
        Assert.Equal(["name", "approved"], Assert.IsType<PdfArray>(action[Name("Fields")])
            .Select(value => DecodeUnicode(Assert.IsType<PdfString>(value))));
        if (expectedFlags == 0)
            Assert.False(action.ContainsKey(Name("Flags")));
        else
            Assert.Equal(expectedFlags, Assert.IsType<PdfInteger>(action[Name("Flags")]).Value);
    }

    [Fact]
    public void AddResetFormPushButton_RejectsUndefinedField()
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddResetFormPushButton(0, "reset", 0, 0, 100, 20, "Reset", ["missing"]));
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddResetFormPushButton(0, "reset", 0, 0, 100, 20, "Reset", excludeFields: true));
    }

    [Fact]
    public void AddSubmitPdfPushButton_WritesUrlFileSpecFieldsAndFlags()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "name", 0, 0, 100, 20)
            .AddSubmitPdfPushButton(0, "submit", 0, 30, 100, 20, "Submit",
                "https://example.com/forms", ["name"], excludeFields: true)
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfArray fields = Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")]);
        PdfDictionary action = Assert.IsType<PdfDictionary>(ResolveDictionary(document, fields[1])[Name("A")]);
        PdfDictionary file = Assert.IsType<PdfDictionary>(action[Name("F")]);

        Assert.Equal("SubmitForm", Assert.IsType<PdfName>(action[Name("S")]).ValueAsLatin1());
        Assert.Equal((1 << 8) | 1, Assert.IsType<PdfInteger>(action[Name("Flags")]).Value);
        Assert.Equal("URL", Assert.IsType<PdfName>(file[Name("FS")]).ValueAsLatin1());
        Assert.Equal("https://example.com/forms",
            DecodeUnicode(Assert.IsType<PdfString>(file[Name("F")])));
        Assert.Equal("name", DecodeUnicode(Assert.IsType<PdfString>(
            Assert.IsType<PdfArray>(action[Name("Fields")])[0])));
    }

    [Theory]
    [InlineData("file:///tmp/upload")]
    [InlineData("mailto:test@example.com")]
    [InlineData("relative")]
    public void AddSubmitPdfPushButton_RejectsUnsafeEndpoint(string uri)
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddSubmitPdfPushButton(0, "submit", 0, 0, 100, 20, "Submit", uri));
    }

    [Fact]
    public void AddSubmitPdfPushButton_RequiresFieldsForExclusionMode()
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddSubmitPdfPushButton(0, "submit", 0, 0, 100, 20, "Submit",
                "https://example.com", excludeFields: true));
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
