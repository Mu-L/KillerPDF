namespace KillerPdf.Engine.Authoring;

/// <summary>A CIE L*a*b* color space with explicit white point and component ranges.</summary>
public sealed class PdfLabColorSpace
{
    public PdfLabColorSpace(
        double whiteX = 0.9642, double whiteY = 1, double whiteZ = 0.8249,
        double blackX = 0, double blackY = 0, double blackZ = 0,
        double minimumA = -128, double maximumA = 127,
        double minimumB = -128, double maximumB = 127)
    {
        WhiteX = Positive(whiteX, nameof(whiteX));
        if (whiteY != 1)
            throw new ArgumentOutOfRangeException(nameof(whiteY),
                "A PDF Lab white point must normalize Y to 1.0.");
        WhiteY = whiteY;
        WhiteZ = Positive(whiteZ, nameof(whiteZ));
        BlackX = Nonnegative(blackX, nameof(blackX));
        BlackY = Nonnegative(blackY, nameof(blackY));
        BlackZ = Nonnegative(blackZ, nameof(blackZ));
        if (!double.IsFinite(minimumA) || !double.IsFinite(maximumA) || minimumA >= maximumA)
            throw new ArgumentException("The Lab a* range must be finite and increasing.");
        if (!double.IsFinite(minimumB) || !double.IsFinite(maximumB) || minimumB >= maximumB)
            throw new ArgumentException("The Lab b* range must be finite and increasing.");
        MinimumA = minimumA;
        MaximumA = maximumA;
        MinimumB = minimumB;
        MaximumB = maximumB;
    }

    public double WhiteX { get; }
    public double WhiteY { get; }
    public double WhiteZ { get; }
    public double BlackX { get; }
    public double BlackY { get; }
    public double BlackZ { get; }
    public double MinimumA { get; }
    public double MaximumA { get; }
    public double MinimumB { get; }
    public double MaximumB { get; }

    private static double Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
        return value;
    }
    private static double Nonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
        return value;
    }
}
