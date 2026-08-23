namespace KillerPdf.Engine.Authoring;

/// <summary>Signing constraints stored with an unsigned signature field.</summary>
public sealed record PdfSignatureSeedValue
{
    public PdfSignatureHandler? Handler { get; init; }
    public bool RequireHandler { get; init; }
    public PdfSignatureSeedParserVersion? ParserVersion { get; init; }
    public bool RequireParserVersion { get; init; }
    public IReadOnlyList<PdfSignatureSubFilter>? SubFilters { get; init; }
    public bool RequireSubFilter { get; init; }
    public IReadOnlyList<PdfSignatureDigestMethod>? DigestMethods { get; init; }
    public bool RequireDigestMethod { get; init; }
    public bool AddRevocationInformation { get; init; }
    public bool RequireRevocationInformation { get; init; }
    public IReadOnlyList<string>? Reasons { get; init; }
    public bool RequireReason { get; init; }
    public PdfSignatureCertificationPermission? CertificationPermission { get; init; }
}
