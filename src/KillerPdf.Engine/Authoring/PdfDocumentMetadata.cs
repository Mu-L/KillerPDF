namespace KillerPdf.Engine.Authoring;

/// <summary>Descriptive metadata written to both the PDF information dictionary and XMP.</summary>
public sealed record PdfDocumentMetadata
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public string? Subject { get; init; }
    public string? Keywords { get; init; }
    public string? Creator { get; init; }
    public string? Producer { get; init; } = "KillerPDF Engine";
    public string? Language { get; init; }
    public DateTimeOffset? CreationDate { get; init; }
    public DateTimeOffset? ModificationDate { get; init; }
}
