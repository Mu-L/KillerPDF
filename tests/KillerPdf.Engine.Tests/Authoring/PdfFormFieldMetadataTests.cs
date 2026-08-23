using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfFormFieldMetadataTests
{
    [Fact]
    public void EveryFieldType_WritesTooltipAndMappingName()
    {
        static PdfFormFieldMetadata Metadata(string value) => new()
        {
            Tooltip = $"{value} tooltip",
            MappingName = $"{value}.export"
        };
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddTextField(0, "text", 10, 10, 100, 20, fieldMetadata: Metadata("text"))
            .AddCheckBox(0, "check", 10, 40, 20, 20, fieldMetadata: Metadata("check"))
            .AddRadioGroup("radio",
            [
                new PdfRadioButtonOption(0, 10, 70, 20, 20, "A"),
                new PdfRadioButtonOption(1, 10, 70, 20, 20, "B")
            ], fieldMetadata: Metadata("radio"))
            .AddComboBox(0, "choice", 10, 100, 100, 20, ["A", "B"],
                fieldMetadata: Metadata("choice"))
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfArray fields = Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")]);

        string[] names = ["text", "check", "radio", "choice"];
        for (int index = 0; index < names.Length; index++)
        {
            PdfDictionary field = ResolveDictionary(document, fields[index]);
            Assert.Equal($"{names[index]} tooltip",
                DecodeUnicode(Assert.IsType<PdfString>(field[Name("TU")])));
            Assert.Equal($"{names[index]}.export",
                DecodeUnicode(Assert.IsType<PdfString>(field[Name("TM")])));
        }
    }

    [Fact]
    public void FieldMetadata_RejectsEmptyNames()
    {
        Assert.Throws<ArgumentException>(() => new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "text", 10, 10, 100, 20,
                fieldMetadata: new PdfFormFieldMetadata { Tooltip = " " }));
    }

    [Fact]
    public void EveryFieldType_WritesCommonFieldFlags()
    {
        var common = new PdfFormFieldOptions { ReadOnly = true, Required = true, NoExport = true };
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "text", 10, 10, 100, 20,
                options: new PdfTextFieldOptions { ReadOnly = true, Required = true, NoExport = true })
            .AddCheckBox(0, "check", 10, 40, 20, 20, options: common)
            .AddRadioGroup("radio",
            [
                new PdfRadioButtonOption(0, 10, 70, 20, 20, "A"),
                new PdfRadioButtonOption(0, 40, 70, 20, 20, "B")
            ], fieldOptions: common)
            .AddComboBox(0, "choice", 10, 100, 100, 20, ["A", "B"], fieldOptions: common)
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfArray fields = Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")]);

        Assert.Equal(7, FieldFlags(document, fields[0]));
        Assert.Equal(7, FieldFlags(document, fields[1]));
        Assert.Equal((1 << 15) | 7, FieldFlags(document, fields[2]));
        Assert.Equal((1 << 17) | 7, FieldFlags(document, fields[3]));
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static long FieldFlags(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfInteger>(ResolveDictionary(document, value)[Name("Ff")]).Value;
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
