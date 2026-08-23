namespace KillerPdf.Engine.Authoring;

/// <summary>A background color used where a bounded shading has no defined value.</summary>
public sealed class PdfGradientBackground
{
    public PdfGradientBackground(double gray)
        : this(PdfGradientColorSpace.Gray, [Component(gray, nameof(gray))])
    {
    }

    public PdfGradientBackground(PdfRgbColor color)
        : this(PdfGradientColorSpace.Rgb, [color.Red, color.Green, color.Blue])
    {
    }

    public PdfGradientBackground(PdfCmykColor color)
        : this(PdfGradientColorSpace.Cmyk,
            [color.Cyan, color.Magenta, color.Yellow, color.Black])
    {
    }

    private PdfGradientBackground(
        PdfGradientColorSpace colorSpace, IReadOnlyList<double> components)
    {
        ColorSpace = colorSpace;
        Components = components;
    }

    public PdfGradientColorSpace ColorSpace { get; }
    internal IReadOnlyList<double> Components { get; }

    private static double Component(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }
}
