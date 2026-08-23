using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Tests.Fonts;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfContentStreamBuilderTests
{
    [Fact]
    public void Build_WritesDeterministicGraphicsOperators()
    {
        byte[] content = new PdfContentStreamBuilder()
            .SaveState()
            .SetLineWidth(2)
            .SetStrokeRgb(1, 0.5, 0)
            .MoveTo(10, 20)
            .LineTo(30.25, 40)
            .Stroke()
            .RestoreState()
            .Build();

        Assert.Equal(
            "q\n2 w\n1 0.5 0 RG\n10 20 m\n30.25 40 l\nS\nQ\n",
            Encoding.ASCII.GetString(content));
    }

    [Fact]
    public void DocumentBuilder_EmbedsTypedContentAndReopensIt()
    {
        var content = new PdfContentStreamBuilder()
            .SetFillGray(0.25)
            .Rectangle(10, 20, 30, 40)
            .Fill();
        PdfDocument document = PdfDocument.Open(
            new PdfDocumentBuilder().AddPage(100, 100, content).Build());
        var catalog = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(document.Trailer[Name("Root")])));
        var pages = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(catalog[Name("Pages")])));
        var kids = Assert.IsType<PdfArray>(pages[Name("Kids")]);
        var page = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(kids[0])));
        var stream = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(page[Name("Contents")])));

        Assert.Equal("0.25 g\n10 20 30 40 re\nf\n", Encoding.ASCII.GetString(stream.EncodedData.Span));
    }

    [Fact]
    public void Build_RejectsUnbalancedGraphicsState()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PdfContentStreamBuilder().SaveState().Build());
        Assert.Throws<InvalidOperationException>(() =>
            new PdfContentStreamBuilder().RestoreState());
    }

    [Fact]
    public void TextOperators_CreateAFontResourceAndEscapedText()
    {
        var content = new PdfContentStreamBuilder()
            .BeginText()
            .SetFont(PdfStandardFont.HelveticaBold, 18)
            .MoveText(72, 700)
            .ShowLatin1Text("KillerPDF (2.0)")
            .EndText();
        PdfDocument document = PdfDocument.Open(
            new PdfDocumentBuilder().AddPage(612, 792, content).Build());
        var catalog = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(document.Trailer[Name("Root")])));
        var pages = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(catalog[Name("Pages")])));
        var page = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfArray>(pages[Name("Kids")])[0])));
        var resources = Assert.IsType<PdfDictionary>(page[Name("Resources")]);
        var fonts = Assert.IsType<PdfDictionary>(resources[Name("Font")]);
        var font = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(fonts[Name("F1")])));

        Assert.Equal("Helvetica-Bold", Assert.IsType<PdfName>(font[Name("BaseFont")]).ValueAsLatin1());
        var stream = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(page[Name("Contents")])));
        Assert.Contains("(KillerPDF \\(2.0\\)) Tj", Encoding.ASCII.GetString(stream.EncodedData.Span));
    }

    [Fact]
    public void UnicodeText_EmbedsTrueTypeType0FontAndToUnicodeMap()
    {
        TrueTypeFont embedded = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(format12: false));
        var content = new PdfContentStreamBuilder()
            .BeginText()
            .SetFont(embedded, 12)
            .MoveText(72, 700)
            .ShowUnicodeText("A")
            .EndText();
        PdfDocument document = PdfDocument.Open(
            new PdfDocumentBuilder().AddPage(612, 792, content).Build());
        var catalog = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(document.Trailer[Name("Root")])));
        var pages = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(catalog[Name("Pages")])));
        var page = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfArray>(pages[Name("Kids")])[0])));
        var resources = Assert.IsType<PdfDictionary>(page[Name("Resources")]);
        var fontResources = Assert.IsType<PdfDictionary>(resources[Name("Font")]);
        var type0 = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(fontResources[Name("F1")])));
        var toUnicode = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(type0[Name("ToUnicode")])));
        var descendants = Assert.IsType<PdfArray>(type0[Name("DescendantFonts")]);
        var cidFont = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(descendants[0])));
        var descriptor = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(cidFont[Name("FontDescriptor")])));
        var fontFile = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(descriptor[Name("FontFile2")])));

        Assert.Equal("Type0", Assert.IsType<PdfName>(type0[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal("CIDFontType2", Assert.IsType<PdfName>(cidFont[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(embedded.FontData.Length, fontFile.EncodedData.Length);
        Assert.Contains("<0001> <0041>", Encoding.ASCII.GetString(toUnicode.EncodedData.Span));
        var contentStream = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(page[Name("Contents")])));
        Assert.Contains("<0001> Tj", Encoding.ASCII.GetString(contentStream.EncodedData.Span));
    }

    [Fact]
    public void UnicodeText_WritesSupplementaryCharactersAsUtf16InToUnicodeMap()
    {
        TrueTypeFont embedded = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(format12: true));
        var content = new PdfContentStreamBuilder()
            .BeginText().SetFont(embedded, 12).ShowUnicodeText("😀").EndText();
        PdfDocument document = PdfDocument.Open(
            new PdfDocumentBuilder().AddPage(100, 100, content).Build());
        PdfStream toUnicode = FindToUnicode(document);

        Assert.Contains("<0001> <D83DDE00>", Encoding.ASCII.GetString(toUnicode.EncodedData.Span));
    }

    [Fact]
    public void SetFont_RejectsRestrictedTrueTypeEmbedding()
    {
        TrueTypeFont restricted = TrueTypeFont.Load(
            TrueTypeFontTests.BuildTestFont(format12: false, embeddingFlags: 0x0002));
        var content = new PdfContentStreamBuilder().BeginText();

        Assert.Throws<InvalidOperationException>(() => content.SetFont(restricted, 12));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void SetFillGray_RejectsInvalidComponents(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PdfContentStreamBuilder().SetFillGray(value));
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));

    private static PdfStream FindToUnicode(PdfDocument document)
    {
        var catalog = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(document.Trailer[Name("Root")])));
        var pages = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(catalog[Name("Pages")])));
        var page = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfArray>(pages[Name("Kids")])[0])));
        var resources = Assert.IsType<PdfDictionary>(page[Name("Resources")]);
        var fonts = Assert.IsType<PdfDictionary>(resources[Name("Font")]);
        var type0 = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(fonts[Name("F1")])));
        return Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(type0[Name("ToUnicode")])));
    }
}
