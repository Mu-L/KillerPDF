using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfSignatureFieldTests
{
    [Fact]
    public void AddSignatureField_WritesUnsignedWidgetAndAcroFormFlags()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval.signature", 72, 100, 220, 60,
                new PdfFormFieldMetadata
                {
                    Tooltip = "Approval signature",
                    MappingName = "approval_signature"
                },
                new PdfFormFieldOptions { Required = true })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary acroForm = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfDictionary field = ResolveDictionary(document, Assert.IsType<PdfArray>(acroForm[Name("Fields")])[0]);

        Assert.Equal(1, Assert.IsType<PdfInteger>(acroForm[Name("SigFlags")]).Value);
        Assert.Equal("Sig", Assert.IsType<PdfName>(field[Name("FT")]).ValueAsLatin1());
        Assert.Equal(2, Assert.IsType<PdfInteger>(field[Name("Ff")]).Value);
        Assert.False(field.ContainsKey(Name("V")));
        Assert.Equal("Approval signature", DecodeUnicode(Assert.IsType<PdfString>(field[Name("TU")])));
        Assert.IsType<PdfIndirectReference>(field[Name("P")]);
    }

    [Fact]
    public void AddSignatureField_UsesSharedFieldNameValidation()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage()
            .AddTextField(0, "duplicate", 0, 0, 100, 20);
        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "duplicate", 0, 30, 100, 30));
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
