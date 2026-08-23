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
        Assert.Throws<ArgumentException>(() =>
            new PdfDocumentBuilder().AddBlankPage().AddBookmark("Child", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PdfDocumentBuilder().AddBlankPage().AddBookmark("Invalid", 0, -1));
        Assert.Throws<ArgumentException>(() =>
            new PdfDocumentBuilder().AddBlankPage()
                .AddBookmark("Parent", 0)
                .AddBookmark("Skipped", 0, 2));
    }

    [Fact]
    public void AddBookmark_WritesNestedOutlineHierarchy()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage().AddBlankPage()
            .AddBookmark("Chapter", 0)
            .AddBookmark("Section", 1, 1)
            .AddBookmark("Detail", 2, 2)
            .AddBookmark("Next chapter", 2)
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfIndirectReference rootReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("Outlines")]);
        PdfDictionary root = ResolveDictionary(document, rootReference);
        PdfIndirectReference chapterReference = Assert.IsType<PdfIndirectReference>(root[Name("First")]);
        PdfDictionary chapter = ResolveDictionary(document, chapterReference);
        PdfIndirectReference sectionReference = Assert.IsType<PdfIndirectReference>(chapter[Name("First")]);
        PdfDictionary section = ResolveDictionary(document, sectionReference);
        PdfIndirectReference detailReference = Assert.IsType<PdfIndirectReference>(section[Name("First")]);
        PdfDictionary detail = ResolveDictionary(document, detailReference);
        PdfIndirectReference nextChapterReference = Assert.IsType<PdfIndirectReference>(chapter[Name("Next")]);

        Assert.Equal(4, Assert.IsType<PdfInteger>(root[Name("Count")]).Value);
        Assert.Equal(2, Assert.IsType<PdfInteger>(chapter[Name("Count")]).Value);
        Assert.Equal(1, Assert.IsType<PdfInteger>(section[Name("Count")]).Value);
        Assert.Equal(chapterReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(section[Name("Parent")]).ObjectNumber);
        Assert.Equal(sectionReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(detail[Name("Parent")]).ObjectNumber);
        Assert.Equal(nextChapterReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(root[Name("Last")]).ObjectNumber);
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
