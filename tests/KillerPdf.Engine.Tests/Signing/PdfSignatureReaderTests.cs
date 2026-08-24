using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Signing;
using KillerPdf.Engine.Writing;
using Xunit;

namespace KillerPdf.Engine.Tests.Signing;

public sealed class PdfSignatureReaderTests
{
    [Fact]
    public void Read_ReportsUnsignedAndCertificationSignaturesAndExtractsSignedContent()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddSignatureField(0, "approval", 20, 80, 160, 40)
            .AddSignatureField(0, "certification", 20, 20, 160, 40,
                fieldLock: new PdfSignatureFieldLock(
                    PdfSignatureLockAction.Include, ["approval"],
                    PdfSignatureLockPermission.FormFillingAndSignatures))
            .Build();
        byte[]? callbackContent = null;
        byte[] signedBytes = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), content =>
            {
                callbackContent = content.ToArray();
                return [0x30, 0x01, 0x00];
            }, new PdfSignatureOptions
            {
                FieldName = "certification",
                CertificationPermission =
                    PdfSignatureCertificationPermission.FormFillingAndSignatures,
                ReservedSignatureSize = 16
            });
        PdfDocument document = PdfDocument.Open(signedBytes);

        IReadOnlyList<PdfSignatureInfo> signatures = PdfSignatureReader.Read(document);
        PdfSignatureInfo certification = Assert.Single(signatures,
            item => item.FieldName == "certification");
        PdfSignatureInfo approval = Assert.Single(signatures,
            item => item.FieldName == "approval");

        Assert.True(certification.IsSigned);
        Assert.True(certification.IsCertificationSignature);
        Assert.Equal(PdfSignatureCertificationPermission.FormFillingAndSignatures,
            certification.CertificationPermission);
        Assert.Equal(PdfSignatureLockAction.Include, certification.FieldLockAction);
        Assert.Equal(PdfSignatureLockPermission.FormFillingAndSignatures,
            certification.FieldLockPermission);
        Assert.Equal(["approval"], certification.LockedFields);
        Assert.True(certification.HasValidByteRange);
        Assert.True(certification.CoversWholeDocument);
        Assert.Equal("Adobe.PPKLite", certification.Filter);
        Assert.Equal("ETSI.CAdES.detached", certification.SubFilter);
        Assert.Equal(16, certification.Contents.Length);
        Assert.True(certification.HasValidCmsEncoding);
        Assert.Equal([0x30, 0x01, 0x00], certification.Cms.ToArray());
        Assert.Equal(callbackContent, PdfSignatureReader.GetSignedContent(
            document, certification));
        Assert.Equal(PdfSignedRevisionPermissionAssessment.NoLaterChanges,
            PdfSignedRevisionAnalyzer.Analyze(document, certification).PermissionAssessment);
        Assert.False(approval.IsSigned);
        Assert.False(approval.IsCertificationSignature);
        Assert.Null(approval.ByteRange);
    }

    [Fact]
    public void Read_DistinguishesValidEarlierRevisionCoverageFromWholeDocumentCoverage()
    {
        byte[] initial = new PdfDocumentBuilder().AddBlankPage().Build();
        var seed = new PdfIncrementalUpdateBuilder(PdfDocument.Open(initial));
        PdfIndirectReference freedObject = seed.AddObject(new PdfInteger(99));
        byte[] source = seed.Build();
        byte[] signed = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                ReservedSignatureSize = 8
            });
        PdfDocument signedDocument = PdfDocument.Open(signed);
        var update = new PdfIncrementalUpdateBuilder(signedDocument);
        PdfIndirectReference addedObject = update.AddObject(new PdfInteger(1));
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            signedDocument.Trailer[new PdfName("Root"u8)]);
        update.ReplaceObject(catalogReference.ObjectNumber,
            signedDocument.Resolve(catalogReference));
        update.FreeObject(freedObject.ObjectNumber);
        byte[] withLaterBytes = update.Build(new PdfIncrementalUpdateWriteOptions
        {
            CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
            UseObjectStreams = true,
            CompressObjectStreams = true,
            CompressCrossReferenceStream = true
        });
        PdfDocument document = PdfDocument.Open(withLaterBytes);
        PdfSignatureInfo signature = Assert.Single(PdfSignatureReader.Read(document));
        PdfSignedRevisionAnalysis analysis =
            PdfSignedRevisionAnalyzer.Analyze(document, signature);

        Assert.True(signature.HasValidByteRange);
        Assert.False(signature.CoversWholeDocument);
        Assert.True(analysis.SignedRevisionIsValidPdf);
        Assert.True(analysis.HasLaterChanges);
        Assert.Equal(1, analysis.LaterRevisionCount);
        Assert.Contains(addedObject.ObjectNumber, analysis.ChangedObjectNumbers);
        Assert.Contains(addedObject.ObjectNumber, analysis.AddedObjectNumbers);
        Assert.Contains(catalogReference.ObjectNumber, analysis.UpdatedObjectNumbers);
        Assert.Contains(freedObject.ObjectNumber, analysis.FreedObjectNumbers);
        Assert.Equal(PdfCrossReferenceEntryType.Compressed,
            document.CrossReferences[addedObject.ObjectNumber].Type);
        Assert.Equal(PdfSignedRevisionPermissionAssessment.NotCertified,
            analysis.PermissionAssessment);
    }

    [Fact]
    public void Analyze_ReportsLaterChangesAsProhibitedByNoChangesCertification()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] signed = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [0x30, 0x00], new PdfSignatureOptions
            {
                CertificationPermission = PdfSignatureCertificationPermission.NoChanges,
                ReservedSignatureSize = 8
            });
        var update = new PdfIncrementalUpdateBuilder(PdfDocument.Open(signed));
        update.AddObject(new PdfInteger(1));
        PdfDocument changed = PdfDocument.Open(update.Build());
        PdfSignatureInfo signature = Assert.Single(PdfSignatureReader.Read(changed));

        PdfSignedRevisionAnalysis analysis =
            PdfSignedRevisionAnalyzer.Analyze(changed, signature);

        Assert.Equal(PdfSignedRevisionPermissionAssessment.Prohibited,
            analysis.PermissionAssessment);
    }

    [Fact]
    public void Analyze_ReportsMalformedFilteredSignedRevisionWithoutThrowing()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int malformedXrefOffset = source.Length;
        source.Append("1 0 obj\n")
            .Append("<< /Type /XRef /Size 1 /W [1 1 1] /Filter /Bogus /Length 1 >>\n")
            .Append("stream\nx\nendstream\nendobj\n")
            .Append($"startxref\n{malformedXrefOffset}\n%%EOF\n");
        int signedLength = Encoding.ASCII.GetByteCount(source.ToString());
        int catalogOffset = source.Length;
        source.Append("2 0 obj << /Type /Catalog >> endobj\n");
        int currentXrefOffset = source.Length;
        source.Append("xref\n0 3\n")
            .Append("0000000000 65535 f\n")
            .Append("0000000000 00000 f\n")
            .Append($"{catalogOffset:0000000000} 00000 n\n")
            .Append("trailer << /Size 3 /Root 2 0 R >>\n")
            .Append($"startxref\n{currentXrefOffset}\n%%EOF\n");
        PdfDocument document = PdfDocument.Open(
            Encoding.ASCII.GetBytes(source.ToString()));
        var signature = new PdfSignatureInfo
        {
            FieldName = "malformed-history",
            IsSigned = true,
            HasValidByteRange = true,
            ByteRange = [0, 0, 1, signedLength - 1]
        };

        PdfSignedRevisionAnalysis analysis =
            PdfSignedRevisionAnalyzer.Analyze(document, signature);

        Assert.False(analysis.SignedRevisionIsValidPdf);
        Assert.True(analysis.HasLaterChanges);
        Assert.Equal(1, analysis.LaterRevisionCount);
    }

    [Fact]
    public void Read_ReportsInvalidByteRangeWithoutReadingOutsideTheDocument()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] signed = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                ReservedSignatureSize = 8
            });
        byte[] marker = Encoding.ASCII.GetBytes("/ByteRange [0000000000");
        int markerOffset = signed.AsSpan().IndexOf(marker);
        Assert.True(markerOffset >= 0);
        signed[markerOffset + marker.Length - 1] = (byte)'1';

        PdfSignatureInfo signature = Assert.Single(
            PdfSignatureReader.Read(PdfDocument.Open(signed)));

        Assert.True(signature.IsSigned);
        Assert.False(signature.HasValidByteRange);
        Assert.False(signature.CoversWholeDocument);
        Assert.Throws<InvalidOperationException>(() =>
            PdfSignatureReader.GetSignedContent(PdfDocument.Open(signed), signature));
    }

    [Fact]
    public void Read_RejectsByteRangeGapThatIncludesBytesBeyondContents()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] signed = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                ReservedSignatureSize = 8
            });
        string text = Encoding.ASCII.GetString(signed);
        Match match = Regex.Match(text,
            @"/ByteRange \[(\d{10}) (\d{10}) (\d{10}) (\d{10})\]");
        Assert.True(match.Success);
        int secondStart = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        int secondLength = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        Encoding.ASCII.GetBytes($"{secondStart + 1:0000000000}")
            .CopyTo(signed.AsSpan(match.Groups[3].Index, 10));
        Encoding.ASCII.GetBytes($"{secondLength - 1:0000000000}")
            .CopyTo(signed.AsSpan(match.Groups[4].Index, 10));

        PdfSignatureInfo signature = Assert.Single(
            PdfSignatureReader.Read(PdfDocument.Open(signed)));

        Assert.False(signature.HasValidByteRange);
        Assert.False(signature.CoversWholeDocument);
    }

    [Fact]
    public void Read_RejectsStaleCertificationSignatureReference()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] signed = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                CertificationPermission = PdfSignatureCertificationPermission.NoChanges,
                ReservedSignatureSize = 8
            });
        PdfDocument document = PdfDocument.Open(signed);
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            document.Trailer[Name("Root")]);
        PdfDictionary catalog = Assert.IsType<PdfDictionary>(document.Resolve(catalogReference));
        PdfDictionary permissions = Assert.IsType<PdfDictionary>(catalog[Name("Perms")]);
        PdfIndirectReference certification = Assert.IsType<PdfIndirectReference>(
            permissions[Name("DocMDP")]);
        var stalePermissions = new PdfDictionary(permissions.Select(entry =>
            entry.Key.Equals(Name("DocMDP"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key,
                    new PdfIndirectReference(
                        certification.ObjectNumber, certification.Generation + 1))
                : entry));
        var staleCatalog = new PdfDictionary(catalog.Select(entry =>
            entry.Key.Equals(Name("Perms"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, stalePermissions)
                : entry));
        var update = new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(catalogReference.ObjectNumber, staleCatalog);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            PdfSignatureReader.Read(PdfDocument.Open(update.Build())));

        Assert.Contains("certification signature", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsInvalidCertificationTransformVersion()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        byte[] signed = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), _ => [1], new PdfSignatureOptions
            {
                CertificationPermission = PdfSignatureCertificationPermission.NoChanges,
                ReservedSignatureSize = 8
            });
        PdfDocument document = PdfDocument.Open(signed);
        PdfDictionary catalog = Assert.IsType<PdfDictionary>(
            document.Resolve(Assert.IsType<PdfIndirectReference>(document.Trailer[Name("Root")])));
        PdfDictionary permissions = Assert.IsType<PdfDictionary>(catalog[Name("Perms")]);
        PdfIndirectReference signatureReference = Assert.IsType<PdfIndirectReference>(
            permissions[Name("DocMDP")]);
        PdfDictionary signature = Assert.IsType<PdfDictionary>(document.Resolve(signatureReference));
        PdfArray references = Assert.IsType<PdfArray>(signature[Name("Reference")]);
        PdfDictionary reference = Assert.IsType<PdfDictionary>(Assert.Single(references));
        PdfDictionary parameters = Assert.IsType<PdfDictionary>(reference[Name("TransformParams")]);
        var malformedParameters = new PdfDictionary(parameters.Select(entry =>
            entry.Key.Equals(Name("V"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, Name("2.0"))
                : entry));
        var malformedReference = new PdfDictionary(reference.Select(entry =>
            entry.Key.Equals(Name("TransformParams"))
                ? new KeyValuePair<PdfName, PdfObject>(entry.Key, malformedParameters)
                : entry));
        var malformedSignature = new PdfDictionary(signature.Select(entry =>
            entry.Key.Equals(Name("Reference"))
                ? new KeyValuePair<PdfName, PdfObject>(
                    entry.Key, new PdfArray([malformedReference]))
                : entry));
        var update = new PdfIncrementalUpdateBuilder(document)
            .ReplaceObject(signatureReference.ObjectNumber, malformedSignature);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            PdfSignatureReader.Read(PdfDocument.Open(update.Build())));

        Assert.Contains("/1.2", error.Message, StringComparison.Ordinal);
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
