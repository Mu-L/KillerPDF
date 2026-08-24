using System.Globalization;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Security;
using KillerPdf.Engine.Signing;
using KillerPdf.Engine.Writing;
using Xunit;

namespace KillerPdf.Engine.Tests.Signing;

public sealed class PdfDetachedSignatureWriterTests
{
    [Fact]
    public void Sign_EnforcesUserPasswordFormPermissionsWhileOwnerBypasses()
    {
        byte[] existingAllowed = EncryptedSignatureDocument(
            includeField: true, allowModification: false,
            allowAnnotations: false, allowFormFilling: true);
        byte[] existingDenied = EncryptedSignatureDocument(
            includeField: true, allowModification: false,
            allowAnnotations: false, allowFormFilling: false);
        byte[] newDenied = EncryptedSignatureDocument(
            includeField: false, allowModification: false,
            allowAnnotations: false, allowFormFilling: true);

        Assert.NotEmpty(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(existingAllowed, "user"), _ => [1],
            new PdfSignatureOptions { FieldName = "approval" }));
        InvalidOperationException existingError = Assert.Throws<InvalidOperationException>(() =>
            PdfDetachedSignatureWriter.Sign(
                PdfDocument.Open(existingDenied, "user"), _ => [1],
                new PdfSignatureOptions { FieldName = "approval" }));
        InvalidOperationException newError = Assert.Throws<InvalidOperationException>(() =>
            PdfDetachedSignatureWriter.Sign(
                PdfDocument.Open(newDenied, "user"), _ => [1]));
        Assert.NotEmpty(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(newDenied, "owner"), _ => [1]));

        Assert.Contains("existing form field", existingError.Message, StringComparison.Ordinal);
        Assert.Contains("creating a signature field", newError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_CanEmitCompressedCrossReferenceStreamRevision()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        var options = new PdfSignatureOptions
        {
            ReservedSignatureSize = 16,
            IncrementalWriteOptions = new PdfIncrementalUpdateWriteOptions
            {
                CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
                CompressCrossReferenceStream = true
            }
        };

        PdfDocument reopened = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [0x30, 0x00], options));
        Assert.Single(PdfSignatureReader.Read(reopened));
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfDictionary field = ResolveDictionary(reopened,
            Assert.IsType<PdfArray>(form[Name("Fields")])[0]);
        PdfIndirectReference signatureReference = Assert.IsType<PdfIndirectReference>(field[Name("V")]);

        Assert.True(reopened.CrossReferences.Sections[0].IsStream);
        Assert.Equal(PdfCrossReferenceEntryType.InUse,
            reopened.CrossReferences[signatureReference.ObjectNumber].Type);
    }

    [Fact]
    public void Sign_PacksEligibleRevisionObjectsButKeepsSignatureDictionaryDirect()
    {
        PdfDocument document = PdfDocument.Open(
            new PdfDocumentBuilder().AddBlankPage().Build());

        PdfDocument reopened = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            document, _ => [1], new PdfSignatureOptions
            {
                ReservedSignatureSize = 8,
                IncrementalWriteOptions = new PdfIncrementalUpdateWriteOptions
                {
                    CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
                    UseObjectStreams = true,
                    CompressObjectStreams = true,
                    CompressCrossReferenceStream = true
                }
            }));
        PdfDictionary catalog = ResolveDictionary(reopened, reopened.Trailer[Name("Root")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfIndirectReference fieldReference = Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(form[Name("Fields")])[0]);
        PdfDictionary field = ResolveDictionary(reopened, fieldReference);
        PdfIndirectReference signatureReference = Assert.IsType<PdfIndirectReference>(field[Name("V")]);

        Assert.Equal(PdfCrossReferenceEntryType.InUse,
            reopened.CrossReferences[signatureReference.ObjectNumber].Type);
        Assert.Contains(reopened.CrossReferences.Sections[0].Values,
            entry => entry.Type == PdfCrossReferenceEntryType.Compressed);
    }

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
    [InlineData(false, "approval")]
    [InlineData(true, "group.approval")]
    public void Sign_FillsDirectSignatureFieldsAtRootOrInsideIndirectParent(
        bool nested, string fieldName)
    {
        PdfDocument signed = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(BuildDirectSignatureFieldDocument(nested)), _ => [1],
            new PdfSignatureOptions
            {
                FieldName = fieldName,
                PageIndex = 999,
                ReservedSignatureSize = 8
            }));
        PdfDictionary catalog = ResolveDictionary(signed, signed.Trailer[Name("Root")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfObject rootFieldValue = Assert.IsType<PdfArray>(form[Name("Fields")])[0];
        PdfDictionary rootField = rootFieldValue is PdfIndirectReference rootReference
            ? ResolveDictionary(signed, rootReference)
            : Assert.IsType<PdfDictionary>(rootFieldValue);
        PdfDictionary field = nested
            ? Assert.IsType<PdfDictionary>(Assert.Single(
                Assert.IsType<PdfArray>(rootField[Name("Kids")])))
            : rootField;

        Assert.IsType<PdfIndirectReference>(field[Name("V")]);
        Assert.Equal(3, Assert.IsType<PdfInteger>(form[Name("SigFlags")]).Value);
        Assert.True(Assert.Single(PdfSignatureReader.Read(signed)).IsSigned);
    }

    [Fact]
    public void Sign_FillsPreAuthoredFieldWithoutChangingTaggedPdfStructure()
    {
        var content = new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Figure, 0)
            .Rectangle(10, 10, 20, 20).Fill()
            .EndMarkedContent();
        byte[] source = new PdfDocumentBuilder()
            .AddPage(200, 300, content)
            .AddSignatureField(0, "approval", 20, 40, 160, 40)
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
                alternateDescription: "Square")
            .Build();
        PdfDocument original = PdfDocument.Open(source);
        PdfDictionary originalCatalog = ResolveDictionary(
            original, original.Trailer[Name("Root")]);
        PdfIndirectReference structureReference = Assert.IsType<PdfIndirectReference>(
            originalCatalog[Name("StructTreeRoot")]);

        PdfDocument signed = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            original, _ => [1], new PdfSignatureOptions
            {
                FieldName = "approval",
                PageIndex = 999,
                ReservedSignatureSize = 8
            }));
        PdfDictionary catalog = ResolveDictionary(signed, signed.Trailer[Name("Root")]);

        Assert.Equal(structureReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(catalog[Name("StructTreeRoot")]).ObjectNumber);
        Assert.True(Assert.Single(PdfSignatureReader.Read(signed)).IsSigned);
    }

    [Fact]
    public void Sign_CreatesAccessibleSignatureFieldInTaggedPdf()
    {
        var content = new PdfContentStreamBuilder()
            .BeginMarkedContent(PdfStructureType.Figure, 0)
            .Rectangle(10, 10, 20, 20).Fill()
            .EndMarkedContent();
        byte[] source = new PdfDocumentBuilder()
            .AddPage(200, 300, content)
            .AddStructureContainer(PdfStructureType.Document)
            .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
                alternateDescription: "Square")
            .Build();

        PdfDocument signed = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                FieldName = "approval",
                ReservedSignatureSize = 8
            }));
        PdfDictionary catalog = ResolveDictionary(signed, signed.Trailer[Name("Root")]);
        PdfDictionary form = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfDictionary field = ResolveDictionary(signed,
            Assert.IsType<PdfArray>(form[Name("Fields")])[0]);
        long structureParentKey = Assert.IsType<PdfInteger>(
            field[Name("StructParent")]).Value;
        PdfDictionary structureRoot = ResolveDictionary(
            signed, catalog[Name("StructTreeRoot")]);
        PdfArray structureKids = Assert.IsType<PdfArray>(structureRoot[Name("K")]);
        PdfIndirectReference structureElementReference = Assert.IsType<PdfIndirectReference>(
            structureKids[^1]);
        PdfDictionary structureElement = ResolveDictionary(signed, structureElementReference);
        PdfDictionary objectReference = Assert.IsType<PdfDictionary>(
            structureElement[Name("K")]);
        PdfDictionary parentTree = ResolveDictionary(
            signed, structureRoot[Name("ParentTree")]);
        PdfArray numbers = Assert.IsType<PdfArray>(parentTree[Name("Nums")]);
        int keyIndex = Enumerable.Range(0, numbers.Count / 2)
            .Select(index => index * 2)
            .Single(index => Assert.IsType<PdfInteger>(numbers[index]).Value
                == structureParentKey);

        Assert.Equal("Form", Assert.IsType<PdfName>(
            structureElement[Name("S")]).ValueAsLatin1());
        Assert.Equal("OBJR", Assert.IsType<PdfName>(
            objectReference[Name("Type")]).ValueAsLatin1());
        Assert.Equal(Assert.IsType<PdfIndirectReference>(
            Assert.IsType<PdfArray>(form[Name("Fields")])[0]).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(objectReference[Name("Obj")]).ObjectNumber);
        Assert.Equal(structureElementReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(numbers[keyIndex + 1]).ObjectNumber);
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
    public void Sign_EnforcesRequiredSeedValueConstraints()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval", 20, 20, 160, 40,
                seedValue: new PdfSignatureSeedValue
                {
                    Handler = PdfSignatureHandler.AdobePpkLite,
                    RequireHandler = true,
                    ParserVersion = PdfSignatureSeedParserVersion.Pdf20,
                    RequireParserVersion = true,
                    SubFilters = [PdfSignatureSubFilter.EtsiCadesDetached],
                    RequireSubFilter = true,
                    DigestMethods = [PdfSignatureDigestMethod.Sha384],
                    RequireDigestMethod = true,
                    AddRevocationInformation = true,
                    RequireRevocationInformation = true,
                    Reasons = ["Approved"],
                    RequireReason = true,
                    LegalAttestations = ["Reviewed"],
                    RequireLegalAttestation = true,
                    CertificationPermission =
                        PdfSignatureCertificationPermission.FormFillingAndSignatures,
                    DocumentLockIntent = PdfSignatureDocumentLockIntent.Lock,
                    RequireDocumentLockIntent = true,
                    AppearanceName = "Approval appearance",
                    RequireAppearance = true
                })
            .Build();
        var valid = new PdfSignatureOptions
        {
            FieldName = "approval",
            DigestMethod = PdfSignatureDigestMethod.Sha384,
            IncludesRevocationInformation = true,
            Reason = "Approved",
            LegalAttestation = "Reviewed",
            CertificationPermission =
                PdfSignatureCertificationPermission.FormFillingAndSignatures,
            DocumentLockIntent = PdfSignatureDocumentLockIntent.Lock,
            AppearanceName = "Approval appearance",
            ReservedSignatureSize = 8
        };

        PdfDocument signed = PdfDocument.Open(PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], valid));
        Assert.True(ResolveDictionary(signed, signed.Trailer[Name("Root")])
            .ContainsKey(Name("Perms")));

        AssertRejected(valid with { DigestMethod = PdfSignatureDigestMethod.Sha256 });
        AssertRejected(valid with { IncludesRevocationInformation = false });
        AssertRejected(valid with { Reason = "Rejected" });
        AssertRejected(valid with { LegalAttestation = "Not reviewed" });
        AssertRejected(valid with { CertificationPermission = null });
        AssertRejected(valid with { DocumentLockIntent = PdfSignatureDocumentLockIntent.DoNotLock });
        AssertRejected(valid with { AppearanceName = "Other appearance" });

        void AssertRejected(PdfSignatureOptions options) =>
            Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
                PdfDocument.Open(source), _ => [1], options));
    }

    [Fact]
    public void Sign_RejectsRequiredSeedConstraintsWithoutVerifiableSignerInput()
    {
        byte[] timestampSource = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval", 20, 20, 160, 40,
                seedValue: new PdfSignatureSeedValue
                {
                    Timestamp = new PdfSignatureTimestamp(
                        "https://timestamp.example.test", Required: true)
                })
            .Build();
        byte[] certificateSource = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval", 20, 20, 160, 40,
                seedValue: new PdfSignatureSeedValue
                {
                    Certificate = new PdfSignatureCertificateSeed
                    {
                        KeyUsages = [new PdfCertificateKeyUsage { DigitalSignature = true }],
                        RequireKeyUsage = true
                    }
                })
            .Build();
        var options = new PdfSignatureOptions
        {
            FieldName = "approval",
            TimestampServerUrl = "https://timestamp.example.test",
            ReservedSignatureSize = 8
        };

        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(timestampSource), _ => [1], options));
        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(certificateSource), _ => [1], options));
    }

    [Fact]
    public void Sign_AcceptsRequiredTimestampOnlyWhenCmsContainsTimestampToken()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval", 20, 20, 160, 40,
                seedValue: new PdfSignatureSeedValue
                {
                    Timestamp = new PdfSignatureTimestamp(
                        "https://timestamp.example.test", Required: true)
                })
            .Build();
        var options = new PdfSignatureOptions
        {
            FieldName = "approval",
            TimestampServerUrl = "https://timestamp.example.test",
            ReservedSignatureSize = 256
        };

        byte[] signed = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => BuildTimestampedCms(), options);

        Assert.True(Assert.Single(PdfSignatureReader.Read(
            PdfDocument.Open(signed))).HasValidByteRange);
    }

    [Fact]
    public void Sign_ValidatesRequiredSignerCertificateConstraints()
    {
        using RSA rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=KillerPDF Test Root", rootKey, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        using X509Certificate2 root = rootRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        using RSA signerKey = RSA.Create(2048);
        var signerRequest = new CertificateRequest(
            "CN=Seed Signer,O=Killer Tools", signerKey, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        signerRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature, true));
        var policyWriter = new AsnWriter(AsnEncodingRules.DER);
        policyWriter.PushSequence();
        policyWriter.PushSequence();
        policyWriter.WriteObjectIdentifier("1.2.3.4");
        policyWriter.PopSequence();
        policyWriter.PopSequence();
        signerRequest.CertificateExtensions.Add(new X509Extension(
            "2.5.29.32", policyWriter.Encode(), false));
        using X509Certificate2 signer = signerRequest.Create(
            root, DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddDays(1), [1, 2, 3, 4]);
        byte[] signerDer = signer.RawData;
        byte[] rootDer = root.RawData;
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval", 20, 20, 160, 40,
                seedValue: new PdfSignatureSeedValue
                {
                    Certificate = new PdfSignatureCertificateSeed
                    {
                        SubjectCertificates = [signerDer],
                        RequireSubject = true,
                        IssuerCertificates = [rootDer],
                        RequireIssuer = true,
                        CertificatePolicyObjectIdentifiers = ["1.2.3.4"],
                        RequireCertificatePolicy = true,
                        SubjectDistinguishedNames =
                        [
                            new PdfCertificateDistinguishedName(
                                new Dictionary<string, string> { ["o"] = "Killer Tools" })
                        ],
                        RequireSubjectDistinguishedName = true,
                        KeyUsages =
                        [
                            new PdfCertificateKeyUsage
                            {
                                DigitalSignature = true,
                                KeyEncipherment = false
                            }
                        ],
                        RequireKeyUsage = true,
                        EnrollmentUrl = "https://signing.example.test/enroll",
                        EnrollmentUrlType = PdfCertificateEnrollmentUrlType.SignatureService,
                        RequireEnrollmentUrl = true
                    }
                })
            .Build();
        var valid = new PdfSignatureOptions
        {
            FieldName = "approval",
            SignerCertificate = signerDer,
            CertificateChain = [rootDer],
            CertificateAcquisitionUrl = "https://signing.example.test/enroll",
            ReservedSignatureSize = 2_048
        };

        byte[] signed = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => BuildCmsWithCertificate(signerDer), valid);
        Assert.True(Assert.Single(PdfSignatureReader.Read(
            PdfDocument.Open(signed))).HasValidByteRange);
        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], valid with { CertificateChain = [] }));
        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], valid with
            {
                CertificateAcquisitionUrl = "https://wrong.example.test"
            }));
        Assert.Throws<InvalidOperationException>(() => PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => BuildCmsWithCertificate(rootDer), valid));
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
        Assert.NotEmpty(PdfDetachedSignatureWriter.Sign(
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

    private static byte[] EncryptedSignatureDocument(
        bool includeField, bool allowModification,
        bool allowAnnotations, bool allowFormFilling)
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        if (includeField)
            builder.AddSignatureField(0, "approval", 20, 20, 160, 40);
        return builder.SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "user",
                OwnerPassword = "owner",
                AllowDocumentModification = allowModification,
                AllowAnnotationModification = allowAnnotations,
                AllowFormFilling = allowFormFilling
            })
            .Build();
    }

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

    private static byte[] BuildDirectSignatureFieldDocument(bool nested)
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int objectCount = nested ? 5 : 4;
        var offsets = new int[objectCount + 1];
        string fields = nested
            ? "[5 0 R]"
            : "[<< /FT /Sig /T (approval) /V null >>]";
        Add(1, $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields {fields} /SigFlags 1 >> >>");
        Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Resources <<>> >>");
        Add(4, "<< /Length 0 >>\nstream\n\nendstream");
        if (nested)
            Add(5, "<< /T (group) /Kids [<< /FT /Sig /T (approval) /V null >>] >>");
        int xrefOffset = source.Length;
        source.Append("xref\n0 ").Append(objectCount + 1)
            .Append("\n0000000000 65535 f \n");
        for (int index = 1; index <= objectCount; index++)
            source.Append(offsets[index].ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        source.Append("trailer\n<< /Size ").Append(objectCount + 1)
            .Append(" /Root 1 0 R >>\nstartxref\n")
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

    private static byte[] BuildTimestampedCms()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
        writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.PushSetOf();
        writer.PopSetOf();
        writer.PushSequence();
        writer.WriteObjectIdentifier("1.2.840.113549.1.7.1");
        writer.PopSequence();
        writer.PushSetOf();
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.PushSequence();
        writer.PopSequence();
        writer.PushSequence();
        writer.PopSequence();
        writer.PushSequence();
        writer.PopSequence();
        writer.WriteOctetString([1]);
        var unsignedTag = new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true);
        writer.PushSetOf(unsignedTag);
        writer.PushSequence();
        writer.WriteObjectIdentifier("1.2.840.113549.1.9.16.2.14");
        writer.PushSetOf();
        writer.WriteNull();
        writer.PopSetOf();
        writer.PopSequence();
        writer.PopSetOf(unsignedTag);
        writer.PopSequence();
        writer.PopSetOf();
        writer.PopSequence();
        writer.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
        writer.PopSequence();
        return writer.Encode();
    }

    private static byte[] BuildCmsWithCertificate(ReadOnlySpan<byte> certificate)
    {
        var certificateReader = new AsnReader(certificate.ToArray(), AsnEncodingRules.DER);
        AsnReader certificateSequence = certificateReader.ReadSequence();
        AsnReader tbs = certificateSequence.ReadSequence();
        if (tbs.PeekTag().HasSameClassAndValue(
            new Asn1Tag(TagClass.ContextSpecific, 0)))
            tbs.ReadEncodedValue();
        ReadOnlyMemory<byte> serial = tbs.ReadIntegerBytes();
        tbs.ReadSequence();
        ReadOnlyMemory<byte> issuer = tbs.ReadEncodedValue();
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
        writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.PushSetOf();
        writer.PopSetOf();
        writer.PushSequence();
        writer.WriteObjectIdentifier("1.2.840.113549.1.7.1");
        writer.PopSequence();
        var certificatesTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
        writer.PushSetOf(certificatesTag);
        writer.WriteEncodedValue(certificate);
        writer.PopSetOf(certificatesTag);
        writer.PushSetOf();
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.PushSequence();
        writer.WriteEncodedValue(issuer.Span);
        writer.WriteInteger(serial.Span);
        writer.PopSequence();
        writer.PushSequence();
        writer.PopSequence();
        writer.PushSequence();
        writer.PopSequence();
        writer.WriteOctetString([1]);
        writer.PopSequence();
        writer.PopSetOf();
        writer.PopSequence();
        writer.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
        writer.PopSequence();
        return writer.Encode();
    }
}
