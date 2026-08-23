namespace KillerPdf.Engine.Authoring;

/// <summary>A color positioned from zero to one along a gradient axis.</summary>
public readonly record struct PdfGradientStop
{
    public PdfGradientStop(double offset, PdfRgbColor color)
        : this(offset, PdfGradientColorSpace.Rgb, [color.Red, color.Green, color.Blue])
    {
        RgbColor = color;
    }

    public PdfGradientStop(double offset, double gray)
        : this(offset, PdfGradientColorSpace.Gray, [Component(gray, nameof(gray))])
    {
        Gray = gray;
    }

    public PdfGradientStop(double offset, PdfCmykColor color)
        : this(offset, PdfGradientColorSpace.Cmyk,
            [color.Cyan, color.Magenta, color.Yellow, color.Black])
    {
        CmykColor = color;
    }

    private PdfGradientStop(
        double offset, PdfGradientColorSpace colorSpace, IReadOnlyList<double> components)
    {
        if (!double.IsFinite(offset) || offset is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(offset),
                "A gradient-stop offset must be between zero and one.");
        Offset = offset;
        ColorSpace = colorSpace;
        Components = components;
    }

    public double Offset { get; }
    /// <summary>The RGB value for an RGB stop.</summary>
    /// <exception cref="InvalidOperationException">The stop uses Gray or CMYK.</exception>
    public PdfRgbColor Color => RgbColor ?? throw new InvalidOperationException(
        "This gradient stop does not use the RGB color space.");
    public PdfRgbColor? RgbColor { get; }
    public double? Gray { get; }
    public PdfCmykColor? CmykColor { get; }
    public PdfGradientColorSpace ColorSpace { get; }
    internal IReadOnlyList<double> Components { get; }

    private static double Component(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }
}

public enum PdfGradientColorSpace { Gray, Rgb, Cmyk }
