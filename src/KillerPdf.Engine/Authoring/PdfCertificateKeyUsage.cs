namespace KillerPdf.Engine.Authoring;

/// <summary>Required, forbidden, or unrestricted X.509 key-usage bits for a signing certificate.</summary>
public sealed record PdfCertificateKeyUsage
{
    public bool? DigitalSignature { get; init; }
    public bool? NonRepudiation { get; init; }
    public bool? KeyEncipherment { get; init; }
    public bool? DataEncipherment { get; init; }
    public bool? KeyAgreement { get; init; }
    public bool? KeyCertificateSigning { get; init; }
    public bool? CertificateRevocationListSigning { get; init; }
    public bool? EncipherOnly { get; init; }
    public bool? DecipherOnly { get; init; }
}
