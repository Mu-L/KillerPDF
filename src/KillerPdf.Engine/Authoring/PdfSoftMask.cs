namespace KillerPdf.Engine.Authoring;

/// <summary>A reusable transparency-group form used as an alpha or luminosity soft mask.</summary>
public sealed record PdfSoftMask
{
    public PdfSoftMask(
        PdfFormXObject group,
        PdfSoftMaskSubtype subtype = PdfSoftMaskSubtype.Alpha,
        PdfSoftMaskBackdrop? backdrop = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!Enum.IsDefined(subtype))
            throw new ArgumentOutOfRangeException(nameof(subtype));
        if (!group.IsolatedTransparencyGroup && !group.KnockoutTransparencyGroup)
            throw new ArgumentException(
                "A soft mask must be backed by a transparency-group Form XObject.", nameof(group));
        Group = group;
        Subtype = subtype;
        if (backdrop.HasValue && !backdrop.Value.IsValid)
            throw new ArgumentException("The soft-mask backdrop is not initialized.", nameof(backdrop));
        if (backdrop.HasValue && backdrop.Value.ColorSpace != group.TransparencyGroupColorSpace)
            throw new ArgumentException(
                "A soft-mask backdrop must use its transparency group's color space.",
                nameof(backdrop));
        Backdrop = backdrop;
    }

    public PdfFormXObject Group { get; }
    public PdfSoftMaskSubtype Subtype { get; }
    public PdfSoftMaskBackdrop? Backdrop { get; }
}

public enum PdfSoftMaskSubtype
{
    Alpha,
    Luminosity
}

public readonly record struct PdfSoftMaskBackdrop
{
    public PdfSoftMaskBackdrop(double gray)
    {
        Validate(gray, nameof(gray));
        ColorSpace = PdfTransparencyGroupColorSpace.Gray;
        Gray = gray;
    }

    public PdfSoftMaskBackdrop(PdfRgbColor color)
    {
        ColorSpace = PdfTransparencyGroupColorSpace.Rgb;
        Rgb = color;
    }

    public PdfSoftMaskBackdrop(PdfCmykColor color)
    {
        ColorSpace = PdfTransparencyGroupColorSpace.Cmyk;
        Cmyk = color;
    }

    public PdfTransparencyGroupColorSpace ColorSpace { get; }
    public double? Gray { get; }
    public PdfRgbColor? Rgb { get; }
    public PdfCmykColor? Cmyk { get; }

    internal IReadOnlyList<double> Components => ColorSpace switch
    {
        PdfTransparencyGroupColorSpace.Gray => [Gray!.Value],
        PdfTransparencyGroupColorSpace.Rgb =>
            [Rgb!.Value.Red, Rgb.Value.Green, Rgb.Value.Blue],
        PdfTransparencyGroupColorSpace.Cmyk =>
            [Cmyk!.Value.Cyan, Cmyk.Value.Magenta, Cmyk.Value.Yellow, Cmyk.Value.Black],
        _ => throw new InvalidOperationException()
    };
    internal bool IsValid => Gray.HasValue || Rgb.HasValue || Cmyk.HasValue;

    private static void Validate(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}
