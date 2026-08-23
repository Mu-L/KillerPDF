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

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
