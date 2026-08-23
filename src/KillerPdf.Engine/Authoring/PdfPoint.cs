namespace KillerPdf.Engine.Authoring;

/// <summary>A finite point in PDF user-space coordinates.</summary>
public readonly record struct PdfPoint
{
    public PdfPoint(double x, double y)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}
