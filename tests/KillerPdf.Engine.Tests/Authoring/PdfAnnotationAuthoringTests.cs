using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfAnnotationAuthoringTests
{
    [Fact]
    public void AddTextNote_WritesContentsIdentityColorAndAppearance()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextNote(0, 72, 650, "Review résumé", new PdfRgbColor(1, 0.5, 0), open: true,
                annotationMetadata: new PdfAnnotationMetadata
                {
                    Author = "Zoë",
                    Subject = "Copy review",
                    Flags = PdfAnnotationFlags.Print | PdfAnnotationFlags.Locked
                })
            .Build());
        PdfDictionary annotation = Annotation(document, 0);
        PdfStream appearance = Appearance(document, annotation);

        Assert.Equal("Text", Assert.IsType<PdfName>(annotation[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal("Review résumé", DecodeUnicode(Assert.IsType<PdfString>(annotation[Name("Contents")])));
        Assert.True(Assert.IsType<PdfBoolean>(annotation[Name("Open")]).Value);
        Assert.Equal("KillerPDF-Note-1",
            Encoding.Latin1.GetString(Assert.IsType<PdfString>(annotation[Name("NM")]).Bytes.Span));
        Assert.Equal("Zoë", DecodeUnicode(Assert.IsType<PdfString>(annotation[Name("T")])));
        Assert.Equal("Copy review",
            DecodeUnicode(Assert.IsType<PdfString>(annotation[Name("Subj")])));
        Assert.Equal(132, Assert.IsType<PdfInteger>(annotation[Name("F")]).Value);
        Assert.Contains("1 0.5 0 rg", Encoding.ASCII.GetString(appearance.EncodedData.Span));
    }

    [Fact]
    public void AddHighlight_WritesQuadPointsOpacityAndMultiplyAppearance()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddHighlight(0, 10, 20, 100, 15, "Important", opacity: 0.4)
            .Build());
        PdfDictionary annotation = Annotation(document, 0);
        PdfStream appearance = Appearance(document, annotation);
        var quadPoints = Assert.IsType<PdfArray>(annotation[Name("QuadPoints")]);
        var resources = Assert.IsType<PdfDictionary>(appearance.Dictionary[Name("Resources")]);
        var states = Assert.IsType<PdfDictionary>(resources[Name("ExtGState")]);
        var state = Assert.IsType<PdfDictionary>(states[Name("GS1")]);

        Assert.Equal("Highlight", Assert.IsType<PdfName>(annotation[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(8, quadPoints.Count);
        Assert.Equal(0.4, Assert.IsType<PdfReal>(annotation[Name("CA")]).Value);
        Assert.Equal("Multiply", Assert.IsType<PdfName>(state[Name("BM")]).ValueAsLatin1());
        Assert.Contains("/GS1 gs", Encoding.ASCII.GetString(appearance.EncodedData.Span));
    }

    [Theory]
    [InlineData(PdfTextNoteIcon.Note, "Note")]
    [InlineData(PdfTextNoteIcon.Comment, "Comment")]
    [InlineData(PdfTextNoteIcon.Key, "Key")]
    [InlineData(PdfTextNoteIcon.Help, "Help")]
    [InlineData(PdfTextNoteIcon.NewParagraph, "NewParagraph")]
    [InlineData(PdfTextNoteIcon.Paragraph, "Paragraph")]
    [InlineData(PdfTextNoteIcon.Insert, "Insert")]
    public void AddTextNote_WritesEveryStandardIconName(
        PdfTextNoteIcon icon, string expectedName)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextNote(0, 10, 10, "Note", icon: icon)
            .Build());

        Assert.Equal(expectedName, Assert.IsType<PdfName>(
            Annotation(document, 0)[Name("Name")]).ValueAsLatin1());
    }

    [Fact]
    public void AnnotationArguments_AreValidatedBeforeAllocation()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddTextNote(0, 0, 0, "note", size: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddHighlight(0, 0, 0, 10, 10, opacity: 1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PdfRgbColor(-0.1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PdfAnnotationMetadata { Flags = (PdfAnnotationFlags)1024 });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddTextNote(0, 0, 0, "note", icon: (PdfTextNoteIcon)99));
    }

    [Theory]
    [InlineData("Underline")]
    [InlineData("StrikeOut")]
    [InlineData("Squiggly")]
    public void TextMarkupStyles_WriteTheirStandardSubtypeAndExplicitAppearance(string subtype)
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        _ = subtype switch
        {
            "Underline" => builder.AddUnderline(0, 10, 20, 100, 15),
            "StrikeOut" => builder.AddStrikeOut(0, 10, 20, 100, 15),
            "Squiggly" => builder.AddSquiggly(0, 10, 20, 100, 15),
            _ => throw new ArgumentOutOfRangeException(nameof(subtype))
        };
        PdfDocument document = PdfDocument.Open(builder.Build());
        PdfDictionary annotation = Annotation(document, 0);

        Assert.Equal(subtype, Assert.IsType<PdfName>(annotation[Name("Subtype")]).ValueAsLatin1());
        Assert.True(Appearance(document, annotation).EncodedData.Length > 0);
    }

    [Fact]
    public void AnnotationAuthoring_IsDeterministic()
    {
        static byte[] Build() => new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextNote(0, 30, 700, "Note")
            .AddHighlight(0, 70, 650, 200, 20, "Markup")
            .AddUnderline(0, 70, 610, 200, 20)
            .Build();

        Assert.Equal(Build(), Build());
    }

    private static PdfDictionary Annotation(PdfDocument document, int index)
    {
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var pages = ResolveDictionary(document, catalog[Name("Pages")]);
        var page = ResolveDictionary(document, Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);
        return ResolveDictionary(document, Assert.IsType<PdfArray>(page[Name("Annots")])[index]);
    }

    private static PdfStream Appearance(PdfDocument document, PdfDictionary annotation) =>
        Assert.IsType<PdfStream>(document.Resolve(Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfDictionary>(annotation[Name("AP")])[Name("N")])));
    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
