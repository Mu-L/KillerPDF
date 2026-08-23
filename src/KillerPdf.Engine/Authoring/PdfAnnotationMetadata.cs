namespace KillerPdf.Engine.Authoring;

/// <summary>Optional authorship and lifecycle metadata for an authored annotation.</summary>
public sealed record PdfAnnotationMetadata
{
    private PdfAnnotationFlags _flags = PdfAnnotationFlags.Print;

    public string? Author { get; init; }
    public string? Subject { get; init; }
    public DateTimeOffset? CreationDate { get; init; }
    public DateTimeOffset? ModificationDate { get; init; }
    public PdfAnnotationFlags Flags
    {
        get => _flags;
        init
        {
            const PdfAnnotationFlags all =
                PdfAnnotationFlags.Invisible | PdfAnnotationFlags.Hidden
                | PdfAnnotationFlags.Print | PdfAnnotationFlags.NoZoom
                | PdfAnnotationFlags.NoRotate | PdfAnnotationFlags.NoView
                | PdfAnnotationFlags.ReadOnly | PdfAnnotationFlags.Locked
                | PdfAnnotationFlags.ToggleNoView | PdfAnnotationFlags.LockedContents;
            if ((value & ~all) != 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            _flags = value;
        }
    }
}

[Flags]
public enum PdfAnnotationFlags
{
    None = 0,
    Invisible = 1,
    Hidden = 2,
    Print = 4,
    NoZoom = 8,
    NoRotate = 16,
    NoView = 32,
    ReadOnly = 64,
    Locked = 128,
    ToggleNoView = 256,
    LockedContents = 512
}
