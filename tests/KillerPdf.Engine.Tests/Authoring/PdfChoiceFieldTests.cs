using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfChoiceFieldTests
{
    [Theory]
    [InlineData(false, 1 << 17)]
    [InlineData(true, (1 << 17) | (1 << 18))]
    public void AddComboBox_WritesOptionsValueFlagsAndAppearance(bool editable, int expectedFlags)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddComboBox(0, "theme", 72, 650, 180, 24,
                ["Dark", "Mourning", "98SE"], "Mourning", editable)
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var acroForm = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        var field = ResolveDictionary(document, Assert.IsType<PdfArray>(acroForm[Name("Fields")])[0]);
        var options = Assert.IsType<PdfArray>(field[Name("Opt")]);
        var appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")])));

        Assert.Equal("Ch", Assert.IsType<PdfName>(field[Name("FT")]).ValueAsLatin1());
        Assert.Equal(expectedFlags, Assert.IsType<PdfInteger>(field[Name("Ff")]).Value);
        Assert.Equal("Mourning", DecodeUnicode(Assert.IsType<PdfString>(field[Name("V")])));
        Assert.Equal(new[] { "Dark", "Mourning", "98SE" },
            options.Select(value => DecodeUnicode(Assert.IsType<PdfString>(value))));
        Assert.Contains("(Mourning) Tj", Encoding.ASCII.GetString(appearance.EncodedData.Span));
    }

    [Fact]
    public void AddComboBox_ValidatesOptionsAndNonEditableSelection()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        Assert.Throws<ArgumentException>(() => builder.AddComboBox(
            0, "empty", 0, 0, 100, 20, []));
        Assert.Throws<ArgumentException>(() => builder.AddComboBox(
            0, "duplicate", 0, 0, 100, 20, ["A", "A"]));
        Assert.Throws<ArgumentException>(() => builder.AddComboBox(
            0, "selection", 0, 0, 100, 20, ["A", "B"], "C"));
    }

    [Fact]
    public void AddListBox_WritesChoiceFieldWithoutComboFlag()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddListBox(0, "theme", 72, 600, 180, 72,
                ["Dark", "Mourning", "98SE"], "98SE",
                fieldMetadata: new PdfFormFieldMetadata { Tooltip = "Theme list" },
                fieldOptions: new PdfFormFieldOptions { Required = true })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);

        Assert.Equal("Ch", Assert.IsType<PdfName>(field[Name("FT")]).ValueAsLatin1());
        Assert.Equal(1 << 1, Assert.IsType<PdfInteger>(field[Name("Ff")]).Value);
        Assert.Equal("98SE", DecodeUnicode(Assert.IsType<PdfString>(field[Name("V")])));
        Assert.Equal("Theme list", DecodeUnicode(Assert.IsType<PdfString>(field[Name("TU")])));
        PdfStream appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")])));
        string content = Encoding.ASCII.GetString(appearance.EncodedData.Span);
        Assert.Contains("(Dark) Tj", content);
        Assert.Contains("(Mourning) Tj", content);
        Assert.Contains("(98SE) Tj", content);
    }

    [Fact]
    public void AddListBox_ValidatesOptionsAndSelection()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        Assert.Throws<ArgumentException>(() => builder.AddListBox(0, "empty", 0, 0, 100, 40, []));
        Assert.Throws<ArgumentException>(() => builder.AddListBox(0, "duplicate", 0, 0, 100, 40, ["A", "A"]));
        Assert.Throws<ArgumentException>(() => builder.AddListBox(0, "selection", 0, 0, 100, 40, ["A", "B"], "C"));
    }

    [Fact]
    public void AddMultiSelectListBox_WritesOrderedValuesIndicesAndHighlights()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddMultiSelectListBox(0, "features", 20, 20, 160, 80,
                ["Links", "Forms", "PDF/A", "PDF/UA"], ["PDF/UA", "Forms"])
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfArray values = Assert.IsType<PdfArray>(field[Name("V")]);
        PdfArray indices = Assert.IsType<PdfArray>(field[Name("I")]);

        Assert.Equal(1 << 21, Assert.IsType<PdfInteger>(field[Name("Ff")]).Value);
        Assert.Equal(["Forms", "PDF/UA"],
            values.Select(value => DecodeUnicode(Assert.IsType<PdfString>(value))));
        Assert.Equal([1L, 3L], indices.Select(value => Assert.IsType<PdfInteger>(value).Value));
        PdfStream appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")])));
        Assert.Equal(2, Encoding.ASCII.GetString(appearance.EncodedData.Span)
            .Split("0.75 g", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AddMultiSelectListBox_RejectsInvalidSelections()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        Assert.Throws<ArgumentException>(() => builder.AddMultiSelectListBox(
            0, "duplicate", 0, 0, 100, 40, ["A", "B"], ["A", "A"]));
        Assert.Throws<ArgumentException>(() => builder.AddMultiSelectListBox(
            0, "unknown", 0, 0, 100, 40, ["A", "B"], ["C"]));
    }

    [Fact]
    public void AddListBox_WritesAndRendersFromTopIndex()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddListBox(0, "scrolled", 10, 10, 100, 40,
                ["A", "B", "C", "D"], "C", topIndex: 2)
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(document,
            Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        Assert.Equal(2, Assert.IsType<PdfInteger>(field[Name("TI")]).Value);
        PdfStream appearance = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")])));
        string content = Encoding.ASCII.GetString(appearance.EncodedData.Span);
        Assert.DoesNotContain("(A) Tj", content);
        Assert.DoesNotContain("(B) Tj", content);
        Assert.Contains("(C) Tj", content);
        Assert.Contains("(D) Tj", content);
    }

    [Fact]
    public void AddListBox_RejectsTopIndexOutsideOptions()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddListBox(
            0, "negative", 0, 0, 100, 40, ["A"], topIndex: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddMultiSelectListBox(
            0, "past-end", 0, 0, 100, 40, ["A"], topIndex: 1));
    }

    [Fact]
    public void ChoiceFields_WriteTypedBehaviorAndSortedOptions()
    {
        var behavior = new PdfChoiceFieldOptions
        {
            SortOptions = true,
            DoNotSpellCheck = true,
            CommitOnSelectionChange = true
        };
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddComboBox(0, "combo", 0, 0, 100, 20, ["Zulu", "Alpha"], "Zulu",
                choiceOptions: behavior)
            .AddMultiSelectListBox(0, "list", 0, 30, 100, 60,
                ["Zulu", "Alpha", "Mike"], ["Zulu", "Alpha"], choiceOptions: behavior)
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfArray fields = Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")]);
        int behaviorFlags = (1 << 19) | (1 << 22) | (1 << 26);
        PdfDictionary combo = ResolveDictionary(document, fields[0]);
        PdfDictionary list = ResolveDictionary(document, fields[1]);

        Assert.Equal((1 << 17) | behaviorFlags,
            Assert.IsType<PdfInteger>(combo[Name("Ff")]).Value);
        Assert.Equal((1 << 21) | behaviorFlags,
            Assert.IsType<PdfInteger>(list[Name("Ff")]).Value);
        Assert.Equal(["Alpha", "Zulu"], Assert.IsType<PdfArray>(combo[Name("Opt")])
            .Select(value => DecodeUnicode(Assert.IsType<PdfString>(value))));
        Assert.Equal([0L, 2L], Assert.IsType<PdfArray>(list[Name("I")])
            .Select(value => Assert.IsType<PdfInteger>(value).Value));
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
