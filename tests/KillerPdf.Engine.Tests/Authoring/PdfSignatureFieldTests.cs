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

    [Fact]
    public void AddSignatureField_WritesVisibleUnsignedAppearanceText()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "signature", 0, 0, 180, 40,
                appearanceText: "Sign here", fontSize: 11)
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary signature = ResolveDictionary(document, Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfStream stream = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfDictionary>(signature[Name("AP")])[Name("N")])));

        Assert.Contains("Sign here", Encoding.Latin1.GetString(stream.EncodedData.Span));
        Assert.True(Assert.IsType<PdfDictionary>(stream.Dictionary[Name("Resources")])
            .ContainsKey(Name("Font")));
    }

    [Fact]
    public void AddSignatureField_ValidatesAppearanceText()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();

        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "empty", 0, 0, 100, 30, appearanceText: " "));
        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "unicode", 0, 0, 100, 30, appearanceText: "签名"));
    }

    [Theory]
    [InlineData(PdfSignatureLockAction.Include, "Include")]
    [InlineData(PdfSignatureLockAction.Exclude, "Exclude")]
    public void AddSignatureField_WritesValidatedFieldLock(
        PdfSignatureLockAction action, string expectedAction)
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "name", 0, 0, 100, 20)
            .AddCheckBox(0, "approved", 0, 30, 20, 20)
            .AddSignatureField(0, "signature", 0, 60, 140, 40,
                fieldLock: new PdfSignatureFieldLock(action, ["name", "approved"]))
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfArray fields = Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")]);
        PdfDictionary signature = ResolveDictionary(document, fields[2]);
        PdfDictionary fieldLock = Assert.IsType<PdfDictionary>(signature[Name("Lock")]);

        Assert.Equal("SigFieldLock", Assert.IsType<PdfName>(fieldLock[Name("Type")]).ValueAsLatin1());
        Assert.Equal(expectedAction, Assert.IsType<PdfName>(fieldLock[Name("Action")]).ValueAsLatin1());
        Assert.Equal(["name", "approved"], Assert.IsType<PdfArray>(fieldLock[Name("Fields")])
            .Select(value => DecodeUnicode(Assert.IsType<PdfString>(value))));
    }

    [Fact]
    public void AddSignatureField_ValidatesLockShapeAndFieldNames()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage()
            .AddTextField(0, "name", 0, 0, 100, 20);
        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "all", 0, 30, 100, 30,
            fieldLock: new PdfSignatureFieldLock(PdfSignatureLockAction.All, ["name"])));
        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "empty", 0, 30, 100, 30,
            fieldLock: new PdfSignatureFieldLock(PdfSignatureLockAction.Include)));
        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "missing", 0, 30, 100, 30,
            fieldLock: new PdfSignatureFieldLock(PdfSignatureLockAction.Exclude, ["missing"])));
    }

    [Fact]
    public void AddSignatureField_WritesDigestAndReasonSeedValues()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "signature", 0, 0, 140, 40,
                seedValue: new PdfSignatureSeedValue
                {
                    SubFilters = [
                        PdfSignatureSubFilter.AdobePkcs7Detached,
                        PdfSignatureSubFilter.EtsiCadesDetached],
                    RequireSubFilter = true,
                    DigestMethods = [PdfSignatureDigestMethod.Sha256, PdfSignatureDigestMethod.Sha512],
                    RequireDigestMethod = true,
                    Reasons = ["Approved", "Reviewed"],
                    RequireReason = true,
                    CertificationPermission =
                        PdfSignatureCertificationPermission.FormFillingAndSignatures
                })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary signature = ResolveDictionary(document, Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);
        PdfDictionary seed = Assert.IsType<PdfDictionary>(signature[Name("SV")]);

        Assert.Equal(74, Assert.IsType<PdfInteger>(seed[Name("Ff")]).Value);
        Assert.Equal(["adbe.pkcs7.detached", "ETSI.CAdES.detached"],
            Assert.IsType<PdfArray>(seed[Name("SubFilter")])
                .Select(value => Assert.IsType<PdfName>(value).ValueAsLatin1()));
        Assert.Equal(["SHA256", "SHA512"], Assert.IsType<PdfArray>(seed[Name("DigestMethod")])
            .Select(value => Assert.IsType<PdfName>(value).ValueAsLatin1()));
        Assert.Equal(["Approved", "Reviewed"], Assert.IsType<PdfArray>(seed[Name("Reasons")])
            .Select(value => DecodeUnicode(Assert.IsType<PdfString>(value))));
        Assert.Equal(2, Assert.IsType<PdfInteger>(
            Assert.IsType<PdfDictionary>(seed[Name("MDP")])[Name("P")]).Value);
    }

    [Fact]
    public void AddSignatureField_ValidatesSeedValueShape()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();

        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "empty", 0, 0, 100, 30, seedValue: new PdfSignatureSeedValue()));
        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "digest", 0, 0, 100, 30, seedValue: new PdfSignatureSeedValue
            {
                RequireDigestMethod = true
            }));
        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "subfilter", 0, 0, 100, 30, seedValue: new PdfSignatureSeedValue
            {
                RequireSubFilter = true
            }));
        Assert.Throws<ArgumentException>(() => builder.AddSignatureField(
            0, "reasons", 0, 0, 100, 30, seedValue: new PdfSignatureSeedValue
            {
                Reasons = ["Approved", "Approved"]
            }));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddSignatureField(
            0, "permission", 0, 0, 100, 30, seedValue: new PdfSignatureSeedValue
            {
                CertificationPermission = (PdfSignatureCertificationPermission)9
            }));
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
