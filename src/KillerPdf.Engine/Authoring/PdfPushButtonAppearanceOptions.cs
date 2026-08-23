namespace KillerPdf.Engine.Authoring;

/// <summary>Alternate captions shown while a push button is hovered or pressed.</summary>
public sealed record PdfPushButtonAppearanceOptions
{
    public PdfTextFieldAlignment Alignment { get; init; } = PdfTextFieldAlignment.Center;
    public PdfImage? Icon { get; init; }
    public PdfImage? RolloverIcon { get; init; }
    public PdfImage? DownIcon { get; init; }
    public PdfPushButtonCaptionPosition CaptionPosition { get; init; }
    public PdfPushButtonIconScaleMode IconScaleMode { get; init; } =
        PdfPushButtonIconScaleMode.Always;
    public bool ProportionalIconScaling { get; init; } = true;
    public double IconHorizontalAlignment { get; init; } = 0.5;
    public double IconVerticalAlignment { get; init; } = 0.5;
    public bool FitIconToBounds { get; init; }
    public string? RolloverLabel { get; init; }
    public string? DownLabel { get; init; }
}
