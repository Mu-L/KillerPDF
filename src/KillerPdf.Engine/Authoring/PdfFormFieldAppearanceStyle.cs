namespace KillerPdf.Engine.Authoring;

/// <summary>Colors and border geometry used by an authored form-field widget.</summary>
public sealed record PdfFormFieldAppearanceStyle
{
    public PdfRgbColor? BackgroundColor { get; init; } = new PdfRgbColor(1, 1, 1);
    public PdfRgbColor? BorderColor { get; init; } = new PdfRgbColor(0, 0, 0);
    public PdfRgbColor TextColor { get; init; } = new PdfRgbColor(0, 0, 0);
    public double BorderWidth { get; init; } = 1;
    public PdfFormFieldBorderStyle BorderStyle { get; init; }
    public IReadOnlyList<double>? DashPattern { get; init; }
}
