namespace KillerPdf.Engine.Authoring;

/// <summary>Constraints on the X.509 certificate used to sign a field.</summary>
public sealed record PdfSignatureCertificateSeed
{
    public IReadOnlyList<byte[]>? SubjectCertificates { get; init; }
    public bool RequireSubject { get; init; }
    public IReadOnlyList<byte[]>? IssuerCertificates { get; init; }
    public bool RequireIssuer { get; init; }
    public IReadOnlyList<string>? CertificatePolicyObjectIdentifiers { get; init; }
    public bool RequireCertificatePolicy { get; init; }
    public IReadOnlyList<PdfCertificateDistinguishedName>? SubjectDistinguishedNames { get; init; }
    public bool RequireSubjectDistinguishedName { get; init; }
    public IReadOnlyList<PdfCertificateKeyUsage>? KeyUsages { get; init; }
    public bool RequireKeyUsage { get; init; }
    public string? EnrollmentUrl { get; init; }
    public PdfCertificateEnrollmentUrlType? EnrollmentUrlType { get; init; }
    public bool RequireEnrollmentUrl { get; init; }
}
