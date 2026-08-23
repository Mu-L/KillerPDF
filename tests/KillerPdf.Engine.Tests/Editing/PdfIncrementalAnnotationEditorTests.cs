using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;
using Xunit;

namespace KillerPdf.Engine.Tests.Editing;

public sealed class PdfIncrementalAnnotationEditorTests
{
    [Fact]
    public void Build_AppendsAnnotationsToTheSelectedExistingPage()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().AddBlankPage().Build();
        PdfDocument original = PdfDocument.Open(source);
        var editor = new PdfIncrementalAnnotationEditor(original);

        byte[] result = editor
            .AddTextNote(1, 72, 650, "Review résumé", open: true)
            .AddHighlight(1, 72, 600, 200, 20, "Important", opacity: 0.4)
            .Build();
        PdfDocument reopened = PdfDocument.Open(result);
        IReadOnlyList<(PdfIndirectReference Reference, PdfDictionary Page)> pages = Pages(reopened);
        var annotations = Assert.IsType<PdfArray>(pages[1].Page[Name("Annots")]);
        PdfDictionary note = ResolveDictionary(reopened, annotations[0]);
        PdfDictionary highlight = ResolveDictionary(reopened, annotations[1]);

        Assert.Equal(2, editor.PageCount);
        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
        Assert.False(pages[0].Page.ContainsKey(Name("Annots")));
        Assert.Equal("Text", Assert.IsType<PdfName>(note[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal("Highlight", Assert.IsType<PdfName>(highlight[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(pages[1].Reference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(note[Name("P")]).ObjectNumber);
        Assert.IsType<PdfStream>(reopened.Resolve(Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfDictionary>(highlight[Name("AP")])[Name("N")])));
    }

    [Fact]
    public void Build_PreservesExistingDirectAnnotationArray()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextNote(0, 20, 700, "Existing")
            .Build();
        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(source))
            .AddHighlight(0, 20, 650, 100, 15)
            .Build());
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);

        Assert.Equal(2, annotations.Count);
        Assert.Equal("Text", Assert.IsType<PdfName>(
            ResolveDictionary(reopened, annotations[0])[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal("Highlight", Assert.IsType<PdfName>(
            ResolveDictionary(reopened, annotations[1])[Name("Subtype")]).ValueAsLatin1());
    }

    [Fact]
    public void Build_RevisesAnExistingIndirectAnnotationArray()
    {
        byte[] initial = new PdfDocumentBuilder().AddBlankPage().Build();
        PdfDocument firstDocument = PdfDocument.Open(initial);
        (PdfIndirectReference pageReference, PdfDictionary page) = Pages(firstDocument)[0];
        var setup = new PdfIncrementalUpdateBuilder(firstDocument);
        PdfIndirectReference arrayReference = setup.AddObject(new PdfArray([]));
        setup.ReplaceObject(pageReference.ObjectNumber, Replace(
            page, Name("Annots"), arrayReference));
        byte[] source = setup.Build();

        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(source))
            .AddTextNote(0, 30, 700, "Indirect array")
            .Build());
        PdfDictionary reopenedPage = Pages(reopened)[0].Page;
        PdfIndirectReference reopenedArrayReference = Assert.IsType<PdfIndirectReference>(
            reopenedPage[Name("Annots")]);
        PdfArray annotations = Assert.IsType<PdfArray>(reopened.Resolve(reopenedArrayReference));

        Assert.Equal(arrayReference.ObjectNumber, reopenedArrayReference.ObjectNumber);
        Assert.Single(annotations);
        Assert.Equal(3, reopened.CrossReferences.Sections.Count);
    }

    [Fact]
    public void ArgumentsAndEmptyUpdates_AreRejected()
    {
        var editor = new PdfIncrementalAnnotationEditor(PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build()));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddTextNote(1, 0, 0, "bad"));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddHighlight(0, 0, 0, 10, 10, opacity: -1));
        Assert.Throws<InvalidOperationException>(() => editor.Build());
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        PdfDocument source = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        static byte[] Edit(PdfDocument document) => new PdfIncrementalAnnotationEditor(document)
            .AddTextNote(0, 20, 700, "Note")
            .AddHighlight(0, 20, 650, 100, 15)
            .Build();

        Assert.Equal(Edit(source), Edit(source));
    }

    [Theory]
    [InlineData("Underline")]
    [InlineData("StrikeOut")]
    [InlineData("Squiggly")]
    public void Build_AppendsEveryStandardTextMarkupType(string subtype)
    {
        var editor = new PdfIncrementalAnnotationEditor(PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build()));
        _ = subtype switch
        {
            "Underline" => editor.AddUnderline(0, 20, 600, 100, 15),
            "StrikeOut" => editor.AddStrikeOut(0, 20, 600, 100, 15),
            "Squiggly" => editor.AddSquiggly(0, 20, 600, 100, 15),
            _ => throw new ArgumentOutOfRangeException(nameof(subtype))
        };
        PdfDocument reopened = PdfDocument.Open(editor.Build());
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);
        PdfDictionary annotation = ResolveDictionary(reopened, annotations[0]);

        Assert.Equal(subtype, Assert.IsType<PdfName>(annotation[Name("Subtype")]).ValueAsLatin1());
        Assert.IsType<PdfStream>(reopened.Resolve(Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfDictionary>(annotation[Name("AP")])[Name("N")])));
    }

    private static IReadOnlyList<(PdfIndirectReference Reference, PdfDictionary Page)> Pages(
        PdfDocument document)
    {
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary pages = ResolveDictionary(document, catalog[Name("Pages")]);
        return Assert.IsType<PdfArray>(pages[Name("Kids")]).Select(value =>
        {
            var reference = Assert.IsType<PdfIndirectReference>(value);
            return (reference, ResolveDictionary(document, reference));
        }).ToArray();
    }

    private static PdfDictionary Replace(PdfDictionary source, PdfName name, PdfObject value) =>
        new(source.Where(entry => !entry.Key.Equals(name))
            .Append(new KeyValuePair<PdfName, PdfObject>(name, value)));
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
