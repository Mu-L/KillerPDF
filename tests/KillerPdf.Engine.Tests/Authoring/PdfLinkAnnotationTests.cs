using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfLinkAnnotationTests
{
    [Fact]
    public void AddUriLink_WritesUriActionAndInvisibleBorder()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddUriLink(0, 10, 20, 100, 30, "https://killerpdf.net/docs?q=2")
            .Build());
        PdfDictionary annotation = FirstAnnotation(document);
        var action = Assert.IsType<PdfDictionary>(annotation[Name("A")]);
        var rectangle = Assert.IsType<PdfArray>(annotation[Name("Rect")]);
        var border = Assert.IsType<PdfArray>(annotation[Name("Border")]);

        Assert.Equal("Link", Assert.IsType<PdfName>(annotation[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(4, Assert.IsType<PdfInteger>(annotation[Name("F")]).Value);
        Assert.Equal("URI", Assert.IsType<PdfName>(action[Name("S")]).ValueAsLatin1());
        Assert.Equal("https://killerpdf.net/docs?q=2",
            Encoding.UTF8.GetString(Assert.IsType<PdfString>(action[Name("URI")]).Bytes.Span));
        Assert.Equal(new long[] { 10, 20, 110, 50 },
            rectangle.Select(value => Assert.IsType<PdfInteger>(value).Value));
        Assert.All(border, value => Assert.Equal(0, Assert.IsType<PdfInteger>(value).Value));
    }

    [Fact]
    public void AddPageLink_UsesTargetPageReferenceAndFitDestination()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .AddPageLink(0, 0, 0, 50, 50, 1)
            .Build());
        PdfDictionary annotation = FirstAnnotation(document);
        var destination = Assert.IsType<PdfArray>(annotation[Name("Dest")]);
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var pages = ResolveDictionary(document, catalog[Name("Pages")]);
        var target = Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfArray>(pages[Name("Kids")])[1]);

        var actualTarget = Assert.IsType<PdfIndirectReference>(destination[0]);
        Assert.Equal(target.ObjectNumber, actualTarget.ObjectNumber);
        Assert.Equal(target.Generation, actualTarget.Generation);
        Assert.Equal("Fit", Assert.IsType<PdfName>(destination[1]).ValueAsLatin1());
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    public void AddUriLink_RejectsUnsafeOrRelativeSchemes(string uri)
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder()
            .AddBlankPage().AddUriLink(0, 0, 0, 10, 10, uri));
    }

    private static PdfDictionary FirstAnnotation(PdfDocument document)
    {
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var pages = ResolveDictionary(document, catalog[Name("Pages")]);
        var page = ResolveDictionary(document, Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);
        return ResolveDictionary(document, Assert.IsType<PdfArray>(page[Name("Annots")])[0]);
    }

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
