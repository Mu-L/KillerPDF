namespace KillerPdf.Engine.Authoring;

public readonly record struct PdfCmykColor
{
    public PdfCmykColor(double cyan, double magenta, double yellow, double black)
    {
        Cyan = Component(cyan, nameof(cyan));
        Magenta = Component(magenta, nameof(magenta));
        Yellow = Component(yellow, nameof(yellow));
        Black = Component(black, nameof(black));
    }

    public double Cyan { get; }
    public double Magenta { get; }
    public double Yellow { get; }
    public double Black { get; }

    private static double Component(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, "A CMYK component must be between zero and one.");
        return value;
    }
}
