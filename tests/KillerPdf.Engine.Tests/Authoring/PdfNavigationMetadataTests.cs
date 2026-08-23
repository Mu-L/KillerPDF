using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfNavigationMetadataTests
{
    [Fact]
    public void Build_WritesSortedNamedDestinationsLinksAndPageLabels()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage().AddBlankPage()
            .AddNamedDestination("zeta", 0)
            .AddNamedDestination("alpha", 1)
            .AddNamedDestinationLink(2, 20, 20, 80, 30, "alpha")
            .AddPageLabelRange(0, PdfPageLabelStyle.LowerRoman)
            .AddPageLabelRange(2, PdfPageLabelStyle.Decimal, "A-", 3)
            .AddAttachment("notes.txt", "notes"u8.ToArray(), "text/plain")
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary names = Assert.IsType<PdfDictionary>(catalog[Name("Names")]);
        PdfDictionary destinations = Assert.IsType<PdfDictionary>(names[Name("Dests")]);
        PdfArray destinationNames = Assert.IsType<PdfArray>(destinations[Name("Names")]);
        PdfDictionary pages = ResolveDictionary(document, catalog[Name("Pages")]);
        PdfArray pageReferences = Assert.IsType<PdfArray>(pages[Name("Kids")]);
        PdfArray alpha = Assert.IsType<PdfArray>(destinationNames[1]);
        PdfDictionary labels = Assert.IsType<PdfDictionary>(catalog[Name("PageLabels")]);
        PdfArray numbers = Assert.IsType<PdfArray>(labels[Name("Nums")]);
        PdfDictionary roman = Assert.IsType<PdfDictionary>(numbers[1]);
        PdfDictionary appendix = Assert.IsType<PdfDictionary>(numbers[3]);
        PdfDictionary thirdPage = ResolveDictionary(document, pageReferences[2]);
        PdfDictionary link = ResolveDictionary(document,
            Assert.IsType<PdfArray>(thirdPage[Name("Annots")])[0]);

        Assert.True(names.ContainsKey(Name("EmbeddedFiles")));
        Assert.Equal("alpha", DecodeUnicode(Assert.IsType<PdfString>(destinationNames[0])));
        Assert.Equal("zeta", DecodeUnicode(Assert.IsType<PdfString>(destinationNames[2])));
        Assert.Equal(Assert.IsType<PdfIndirectReference>(pageReferences[1]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(alpha[0]).ObjectNumber);
        Assert.Equal("Fit", Assert.IsType<PdfName>(alpha[1]).ValueAsLatin1());
        Assert.Equal("alpha", DecodeUnicode(Assert.IsType<PdfString>(link[Name("Dest")])));
        Assert.Equal([0L, 2L], new[]
        {
            Assert.IsType<PdfInteger>(numbers[0]).Value,
            Assert.IsType<PdfInteger>(numbers[2]).Value
        });
        Assert.Equal("r", Assert.IsType<PdfName>(roman[Name("S")]).ValueAsLatin1());
        Assert.Equal("D", Assert.IsType<PdfName>(appendix[Name("S")]).ValueAsLatin1());
        Assert.Equal("A-", DecodeUnicode(Assert.IsType<PdfString>(appendix[Name("P")])));
        Assert.Equal(3, Assert.IsType<PdfInteger>(appendix[Name("St")]).Value);
    }

    [Fact]
    public void NavigationMetadata_RejectsInvalidOrAmbiguousDefinitions()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();

        Assert.Throws<ArgumentException>(() => builder.AddNamedDestination(" ", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddNamedDestination("missing", 1));
        builder.AddNamedDestination("start", 0);
        Assert.Throws<ArgumentException>(() => builder.AddNamedDestination("start", 0));
        Assert.Throws<ArgumentException>(() => builder.AddNamedDestinationLink(
            0, 0, 0, 10, 10, "missing"));
        Assert.Throws<ArgumentException>(() => builder.AddPageLabelRange(
            0, PdfPageLabelStyle.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddPageLabelRange(
            0, PdfPageLabelStyle.Decimal, startNumber: 0));
        builder.AddPageLabelRange(0, PdfPageLabelStyle.UpperRoman);
        Assert.Throws<ArgumentException>(() => builder.AddPageLabelRange(
            0, PdfPageLabelStyle.Decimal));
    }

    [Fact]
    public void NavigationMetadata_IsDeterministic()
    {
        static byte[] Build() => new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddNamedDestination("second", 1)
            .AddNamedDestinationLink(0, 10, 10, 20, 20, "second")
            .AddPageLabelRange(0, PdfPageLabelStyle.UpperLetters, "Section ")
            .Build();

        Assert.Equal(Build(), Build());
    }

    [Fact]
    public void Build_WritesInitialViewAndRichNamedDestinations()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage(600, 800).AddBlankPage(600, 800)
            .SetOpenAction(1, PdfDestination.At(72, 700, 1.5))
            .AddNamedDestination("width", 0, PdfDestination.FitWidth(760))
            .AddNamedDestination("detail", 1,
                PdfDestination.FitRectangle(100, 200, 400, 650))
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfArray open = Assert.IsType<PdfArray>(catalog[Name("OpenAction")]);
        PdfDictionary names = Assert.IsType<PdfDictionary>(catalog[Name("Names")]);
        PdfArray destinations = Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(names[Name("Dests")])[Name("Names")]);
        PdfArray detail = Assert.IsType<PdfArray>(destinations[1]);
        PdfArray width = Assert.IsType<PdfArray>(destinations[3]);

        Assert.Equal("XYZ", Assert.IsType<PdfName>(open[1]).ValueAsLatin1());
        Assert.Equal(72, Assert.IsType<PdfInteger>(open[2]).Value);
        Assert.Equal(700, Assert.IsType<PdfInteger>(open[3]).Value);
        Assert.Equal(1.5, Assert.IsType<PdfReal>(open[4]).Value);
        Assert.Equal("FitR", Assert.IsType<PdfName>(detail[1]).ValueAsLatin1());
        Assert.Equal([100L, 200L, 400L, 650L],
            detail.Skip(2).Select(value => Assert.IsType<PdfInteger>(value).Value));
        Assert.Equal("FitH", Assert.IsType<PdfName>(width[1]).ValueAsLatin1());
        Assert.Equal(760, Assert.IsType<PdfInteger>(width[2]).Value);
    }

    [Fact]
    public void Destinations_ValidateCoordinatesZoomAndPages()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDestination.At(zoom: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfDestination.FitRectangle(10, 10, 5, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PdfDocumentBuilder().AddBlankPage().SetOpenAction(1, PdfDestination.FitPage()));
        Assert.Throws<ArgumentNullException>(() =>
            new PdfDocumentBuilder().AddBlankPage().SetOpenAction(0, null!));
    }

    [Fact]
    public void PdfUa2_RejectsUnstructuredOpenAction()
    {
        Assert.Throws<InvalidOperationException>(() => new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata { Title = "UA", Language = "en-US" })
            .EnablePdfUa2Conformance()
            .AddBlankPage()
            .SetOpenAction(0, PdfDestination.FitPage())
            .AddStructureContainer(PdfStructureType.Document)
            .Build());
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
