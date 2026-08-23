using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfGraphicsStateTests
{
    [Fact]
    public void Build_WritesOpacityBlendModeResourceAndGraphicsOperator()
    {
        var state = new PdfGraphicsState(0.25, 0.75, PdfBlendMode.Multiply);
        var content = new PdfContentStreamBuilder()
            .SetGraphicsState(state)
            .Rectangle(10, 10, 50, 40)
            .FillAndStroke();

        PdfDocument document = PdfDocument.Open(
            new PdfDocumentBuilder().AddPage(100, 100, content).Build());
        PdfDictionary page = FirstPage(document);
        PdfDictionary resources = Assert.IsType<PdfDictionary>(page[Name("Resources")]);
        PdfDictionary states = Assert.IsType<PdfDictionary>(resources[Name("ExtGState")]);
        PdfDictionary written = ResolveDictionary(document, states[Name("GS1")]);
        PdfStream stream = ResolveStream(document, page[Name("Contents")]);

        Assert.Equal("ExtGState", Assert.IsType<PdfName>(written[Name("Type")]).ValueAsLatin1());
        Assert.Equal(0.25, Number(written[Name("ca")]));
        Assert.Equal(0.75, Number(written[Name("CA")]));
        Assert.Equal("Multiply", Assert.IsType<PdfName>(written[Name("BM")]).ValueAsLatin1());
        Assert.StartsWith("/GS1 gs\n", Encoding.ASCII.GetString(stream.EncodedData.Span));
    }

    [Fact]
    public void Build_DeduplicatesEquivalentStatesWithinAndAcrossPages()
    {
        PdfContentStreamBuilder Content() => new PdfContentStreamBuilder()
            .SetGraphicsState(new PdfGraphicsState(0.5, 0.5, PdfBlendMode.Screen))
            .Rectangle(0, 0, 10, 10).Fill()
            .SetGraphicsState(new PdfGraphicsState(0.5, 0.5, PdfBlendMode.Screen))
            .Rectangle(20, 20, 10, 10).Fill();
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(100, 100, Content())
            .AddPage(100, 100, Content())
            .Build());
        PdfDictionary[] pages = Pages(document);
        int[] references = pages.Select(page =>
        {
            PdfDictionary resources = Assert.IsType<PdfDictionary>(page[Name("Resources")]);
            PdfDictionary states = Assert.IsType<PdfDictionary>(resources[Name("ExtGState")]);
            Assert.Single(states);
            PdfStream stream = ResolveStream(document, page[Name("Contents")]);
            Assert.Equal(2, Encoding.ASCII.GetString(stream.EncodedData.Span)
                .Split("/GS1 gs", StringSplitOptions.None).Length - 1);
            return Assert.IsType<PdfIndirectReference>(states[Name("GS1")]).ObjectNumber;
        }).ToArray();

        Assert.Equal(references[0], references[1]);
    }

    [Theory]
    [InlineData(PdfBlendMode.Normal, "Normal")]
    [InlineData(PdfBlendMode.ColorDodge, "ColorDodge")]
    [InlineData(PdfBlendMode.HardLight, "HardLight")]
    [InlineData(PdfBlendMode.Hue, "Hue")]
    [InlineData(PdfBlendMode.Luminosity, "Luminosity")]
    public void Build_WritesStandardBlendModeNames(PdfBlendMode mode, string expected)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .SetBlendMode(mode).Rectangle(0, 0, 10, 10).Fill())
            .Build());
        PdfDictionary resources = Assert.IsType<PdfDictionary>(
            FirstPage(document)[Name("Resources")]);
        PdfDictionary states = Assert.IsType<PdfDictionary>(resources[Name("ExtGState")]);
        PdfDictionary state = ResolveDictionary(document, states[Name("GS1")]);

        Assert.Equal(expected, Assert.IsType<PdfName>(state[Name("BM")]).ValueAsLatin1());
    }

    [Fact]
    public void GraphicsState_RejectsInvalidOpacityAndBlendMode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfGraphicsState(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfGraphicsState(strokeOpacity: 1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfGraphicsState(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PdfGraphicsState(blendMode: (PdfBlendMode)999));
    }

    [Fact]
    public void PdfUa2Mode_DoesNotMistakeGraphicsStateSelectionForPaintedContent()
    {
        var content = new PdfContentStreamBuilder()
            .SetOpacity(0.5)
            .BeginMarkedContent(PdfStructureType.Figure, 0)
            .Rectangle(0, 0, 10, 10).Fill()
            .EndMarkedContent();
        byte[] result = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata { Title = "Transparency", Language = "en-US" })
            .EnablePdfUa2Conformance()
            .AddPage(100, 100, content)
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
                alternateDescription: "A translucent square")
            .Build();

        Assert.NotEmpty(result);
    }

    private static PdfDictionary FirstPage(PdfDocument document) => Pages(document)[0];
    private static PdfDictionary[] Pages(PdfDocument document)
    {
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary pages = ResolveDictionary(document, catalog[Name("Pages")]);
        return Assert.IsType<PdfArray>(pages[Name("Kids")])
            .Select(value => ResolveDictionary(document, value)).ToArray();
    }
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfStream ResolveStream(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfStream>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static double Number(PdfObject value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => throw new Xunit.Sdk.XunitException("Expected a PDF number.")
    };
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
