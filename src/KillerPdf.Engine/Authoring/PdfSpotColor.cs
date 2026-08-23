namespace KillerPdf.Engine.Authoring;

/// <summary>A named Separation ink with a process-CMYK full-tint alternate.</summary>
public sealed class PdfSpotColor
{
    public PdfSpotColor(string name, PdfCmykColor alternateColor)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A spot-color name cannot be empty.", nameof(name));
        Name = name;
        AlternateColor = alternateColor;
    }

    public string Name { get; }
    public PdfCmykColor AlternateColor { get; }
}
