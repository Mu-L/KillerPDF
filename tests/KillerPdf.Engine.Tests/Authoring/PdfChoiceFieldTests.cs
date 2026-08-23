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

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
