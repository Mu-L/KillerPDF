namespace KillerPdf.Engine.Signing;

using KillerPdf.Engine.Authoring;

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
    public PdfSignatureCertificationPermission? CertificationPermission { get; init; }
}
