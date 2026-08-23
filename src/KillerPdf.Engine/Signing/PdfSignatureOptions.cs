namespace KillerPdf.Engine.Signing;

using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Writing;

/// <summary>Descriptive values written into an approval-signature dictionary.</summary>
public sealed record PdfSignatureOptions
{
    public int PageIndex { get; init; }
    public string FieldName { get; init; } = "Signature1";
    public string? SignerName { get; init; }
    public string? Reason { get; init; }
    public string? Location { get; init; }
    public string? ContactInformation { get; init; }
    public DateTimeOffset? SigningTime { get; init; }
    public int ReservedSignatureSize { get; init; } = 32_768;
    /// <summary>The digest algorithm the detached CMS callback commits to using.</summary>
    public PdfSignatureDigestMethod DigestMethod { get; init; } =
        PdfSignatureDigestMethod.Sha256;
    /// <summary>Whether the detached CMS callback commits to embedding revocation information.</summary>
    public bool IncludesRevocationInformation { get; init; }
    public string? LegalAttestation { get; init; }
    public PdfSignatureDocumentLockIntent? DocumentLockIntent { get; init; }
    public string? AppearanceName { get; init; }
    /// <summary>DER-encoded end-entity certificate used by the detached CMS callback.</summary>
    public ReadOnlyMemory<byte> SignerCertificate { get; init; }
    /// <summary>DER-encoded issuer certificates supplied with the signer certificate.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>>? CertificateChain { get; init; }
    public string? CertificateAcquisitionUrl { get; init; }
    public string? TimestampServerUrl { get; init; }
    public PdfSignatureCertificationPermission? CertificationPermission { get; init; }
    /// <summary>
    /// Optional structural policy for the signature revision. The signature dictionary remains
    /// direct and patchable even when other eligible revision objects are packed.
    /// </summary>
    public PdfIncrementalUpdateWriteOptions? IncrementalWriteOptions { get; init; }
}
