namespace KillerPdf.Engine.Authoring;

/// <summary>Standard separable and non-separable PDF blend modes.</summary>
public enum PdfBlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity
}

internal static class PdfBlendModeNames
{
    internal static string Name(PdfBlendMode mode) => mode switch
    {
        PdfBlendMode.Normal => "Normal",
        PdfBlendMode.Multiply => "Multiply",
        PdfBlendMode.Screen => "Screen",
        PdfBlendMode.Overlay => "Overlay",
        PdfBlendMode.Darken => "Darken",
        PdfBlendMode.Lighten => "Lighten",
        PdfBlendMode.ColorDodge => "ColorDodge",
        PdfBlendMode.ColorBurn => "ColorBurn",
        PdfBlendMode.HardLight => "HardLight",
        PdfBlendMode.SoftLight => "SoftLight",
        PdfBlendMode.Difference => "Difference",
        PdfBlendMode.Exclusion => "Exclusion",
        PdfBlendMode.Hue => "Hue",
        PdfBlendMode.Saturation => "Saturation",
        PdfBlendMode.Color => "Color",
        PdfBlendMode.Luminosity => "Luminosity",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
