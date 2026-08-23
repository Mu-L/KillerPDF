namespace KillerPdf.Engine.Signing;

/// <summary>Cryptographic verification outcome for one structurally inspected signature.</summary>
public sealed record PdfSignatureVerificationResult
{
    public bool IsStructurallyValid { get; init; }
    public bool IsCryptographicallyValid { get; init; }
    public bool CertificateTrustWasChecked { get; init; }
    public bool IsCertificateTrusted { get; init; }
    public string? Error { get; init; }
}
