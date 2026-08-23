namespace KillerPdf.Engine.Authoring;

/// <summary>Alternate captions shown while a push button is hovered or pressed.</summary>
public sealed record PdfPushButtonAppearanceOptions
{
    public PdfTextFieldAlignment Alignment { get; init; } = PdfTextFieldAlignment.Center;
    public string? RolloverLabel { get; init; }
    public string? DownLabel { get; init; }
}
