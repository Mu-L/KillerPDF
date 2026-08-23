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
            .AddUriLink(0, 10, 20, 100, 30, "https://killerpdf.net/docs?q=2",
                annotationMetadata: new PdfAnnotationMetadata
                {
                    Author = "KillerPDF",
                    Subject = "Documentation",
                    Flags = PdfAnnotationFlags.Print | PdfAnnotationFlags.Locked
                }, contents: "Open documentation")
            .Build());
        PdfDictionary annotation = FirstAnnotation(document);
        var action = Assert.IsType<PdfDictionary>(annotation[Name("A")]);
        var rectangle = Assert.IsType<PdfArray>(annotation[Name("Rect")]);
        var border = Assert.IsType<PdfArray>(annotation[Name("Border")]);

        Assert.Equal("Link", Assert.IsType<PdfName>(annotation[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(132, Assert.IsType<PdfInteger>(annotation[Name("F")]).Value);
        Assert.Equal("Open documentation",
            DecodeUnicode(Assert.IsType<PdfString>(annotation[Name("Contents")])));
        Assert.Equal("KillerPDF",
            DecodeUnicode(Assert.IsType<PdfString>(annotation[Name("T")])));
        Assert.True(annotation.ContainsKey(Name("P")));
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

    [Fact]
    public void AddPageLink_WritesPreciseDestinationView()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .AddPageLink(0, 0, 0, 50, 50, 1,
                destination: PdfDestination.FitRectangle(40, 50, 500, 700))
            .Build());
        PdfArray destination = Assert.IsType<PdfArray>(
            FirstAnnotation(document)[Name("Dest")]);

        Assert.Equal("FitR", Assert.IsType<PdfName>(destination[1]).ValueAsLatin1());
        Assert.Equal([40L, 50L, 500L, 700L], destination.Skip(2)
            .Select(value => Assert.IsType<PdfInteger>(value).Value));
    }

    [Fact]
    public void AddUriLink_WithMultipleQuads_WritesTightBoundsAndHitGeometry()
    {
        PdfTextQuad[] quads =
        [
            new(new PdfPoint(10, 40), new PdfPoint(110, 40),
                new PdfPoint(10, 25), new PdfPoint(110, 25)),
            new(new PdfPoint(20, 20), new PdfPoint(80, 22),
                new PdfPoint(20, 5), new PdfPoint(80, 7))
        ];
        PdfDictionary annotation = FirstAnnotation(PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddUriLink(0, quads, "https://killerpdf.net")
            .Build()));

        Assert.Equal(16, Assert.IsType<PdfArray>(annotation[Name("QuadPoints")]).Count);
        Assert.Equal([10d, 5d, 110d, 40d],
            Assert.IsType<PdfArray>(annotation[Name("Rect")]).Select(NumberValue));
    }

    [Fact]
    public void LinkQuadOverloads_RejectEmptyGeometry()
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder()
            .AddBlankPage().AddUriLink(0, [], "https://killerpdf.net"));
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
    private static double NumberValue(PdfObject value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => throw new InvalidOperationException()
    };
    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
