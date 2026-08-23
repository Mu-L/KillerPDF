using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfCheckBoxTests
{
    [Theory]
    [InlineData(true, "Approved")]
    [InlineData(false, "Off")]
    public void AddCheckBox_WritesStateAndBothAppearances(bool isChecked, string expectedState)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddCheckBox(0, "approved", 72, 650, 18, 18, isChecked, "Approved")
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var acroForm = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        var fields = Assert.IsType<PdfArray>(acroForm[Name("Fields")]);
        var widget = ResolveDictionary(document, fields[0]);
        var normal = Assert.IsType<PdfDictionary>(
            Assert.IsType<PdfDictionary>(widget[Name("AP")])[Name("N")]);
        var off = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(normal[Name("Off")])));
        var on = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(normal[Name("Approved")])));

        Assert.Equal("Btn", Assert.IsType<PdfName>(widget[Name("FT")]).ValueAsLatin1());
        Assert.Equal(expectedState, Assert.IsType<PdfName>(widget[Name("V")]).ValueAsLatin1());
        Assert.Equal(expectedState, Assert.IsType<PdfName>(widget[Name("AS")]).ValueAsLatin1());
        Assert.DoesNotContain(" m\n", Encoding.ASCII.GetString(off.EncodedData.Span));
        Assert.Contains(" m\n", Encoding.ASCII.GetString(on.EncodedData.Span));
        Assert.False(Assert.IsType<PdfBoolean>(acroForm[Name("NeedAppearances")]).Value);
    }

    [Fact]
    public void FormFieldNames_AreUniqueAcrossTextAndCheckboxTypes()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage()
            .AddTextField(0, "shared", 0, 0, 100, 20);

        Assert.Throws<ArgumentException>(() =>
            builder.AddCheckBox(0, "shared", 0, 30, 20, 20));
    }

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
