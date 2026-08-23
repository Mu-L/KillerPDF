namespace KillerPdf.Engine.Authoring;

public readonly record struct PdfRgbColor
{
    public PdfRgbColor(double red, double green, double blue)
    {
        Red = Component(red, nameof(red));
        Green = Component(green, nameof(green));
        Blue = Component(blue, nameof(blue));
    }

    public double Red { get; }
    public double Green { get; }
    public double Blue { get; }

    public static PdfRgbColor Yellow { get; } = new(1, 0.92, 0.1);
    public static PdfRgbColor NoteYellow { get; } = new(1, 0.78, 0.1);

    private static double Component(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, "RGB components must be between zero and one.");
        return value;
    }
}
