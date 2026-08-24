namespace KillerPdf.Engine.Authoring;

/// <summary>Descriptive metadata written to both the PDF information dictionary and XMP.</summary>
public sealed record PdfDocumentMetadata
{
    private PdfTrappedStatus? _trapped;

    public string? Title { get; init; }
    public string? Author { get; init; }
    public string? Subject { get; init; }
    public string? Keywords { get; init; }
    public string? Creator { get; init; }
    public string? Producer { get; init; } = "KillerPDF Engine";
    public string? Language { get; init; }
    public DateTimeOffset? CreationDate { get; init; }
    public DateTimeOffset? ModificationDate { get; init; }
    public PdfTrappedStatus? Trapped
    {
        get => _trapped;
        init
        {
            if (value.HasValue && !Enum.IsDefined(value.Value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _trapped = value;
        }
    }
}

/// <summary>Whether a document has been modified to compensate for printing-registration errors.</summary>
public enum PdfTrappedStatus
{
    False,
    True,
    Unknown
}
