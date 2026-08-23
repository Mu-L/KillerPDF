namespace KillerPdf.Engine.Security;

/// <summary>Configures PDF 2.0 AES-256 password protection for a newly authored document.</summary>
public sealed class PdfPasswordEncryptionOptions
{
    public required string UserPassword { get; init; }
    public required string OwnerPassword { get; init; }
    public bool EncryptMetadata { get; init; } = true;
    public bool AllowLowQualityPrinting { get; init; } = true;
    public bool AllowDocumentModification { get; init; } = true;
    public bool AllowContentCopying { get; init; } = true;
    public bool AllowAnnotationModification { get; init; } = true;
    public bool AllowFormFilling { get; init; } = true;
    public bool AllowAccessibilityExtraction { get; init; } = true;
    public bool AllowDocumentAssembly { get; init; } = true;
    public bool AllowHighQualityPrinting { get; init; } = true;
}
