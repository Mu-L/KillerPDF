using System.Globalization;
using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Signing;
using KillerPdf.Engine.Writing;
using Xunit;

namespace KillerPdf.Engine.Tests.Signing;

public sealed class PdfDetachedSignatureWriterTests
{
    [Fact]
    public void Sign_WritesApprovalFieldAndExactDetachedByteRange()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] cms = [0x30, 0x03, 0x01, 0x02, 0x03];
        byte[]? signedContent = null;
        var options = new PdfSignatureOptions
        {
            FieldName = "Approval",
            SignerName = "Steve the Killer",
            Reason = "Release approval",
            Location = "California",
            ContactInformation = "killerpdf.net",
            SigningTime = new DateTimeOffset(2026, 8, 23, 1, 2, 3, TimeSpan.FromHours(-7)),
            ReservedSignatureSize = 64
        };

        byte[] result = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), bytes =>
            {
                signedContent = bytes.ToArray();
                return cms;
            }, options);
        PdfDocument reopened = PdfDocument.Open(result);
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfArray fields = Assert.IsType<PdfArray>(form[Name("Fields")]);
        PdfIndirectReference fieldReference = Assert.IsType<PdfIndirectReference>(fields[0]);
        PdfDictionary field = ResolveDictionary(reopened, fieldReference);
        PdfDictionary signature = ResolveDictionary(reopened, field[Name("V")]);
        PdfArray byteRange = Assert.IsType<PdfArray>(signature[Name("ByteRange")]);
        long[] ranges = byteRange.Select(value => Assert.IsType<PdfInteger>(value).Value).ToArray();
        PdfString contents = Assert.IsType<PdfString>(signature[Name("Contents")]);
        PdfDictionary pages = ResolveDictionary(reopened, catalog[Name("Pages")]);
        PdfDictionary page = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);
        PdfArray annotations = Assert.IsType<PdfArray>(page[Name("Annots")]);

        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
        Assert.Equal([0L, ranges[1], ranges[2], result.Length - ranges[2]], ranges);
        Assert.Equal(result.Length - (ranges[2] - ranges[1]), signedContent!.Length);
        byte[] reconstructed = [
            .. result.AsSpan(0, checked((int)ranges[1])).ToArray(),
            .. result.AsSpan(checked((int)ranges[2])).ToArray()];
        Assert.Equal(reconstructed, signedContent);
        Assert.Equal(64, contents.Bytes.Length);
        Assert.Equal(cms, contents.Bytes.Span[..cms.Length].ToArray());
        Assert.All(contents.Bytes.Span[cms.Length..].ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(3, Assert.IsType<PdfInteger>(form[Name("SigFlags")]).Value);
        Assert.Equal("Sig", Assert.IsType<PdfName>(field[Name("FT")]).ValueAsLatin1());
        Assert.Equal("Approval", DecodeUnicode(Assert.IsType<PdfString>(field[Name("T")])));
        Assert.Equal(fieldReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(annotations[0]).ObjectNumber);
        Assert.Equal("ETSI.CAdES.detached",
            Assert.IsType<PdfName>(signature[Name("SubFilter")]).ValueAsLatin1());
        Assert.Equal("Steve the Killer", DecodeUnicode(
            Assert.IsType<PdfString>(signature[Name("Name")])));
        Assert.Equal("Release approval", DecodeUnicode(
            Assert.IsType<PdfString>(signature[Name("Reason")])));
        Assert.Equal("D:20260823010203-07'00'", Encoding.Latin1.GetString(
            Assert.IsType<PdfString>(signature[Name("M")]).Bytes.Span));
    }

    [Fact]
    public void Sign_PreservesExistingFormFieldsAnnotationsAndFlags()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddCheckBox(0, "approved", 20, 20, 12, 12, isChecked: true)
            .Build();

        PdfDocument reopened = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                FieldName = "signature",
                ReservedSignatureSize = 8
            }));
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfArray fields = Assert.IsType<PdfArray>(form[Name("Fields")]);
        PdfDictionary pages = ResolveDictionary(reopened, catalog[Name("Pages")]);
        PdfDictionary page = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);

        Assert.Equal(2, fields.Count);
        Assert.Equal("approved", DecodeUnicode(Assert.IsType<PdfString>(
            ResolveDictionary(reopened, fields[0])[Name("T")])));
        Assert.Equal("signature", DecodeUnicode(Assert.IsType<PdfString>(
            ResolveDictionary(reopened, fields[1])[Name("T")])));
        Assert.Equal(2, Assert.IsType<PdfArray>(page[Name("Annots")]).Count);
        Assert.Equal(3, Assert.IsType<PdfInteger>(form[Name("SigFlags")]).Value);
    }

    [Fact]
    public void Sign_FillsExistingUnsignedSignatureFieldInPlace()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddTextField(0, "name", 20, 20, 100, 20)
            .AddSignatureField(0, "approval.signature", 20, 60, 180, 44,
                fieldLock: new PdfSignatureFieldLock(
                    PdfSignatureLockAction.Include, ["name"],
                    PdfSignatureLockPermission.NoChanges),
                seedValue: new PdfSignatureSeedValue
                {
                    DigestMethods = [PdfSignatureDigestMethod.Sha256],
                    RequireDigestMethod = true
                },
                appearanceText: "Sign here")
            .Build();
        PdfDocument original = PdfDocument.Open(source);
        PdfDictionary originalCatalog = ResolveDictionary(original, original.Trailer[Name("Root")]);
        PdfDictionary originalForm = Assert.IsType<PdfDictionary>(originalCatalog[Name("AcroForm")]);
        PdfArray originalFields = Assert.IsType<PdfArray>(originalForm[Name("Fields")]);
        PdfIndirectReference fieldReference = Assert.IsType<PdfIndirectReference>(originalFields[1]);
        PdfDictionary originalField = ResolveDictionary(original, fieldReference);
        PdfIndirectReference appearanceReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfDictionary>(originalField[Name("AP")])[Name("N")]);
        PdfIndirectReference lockReference = Assert.IsType<PdfIndirectReference>(
            originalField[Name("Lock")]);
        PdfIndirectReference seedReference = Assert.IsType<PdfIndirectReference>(
            originalField[Name("SV")]);

        PdfDocument signed = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            original, _ => [1, 2, 3], new PdfSignatureOptions
            {
                FieldName = "approval.signature",
                PageIndex = 999,
                ReservedSignatureSize = 16
            }));
        PdfDictionary catalog = ResolveDictionary(signed, signed.Trailer[Name("Root")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfArray fields = Assert.IsType<PdfArray>(form[Name("Fields")]);
        PdfDictionary field = ResolveDictionary(signed, fields[1]);

        Assert.Equal(2, fields.Count);
        Assert.Equal(fieldReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(fields[1]).ObjectNumber);
        Assert.Equal(appearanceReference.ObjectNumber, Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfDictionary>(field[Name("AP")])[Name("N")]).ObjectNumber);
        Assert.Equal(lockReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(field[Name("Lock")]).ObjectNumber);
        Assert.Equal(seedReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(field[Name("SV")]).ObjectNumber);
        PdfDictionary signature = ResolveDictionary(signed,
            Assert.IsType<PdfIndirectReference>(field[Name("V")]));
        PdfDictionary fieldMdp = Assert.IsType<PdfDictionary>(Assert.Single(
            Assert.IsType<PdfArray>(signature[Name("Reference")])));
        PdfDictionary transformParameters = Assert.IsType<PdfDictionary>(
            fieldMdp[Name("TransformParams")]);
        Assert.Equal("FieldMDP", Assert.IsType<PdfName>(
            fieldMdp[Name("TransformMethod")]).ValueAsLatin1());
        Assert.Equal("TransformParams", Assert.IsType<PdfName>(
            transformParameters[Name("Type")]).ValueAsLatin1());
        Assert.Equal("Include", Assert.IsType<PdfName>(
            transformParameters[Name("Action")]).ValueAsLatin1());
        Assert.Equal("name", DecodeUnicode(Assert.IsType<PdfString>(Assert.Single(
            Assert.IsType<PdfArray>(transformParameters[Name("Fields")])))));
        Assert.Equal(1, Assert.IsType<PdfInteger>(transformParameters[Name("P")]).Value);
        Assert.Equal("1.2", Assert.IsType<PdfName>(
            transformParameters[Name("V")]).ValueAsLatin1());
        Assert.Equal(3, Assert.IsType<PdfInteger>(form[Name("SigFlags")]).Value);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Sign_HonorsCertificationPermissionsThatAllowSigning(int permission)
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval", 20, 20, 160, 40)
            .Build();
        PdfDocument certified = PdfDocument.Open(AddCertificationPermission(source, permission));

        PdfDocument signed = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            certified, _ => [1], new PdfSignatureOptions
            {
                FieldName = "approval",
                ReservedSignatureSize = 8
            }));
        PdfDictionary catalog = ResolveDictionary(signed, signed.Trailer[Name("Root")]);
        PdfDictionary field = ResolveDictionary(signed, Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);

        Assert.IsType<PdfIndirectReference>(field[Name("V")]);
        Assert.True(catalog.ContainsKey(Name("Perms")));
    }

    [Fact]
    public void Sign_RejectsCertificationThatForbidsChangesOrNeedsANewField()
    {
        byte[] withField = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval", 20, 20, 160, 40)
            .Build();
        byte[] withoutField = new PdfDocumentBuilder().AddBlankPage().Build();

        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(AddCertificationPermission(withField, 1)), _ => [1],
            new PdfSignatureOptions { FieldName = "approval", ReservedSignatureSize = 8 }));
        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(AddCertificationPermission(withoutField, 2)), _ => [1],
            new PdfSignatureOptions { FieldName = "new-signature", ReservedSignatureSize = 8 }));
    }

    [Theory]
    [InlineData(PdfSignatureCertificationPermission.NoChanges, 1)]
    [InlineData(PdfSignatureCertificationPermission.FormFillingAndSignatures, 2)]
    [InlineData(PdfSignatureCertificationPermission.FormFillingSignaturesAndAnnotations, 3)]
    public void Sign_WritesCertificationSignatureAndDocMdpTransform(
        PdfSignatureCertificationPermission permission, int expectedPermission)
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "certification", 20, 20, 160, 40)
            .Build();

        PdfDocument signed = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1, 2, 3], new PdfSignatureOptions
            {
                FieldName = "certification",
                CertificationPermission = permission,
                ReservedSignatureSize = 16
            }));
        PdfDictionary catalog = ResolveDictionary(signed, signed.Trailer[Name("Root")]);
        PdfDictionary permissions = Assert.IsType<PdfDictionary>(catalog[Name("Perms")]);
        PdfIndirectReference certificationReference = Assert.IsType<PdfIndirectReference>(
            permissions[Name("DocMDP")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfDictionary field = ResolveDictionary(signed,
            Assert.IsType<PdfArray>(form[Name("Fields")])[0]);
        PdfIndirectReference fieldValue = Assert.IsType<PdfIndirectReference>(field[Name("V")]);
        PdfDictionary signature = ResolveDictionary(signed, certificationReference);
        PdfArray references = Assert.IsType<PdfArray>(signature[Name("Reference")]);
        PdfDictionary reference = Assert.IsType<PdfDictionary>(Assert.Single(references));
        PdfDictionary parameters = Assert.IsType<PdfDictionary>(reference[Name("TransformParams")]);

        Assert.Equal(fieldValue.ObjectNumber, certificationReference.ObjectNumber);
        Assert.Equal("SigRef", Assert.IsType<PdfName>(reference[Name("Type")]).ValueAsLatin1());
        Assert.Equal("DocMDP", Assert.IsType<PdfName>(
            reference[Name("TransformMethod")]).ValueAsLatin1());
        Assert.Equal("TransformParams", Assert.IsType<PdfName>(
            parameters[Name("Type")]).ValueAsLatin1());
        Assert.Equal(expectedPermission,
            Assert.IsType<PdfInteger>(parameters[Name("P")]).Value);
        Assert.Equal("1.2", Assert.IsType<PdfName>(parameters[Name("V")]).ValueAsLatin1());
        Assert.Equal(4, Assert.IsType<PdfArray>(signature[Name("ByteRange")]).Count);
    }

    [Fact]
    public void Sign_CreatesCertificationFieldAndPermissionsInOneCatalogUpdate()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();

        PdfDocument signed = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                FieldName = "certification",
                CertificationPermission =
                    PdfSignatureCertificationPermission.FormFillingAndSignatures,
                ReservedSignatureSize = 8
            }));
        PdfDictionary catalog = ResolveDictionary(signed, signed.Trailer[Name("Root")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfDictionary permissions = Assert.IsType<PdfDictionary>(catalog[Name("Perms")]);
        PdfDictionary field = ResolveDictionary(signed,
            Assert.IsType<PdfArray>(form[Name("Fields")])[0]);

        Assert.Equal(Assert.IsType<PdfIndirectReference>(field[Name("V")]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(permissions[Name("DocMDP")]).ObjectNumber);
        Assert.Equal(3, Assert.IsType<PdfInteger>(form[Name("SigFlags")]).Value);
    }

    [Fact]
    public void Sign_RejectsCertificationAfterAnySignatureOrExistingCertification()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "first", 20, 20, 160, 40)
            .AddSignatureField(0, "second", 20, 80, 160, 40)
            .Build();
        byte[] approved = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                FieldName = "first",
                ReservedSignatureSize = 8
            });
        byte[] certified = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                FieldName = "first",
                CertificationPermission = PdfSignatureCertificationPermission.NoChanges,
                ReservedSignatureSize = 8
            });

        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(approved), _ => [1], new PdfSignatureOptions
            {
                FieldName = "second",
                CertificationPermission = PdfSignatureCertificationPermission.FormFillingAndSignatures,
                ReservedSignatureSize = 8
            }));
        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(certified), _ => [1], new PdfSignatureOptions
            {
                FieldName = "second",
                CertificationPermission = PdfSignatureCertificationPermission.FormFillingAndSignatures,
                ReservedSignatureSize = 8
            }));
    }

    [Theory]
    [InlineData(PdfSignatureCertificationPermission.ApprovalSignature)]
    [InlineData((PdfSignatureCertificationPermission)99)]
    public void Sign_RejectsInvalidCertificationPermission(
        PdfSignatureCertificationPermission permission)
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();

        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                CertificationPermission = permission,
                ReservedSignatureSize = 8
            }));
    }

    [Fact]
    public void Sign_UpdatesIndirectAcroFormAndFieldArrayWithoutReplacingTheCatalog()
    {
        byte[] source = BuildIndirectAcroFormDocument();
        PdfDocument original = PdfDocument.Open(source);
        PdfDictionary originalCatalog = ResolveDictionary(
            original, original.Trailer[Name("Root")]);
        int catalogNumber = Assert.IsType<PdfIndirectReference>(
            original.Trailer[Name("Root")]).ObjectNumber;

        PdfDocument reopened = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            original, _ => [1, 2, 3], new PdfSignatureOptions
            {
                FieldName = "approval",
                ReservedSignatureSize = 16
            }));
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfIndirectReference formReference = Assert.IsType<PdfIndirectReference>(
            catalog[Name("AcroForm")]);
        PdfDictionary form = ResolveDictionary(reopened, formReference);
        PdfArray fields = Assert.IsType<PdfArray>(form[Name("Fields")]);

        Assert.Equal(5, formReference.ObjectNumber);
        Assert.Equal(2, fields.Count);
        Assert.Equal(3, Assert.IsType<PdfInteger>(form[Name("SigFlags")]).Value);
        Assert.Equal(catalogNumber, Assert.IsType<PdfIndirectReference>(
            reopened.Trailer[Name("Root")]).ObjectNumber);
        Assert.Equal(originalCatalog[Name("Pages")].GetType(), catalog[Name("Pages")].GetType());
    }

    [Fact]
    public void Sign_IsDeterministicForDeterministicCmsAndOptions()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        var options = new PdfSignatureOptions
        {
            SigningTime = DateTimeOffset.UnixEpoch,
            ReservedSignatureSize = 16
        };
        byte[] Sign() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), bytes => [.. bytes.Span[..8]], options);

        Assert.Equal(Sign(), Sign());
    }

    [Fact]
    public void Sign_RejectsUnsafeDocumentsOptionsAndSignerResults()
    {
        byte[] ordinary = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] duplicateField = new PdfDocumentBuilder()
            .AddBlankPage().AddCheckBox(0, "Signature1", 0, 0, 10, 10).Build();
        var taggedContent = new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Figure, 0)
            .Rectangle(0, 0, 10, 10).Fill().EndMarkedContent();
        byte[] tagged = new PdfDocumentBuilder()
            .AddPage(100, 100, taggedContent)
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
                alternateDescription: "Square")
            .Build();

        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(duplicateField), _ => [1]));
        Assert.Throws<NotSupportedException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(tagged), _ => [1]));
        Assert.Throws<ArgumentException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(ordinary), _ => [1],
            new PdfSignatureOptions { FieldName = "bad.name" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(ordinary), _ => [1],
            new PdfSignatureOptions { PageIndex = 1 }));
        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(ordinary), _ => [],
            new PdfSignatureOptions { ReservedSignatureSize = 8 }));
        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(ordinary), _ => new byte[9],
            new PdfSignatureOptions { ReservedSignatureSize = 8 }));
    }

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));

    private static byte[] BuildIndirectAcroFormDocument()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        var offsets = new int[7];
        Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm 5 0 R >>");
        Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] " +
            "/Resources <<>> /Annots [4 0 R] >>");
        Add(4, "<< /Type /Annot /Subtype /Widget /FT /Btn /T (existing) " +
            "/Rect [10 10 20 20] /P 3 0 R >>");
        Add(5, "<< /Fields 6 0 R /SigFlags 1 >>");
        Add(6, "[4 0 R]");
        int xrefOffset = source.Length;
        source.Append("xref\n0 7\n0000000000 65535 f \n");
        for (int index = 1; index <= 6; index++)
            source.Append(offsets[index].ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        source.Append("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());

        void Add(int number, string value)
        {
            offsets[number] = source.Length;
            source.Append(number).Append(" 0 obj\n").Append(value).Append("\nendobj\n");
        }
    }

    private static byte[] AddCertificationPermission(byte[] source, int permission)
    {
        PdfDocument document = PdfDocument.Open(source);
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference parameters = update.AddObject(Dictionary(
            ("Type", Name("TransformParams")),
            ("P", new PdfInteger(permission)),
            ("V", Name("1.2"))));
        PdfIndirectReference reference = update.AddObject(Dictionary(
            ("Type", Name("SigRef")),
            ("TransformMethod", Name("DocMDP")),
            ("TransformParams", parameters)));
        PdfIndirectReference signature = update.AddObject(Dictionary(
            ("Type", Name("Sig")),
            ("Reference", new PdfArray([reference]))));
        PdfDictionary permissions = Dictionary(("DocMDP", signature));
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            document.Trailer[Name("Root")]);
        update.ReplaceObject(catalogReference.ObjectNumber,
            new PdfDictionary(catalog.Where(entry => !entry.Key.Equals(Name("Perms")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("Perms"), permissions))));
        return update.Build();
    }

    private static PdfDictionary Dictionary(params (string Name, PdfObject Value)[] entries) =>
        new(entries.Select(entry =>
            new KeyValuePair<PdfName, PdfObject>(Name(entry.Name), entry.Value)));
}
