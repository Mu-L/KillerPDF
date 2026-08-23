using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfDocumentBuilderTests
{
    [Fact]
    public void Build_CreatesAReopenableCatalogAndPageTree()
    {
        byte[] bytes = new PdfDocumentBuilder()
            .AddBlankPage(612, 792)
            .AddPage(300.5, 400.25, "0 0 m 100 100 l S\n"u8.ToArray())
            .Build();
        PdfDocument document = PdfDocument.Open(bytes);

        var rootReference = Assert.IsType<PdfIndirectReference>(document.Trailer[Name("Root")]);
        var catalog = Assert.IsType<PdfDictionary>(document.Resolve(rootReference));
        var pagesReference = Assert.IsType<PdfIndirectReference>(catalog[Name("Pages")]);
        var pages = Assert.IsType<PdfDictionary>(document.Resolve(pagesReference));
        Assert.Equal(2, Assert.IsType<PdfInteger>(pages[Name("Count")]).Value);

        var kids = Assert.IsType<PdfArray>(pages[Name("Kids")]);
        var secondPage = Assert.IsType<PdfDictionary>(
            document.Resolve(Assert.IsType<PdfIndirectReference>(kids[1])));
        var mediaBox = Assert.IsType<PdfArray>(secondPage[Name("MediaBox")]);
        Assert.Equal(300.5, Assert.IsType<PdfReal>(mediaBox[2]).Value);
        var content = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(secondPage[Name("Contents")])));
        Assert.Equal("0 0 m 100 100 l S\n", Encoding.ASCII.GetString(content.EncodedData.Span));
    }

    [Fact]
    public void Build_IsDeterministicAndAcceptsAnEmptyPageTree()
    {
        byte[] first = new PdfDocumentBuilder().Build();
        byte[] second = new PdfDocumentBuilder().Build();

        Assert.Equal(first, second);
        Assert.True(KillerPdf.Engine.Diagnostics.PdfDocumentInspector.Inspect(first).IsStructurallyValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AddBlankPage_RejectsInvalidDimensions(double width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PdfDocumentBuilder().AddBlankPage(width, 100));
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
