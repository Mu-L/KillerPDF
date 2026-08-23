namespace KillerPdf.Engine.Authoring;

/// <summary>A color positioned from zero to one along a gradient axis.</summary>
public readonly record struct PdfGradientStop
{
    public PdfGradientStop(double offset, PdfRgbColor color)
    {
        if (!double.IsFinite(offset) || offset is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(offset),
                "A gradient-stop offset must be between zero and one.");
        Offset = offset;
        Color = color;
    }

    public double Offset { get; }
    public PdfRgbColor Color { get; }
}
