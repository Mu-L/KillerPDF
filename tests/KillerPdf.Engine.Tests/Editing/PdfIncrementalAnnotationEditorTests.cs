using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Tests.Fonts;
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

    [Fact]
    public void Build_AppendsEmbeddedFreeTextAndEveryVisualAnnotationType()
    {
        TrueTypeFont font = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(format12: false));
        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(
                new PdfDocumentBuilder().AddBlankPage().Build()))
            .AddFreeText(0, 20, 650, 160, 60, "A\nA", font, fillColor: PdfRgbColor.Yellow)
            .AddLine(0, new PdfPoint(20, 600), new PdfPoint(180, 570), lineWidth: 3)
            .AddRectangle(0, 20, 500, 70, 40, fillColor: PdfRgbColor.Yellow)
            .AddEllipse(0, 110, 500, 70, 40, fillColor: PdfRgbColor.Yellow)
            .AddInk(0,
            [
                [new PdfPoint(20, 450), new PdfPoint(50, 470)],
                [new PdfPoint(80, 450), new PdfPoint(110, 470)]
            ])
            .Build());
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);
        string[] subtypes = annotations.Select(value => Assert.IsType<PdfName>(
            ResolveDictionary(reopened, value)[Name("Subtype")]).ValueAsLatin1()).ToArray();
        PdfDictionary freeText = ResolveDictionary(reopened, annotations[0]);
        PdfStream freeTextAppearance = Assert.IsType<PdfStream>(reopened.Resolve(
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfDictionary>(freeText[Name("AP")])[Name("N")])));
        PdfDictionary fontResources = Assert.IsType<PdfDictionary>(
            Assert.IsType<PdfDictionary>(freeTextAppearance.Dictionary[Name("Resources")])[Name("Font")]);
        PdfDictionary type0 = ResolveDictionary(reopened, fontResources[Name("KpF1")]);
        PdfDictionary ink = ResolveDictionary(reopened, annotations[4]);

        Assert.Equal(["FreeText", "Line", "Square", "Circle", "Ink"], subtypes);
        Assert.Equal("Type0", Assert.IsType<PdfName>(type0[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(2, Assert.IsType<PdfArray>(ink[Name("InkList")]).Count);
    }

    [Fact]
    public void VisualAnnotationArguments_AreRejectedBeforeWriting()
    {
        var editor = new PdfIncrementalAnnotationEditor(PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build()));
        Assert.Throws<ArgumentException>(() => editor.AddLine(
            0, new PdfPoint(1, 1), new PdfPoint(1, 1)));
        Assert.Throws<ArgumentException>(() => editor.AddInk(0, Array.Empty<PdfPoint>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AddRectangle(
            0, 0, 0, 10, 10, lineWidth: 0));
    }

    [Fact]
    public void FreeTextAnnotations_ShareOneDeterministicEmbeddedFontSubset()
    {
        TrueTypeFont font = TrueTypeFont.Load(TrueTypeFontTests.BuildTestFont(format12: false));
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] Build() => new PdfIncrementalAnnotationEditor(PdfDocument.Open(source))
            .AddFreeText(0, 20, 650, 100, 40, "A", font)
            .AddFreeText(0, 140, 650, 100, 40, "AA", font)
            .Build();

        byte[] first = Build();
        PdfDocument reopened = PdfDocument.Open(first);
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);
        PdfIndirectReference FontReference(PdfObject annotationReference)
        {
            PdfDictionary annotation = ResolveDictionary(reopened, annotationReference);
            PdfStream appearance = Assert.IsType<PdfStream>(reopened.Resolve(
                Assert.IsType<PdfIndirectReference>(
                    Assert.IsType<PdfDictionary>(annotation[Name("AP")])[Name("N")])));
            PdfDictionary fonts = Assert.IsType<PdfDictionary>(
                Assert.IsType<PdfDictionary>(appearance.Dictionary[Name("Resources")])[Name("Font")]);
            return Assert.IsType<PdfIndirectReference>(fonts[Name("KpF1")]);
        }

        Assert.Equal(FontReference(annotations[0]).ObjectNumber, FontReference(annotations[1]).ObjectNumber);
        Assert.Equal(first, Build());
    }

    [Fact]
    public void ImageStamps_ShareImageAndSoftMaskObjects()
    {
        PdfImage image = PdfImage.FromRgba(1, 1, new byte[] { 20, 40, 60, 96 });
        PdfDocument reopened = PdfDocument.Open(new PdfIncrementalAnnotationEditor(PdfDocument.Open(
                new PdfDocumentBuilder().AddBlankPage().Build()))
            .AddImageStamp(0, 20, 600, 100, 50, image)
            .AddImageStamp(0, 140, 600, 100, 50, image)
            .Build());
        PdfArray annotations = Assert.IsType<PdfArray>(Pages(reopened)[0].Page[Name("Annots")]);
        PdfIndirectReference ImageReference(PdfObject annotationReference)
        {
            PdfDictionary annotation = ResolveDictionary(reopened, annotationReference);
            PdfStream appearance = Assert.IsType<PdfStream>(reopened.Resolve(
                Assert.IsType<PdfIndirectReference>(
                    Assert.IsType<PdfDictionary>(annotation[Name("AP")])[Name("N")])));
            PdfDictionary xobjects = Assert.IsType<PdfDictionary>(
                Assert.IsType<PdfDictionary>(appearance.Dictionary[Name("Resources")])[Name("XObject")]);
            return Assert.IsType<PdfIndirectReference>(xobjects[Name("Im1")]);
        }
        PdfIndirectReference firstImage = ImageReference(annotations[0]);
        PdfIndirectReference secondImage = ImageReference(annotations[1]);
        PdfStream imageStream = Assert.IsType<PdfStream>(reopened.Resolve(firstImage));

        Assert.Equal(firstImage.ObjectNumber, secondImage.ObjectNumber);
        Assert.IsType<PdfIndirectReference>(imageStream.Dictionary[Name("SMask")]);
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
