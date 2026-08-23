using System.Text;
using KillerPdf.Engine.Authoring;
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
        Assert.False(approval.IsSigned);
        Assert.False(approval.IsCertificationSignature);
        Assert.Null(approval.ByteRange);
    }

    [Fact]
    public void Read_DistinguishesValidEarlierRevisionCoverageFromWholeDocumentCoverage()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
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
        byte[] withLaterBytes = update.Build();
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
        Assert.Empty(analysis.FreedObjectNumbers);
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
}
