using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfBookmarkTests
{
    [Fact]
    public void AddBookmark_WritesLinkedOutlineItemsAndDestinations()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddBookmark("Introduction", 0)
            .AddBookmark("Résumé", 1)
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var outlines = ResolveDictionary(document, catalog[Name("Outlines")]);
        var firstReference = Assert.IsType<PdfIndirectReference>(outlines[Name("First")]);
        var lastReference = Assert.IsType<PdfIndirectReference>(outlines[Name("Last")]);
        var first = ResolveDictionary(document, firstReference);
        var last = ResolveDictionary(document, lastReference);
        var pages = ResolveDictionary(document, catalog[Name("Pages")]);
        var pageKids = Assert.IsType<PdfArray>(pages[Name("Kids")]);

        Assert.Equal(2, Assert.IsType<PdfInteger>(outlines[Name("Count")]).Value);
        Assert.Equal("UseOutlines", Assert.IsType<PdfName>(catalog[Name("PageMode")]).ValueAsLatin1());
        Assert.Equal("Introduction", DecodeUnicode(Assert.IsType<PdfString>(first[Name("Title")])));
        Assert.Equal("Résumé", DecodeUnicode(Assert.IsType<PdfString>(last[Name("Title")])));
        Assert.Equal(lastReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(first[Name("Next")]).ObjectNumber);
        Assert.Equal(firstReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(last[Name("Prev")]).ObjectNumber);
        var destination = Assert.IsType<PdfArray>(last[Name("Dest")]);
        Assert.Equal(Assert.IsType<PdfIndirectReference>(pageKids[1]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(destination[0]).ObjectNumber);
    }

    [Fact]
    public void AddBookmark_RejectsMissingTitleOrPage()
    {
        Assert.Throws<ArgumentException>(() =>
            new PdfDocumentBuilder().AddBlankPage().AddBookmark(" ", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PdfDocumentBuilder().AddBlankPage().AddBookmark("Missing", 1));
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
