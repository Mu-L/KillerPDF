using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Tests.Fonts;
using System.Buffers.Binary;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfTextFieldTests
{
    [Fact]
    public void AddTextField_WritesAcroFormWidgetValueAndAppearance()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "customer.name", 72, 650, 240, 24, "Steve (Killer)", 12)
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var acroForm = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        var fields = Assert.IsType<PdfArray>(acroForm[Name("Fields")]);
        var widgetReference = Assert.IsType<PdfIndirectReference>(fields[0]);
        var widget = ResolveDictionary(document, widgetReference);
        var appearanceDictionary = Assert.IsType<PdfDictionary>(widget[Name("AP")]);
        var appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(appearanceDictionary[Name("N")])));
        var pages = ResolveDictionary(document, catalog[Name("Pages")]);
        var page = ResolveDictionary(document, Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);
        var annotations = Assert.IsType<PdfArray>(page[Name("Annots")]);

        Assert.False(Assert.IsType<PdfBoolean>(acroForm[Name("NeedAppearances")]).Value);
        Assert.Equal("Tx", Assert.IsType<PdfName>(widget[Name("FT")]).ValueAsLatin1());
        Assert.Equal("customer.name", DecodeUnicode(Assert.IsType<PdfString>(widget[Name("T")])));
        Assert.Equal("Steve (Killer)", DecodeUnicode(Assert.IsType<PdfString>(widget[Name("V")])));
        Assert.Equal("Steve (Killer)", DecodeUnicode(Assert.IsType<PdfString>(widget[Name("DV")])));
        Assert.Equal(widgetReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(annotations[0]).ObjectNumber);
        Assert.Contains("(Steve \\(Killer\\)) Tj",
            Encoding.ASCII.GetString(appearance.EncodedData.Span));
        var resources = Assert.IsType<PdfDictionary>(appearance.Dictionary[Name("Resources")]);
        Assert.NotNull(Assert.IsType<PdfDictionary>(resources[Name("Font")])[Name("Helv")]);
    }

    [Fact]
    public void AddTextField_PreservesLinkAnnotationsBeforeWidgets()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddUriLink(0, 0, 0, 10, 10, "https://killerpdf.net")
            .AddTextField(0, "name", 20, 20, 100, 20)
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var pages = ResolveDictionary(document, catalog[Name("Pages")]);
        var page = ResolveDictionary(document, Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);
        var annotations = Assert.IsType<PdfArray>(page[Name("Annots")]);

        Assert.Equal(2, annotations.Count);
        Assert.Equal("Link", Assert.IsType<PdfName>(
            ResolveDictionary(document, annotations[0])[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal("Widget", Assert.IsType<PdfName>(
            ResolveDictionary(document, annotations[1])[Name("Subtype")]).ValueAsLatin1());
    }

    [Fact]
    public void AddTextField_RejectsDuplicateNamesAndUnsupportedAppearanceText()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage()
            .AddTextField(0, "name", 0, 0, 100, 20);

        Assert.Throws<ArgumentException>(() =>
            builder.AddTextField(0, "name", 0, 30, 100, 20));
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddTextField(0, "unicode", 0, 0, 100, 20, "你好"));
    }

    [Fact]
    public void AddTextField_WritesCombLengthAndBehaviorFlags()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "code", 0, 0, 120, 20, "1234", 12,
                new PdfTextFieldOptions
                {
                    Required = true,
                    ReadOnly = true,
                    Comb = true,
                    MaximumLength = 8
                })
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var acroForm = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        var widget = ResolveDictionary(document, Assert.IsType<PdfArray>(acroForm[Name("Fields")])[0]);
        PdfStream appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfDictionary>(widget[Name("AP")])[Name("N")])));
        string content = Encoding.ASCII.GetString(appearance.EncodedData.Span);

        Assert.Equal(8, Assert.IsType<PdfInteger>(widget[Name("MaxLen")]).Value);
        Assert.Equal((1 << 24) | 2 | 1, Assert.IsType<PdfInteger>(widget[Name("Ff")]).Value);
        Assert.Equal(7, content.Split(" 0.5 m\n", StringSplitOptions.None).Length - 1);
        Assert.Contains("(1) Tj", content);
        Assert.Contains("(2) Tj", content);
        Assert.Contains("(3) Tj", content);
        Assert.Contains("(4) Tj", content);
        Assert.DoesNotContain("(1234) Tj", content);
    }

    [Fact]
    public void AddTextField_RejectsInvalidCombConfigurationAndOversizedValue()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        Assert.Throws<ArgumentException>(() => builder.AddTextField(
            0, "comb", 0, 0, 100, 20, options: new PdfTextFieldOptions { Comb = true }));
        Assert.Throws<ArgumentException>(() => builder.AddTextField(
            0, "short", 0, 0, 100, 20, "toolong", options:
                new PdfTextFieldOptions { MaximumLength = 3 }));
    }

    [Fact]
    public void AddTextField_WritesNoSpellCheckAndNoScrollFlags()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "fixed", 0, 0, 100, 20, options: new PdfTextFieldOptions
            {
                DoNotSpellCheck = true,
                DoNotScroll = true
            })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);

        Assert.Equal((1 << 22) | (1 << 23),
            Assert.IsType<PdfInteger>(field[Name("Ff")]).Value);
    }

    [Fact]
    public void AddTextField_RendersMultilineValueOnSeparateClippedBaselines()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "address", 0, 0, 160, 60, "First\r\nSecond\nThird", 12,
                new PdfTextFieldOptions { Multiline = true })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfStream appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")])));
        string content = Encoding.ASCII.GetString(appearance.EncodedData.Span);

        Assert.Equal(1 << 12, Assert.IsType<PdfInteger>(field[Name("Ff")]).Value);
        Assert.Contains("re\nW\nn", content);
        Assert.Contains("(First) Tj", content);
        Assert.Contains("(Second) Tj", content);
        Assert.Contains("(Third) Tj", content);
        Assert.Equal(3, content.Split("BT\n", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AddTextField_RejectsLineBreaksUnlessMultiline()
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddTextField(0, "single", 0, 0, 100, 20, "First\nSecond"));
    }

    [Fact]
    public void AddTextField_MasksPasswordAppearanceWithoutChangingValue()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "password", 0, 0, 100, 20, "secret", options:
                new PdfTextFieldOptions { Password = true })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfStream appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")])));
        string content = Encoding.ASCII.GetString(appearance.EncodedData.Span);

        Assert.Equal("secret", DecodeUnicode(Assert.IsType<PdfString>(field[Name("V")])));
        Assert.DoesNotContain("secret", content);
        Assert.Contains("(******) Tj", content);
    }

    [Fact]
    public void AddTextField_RejectsMultilinePassword()
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder().AddBlankPage()
            .AddTextField(0, "password", 0, 0, 100, 40, "secret", options:
                new PdfTextFieldOptions { Password = true, Multiline = true }));
    }

    [Theory]
    [InlineData(PdfTextFieldAlignment.Center, 1)]
    [InlineData(PdfTextFieldAlignment.Right, 2)]
    public void AddTextField_WritesAlignmentAndMovesAppearance(
        PdfTextFieldAlignment alignment, int expectedQuadding)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "aligned", 0, 0, 200, 20, "Text", options:
                new PdfTextFieldOptions { Alignment = alignment })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfStream appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")])));
        string content = Encoding.ASCII.GetString(appearance.EncodedData.Span);

        Assert.Equal(expectedQuadding, Assert.IsType<PdfInteger>(field[Name("Q")]).Value);
        Assert.DoesNotContain("3 4 Td", content);
        Assert.Contains("(Text) Tj", content);
    }

    [Fact]
    public void AddTextField_EmbedsUnicodeFontInAppearanceAndAcroFormResources()
    {
        TrueTypeFont font = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(format12: true));
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "emoji", 0, 0, 100, 20, "😀", 12, embeddedFont: font)
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var acroForm = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        var formResources = Assert.IsType<PdfDictionary>(
            Assert.IsType<PdfDictionary>(acroForm[Name("DR")])[Name("Font")]);
        var field = ResolveDictionary(document, Assert.IsType<PdfArray>(acroForm[Name("Fields")])[0]);
        var appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")])));
        var type0 = ResolveDictionary(document, formResources[Name("FormF1")]);

        Assert.Equal("Type0", Assert.IsType<PdfName>(type0[Name("Subtype")]).ValueAsLatin1());
        Assert.Contains("/FormF1 12 Tf", Encoding.ASCII.GetString(appearance.EncodedData.Span));
        Assert.Contains("<0001> Tj", Encoding.ASCII.GetString(appearance.EncodedData.Span));
    }

    [Fact]
    public void PdfA4Mode_AcceptsTextFieldWithEmbeddedFont()
    {
        TrueTypeFont font = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(format12: false));
        byte[] icc = new byte[128];
        BinaryPrimitives.WriteUInt32BigEndian(icc, 128);
        "RGB "u8.CopyTo(icc.AsSpan(16));
        "acsp"u8.CopyTo(icc.AsSpan(36));

        byte[] pdf = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata())
            .SetOutputIntent(PdfIccProfile.Load(icc), "Test RGB")
            .EnablePdfA4Conformance()
            .AddBlankPage()
            .AddTextField(0, "letter", 0, 0, 100, 20, "A", embeddedFont: font)
            .Build();

        Assert.NotEmpty(pdf);
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
