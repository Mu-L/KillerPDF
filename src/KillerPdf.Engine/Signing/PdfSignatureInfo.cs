namespace KillerPdf.Engine.Signing;

using KillerPdf.Engine.Authoring;

/// <summary>Structural information about an AcroForm signature field.</summary>
public sealed record PdfSignatureInfo
{
    public required string FieldName { get; init; }
    public bool IsSigned { get; init; }
    public bool IsCertificationSignature { get; init; }
    public PdfSignatureCertificationPermission? CertificationPermission { get; init; }
    public PdfSignatureLockAction? FieldLockAction { get; init; }
    public PdfSignatureLockPermission? FieldLockPermission { get; init; }
    public IReadOnlyList<string>? LockedFields { get; init; }
    public string? Filter { get; init; }
    public string? SubFilter { get; init; }
    public IReadOnlyList<long>? ByteRange { get; init; }
    /// <summary>The complete PDF /Contents string, including hexadecimal placeholder padding.</summary>
    public ReadOnlyMemory<byte> Contents { get; init; }
    /// <summary>The single bounded ASN.1 CMS value with PDF placeholder padding removed.</summary>
    public ReadOnlyMemory<byte> Cms { get; init; }
    public bool HasValidCmsEncoding { get; init; }
    public bool HasValidByteRange { get; init; }
    public bool CoversWholeDocument { get; init; }
}
