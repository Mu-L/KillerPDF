namespace KillerPdf.Engine.Authoring;

public abstract class PdfCalibratedColorSpace
{
    private protected PdfCalibratedColorSpace(
        int componentCount,
        double whiteX, double whiteY, double whiteZ,
        double blackX, double blackY, double blackZ)
    {
        if (!double.IsFinite(whiteX) || whiteX <= 0) throw new ArgumentOutOfRangeException(nameof(whiteX));
        if (whiteY != 1) throw new ArgumentOutOfRangeException(nameof(whiteY),
            "A calibrated PDF white point must normalize Y to 1.0.");
        if (!double.IsFinite(whiteZ) || whiteZ <= 0) throw new ArgumentOutOfRangeException(nameof(whiteZ));
        if (!double.IsFinite(blackX) || blackX < 0) throw new ArgumentOutOfRangeException(nameof(blackX));
        if (!double.IsFinite(blackY) || blackY < 0) throw new ArgumentOutOfRangeException(nameof(blackY));
        if (!double.IsFinite(blackZ) || blackZ < 0) throw new ArgumentOutOfRangeException(nameof(blackZ));
        ComponentCount = componentCount;
        WhiteX = whiteX; WhiteY = whiteY; WhiteZ = whiteZ;
        BlackX = blackX; BlackY = blackY; BlackZ = blackZ;
    }

    internal int ComponentCount { get; }
    public double WhiteX { get; }
    public double WhiteY { get; }
    public double WhiteZ { get; }
    public double BlackX { get; }
    public double BlackY { get; }
    public double BlackZ { get; }
}

public sealed class PdfCalGrayColorSpace : PdfCalibratedColorSpace
{
    public PdfCalGrayColorSpace(
        double whiteX = 0.9642, double whiteY = 1, double whiteZ = 0.8249,
        double blackX = 0, double blackY = 0, double blackZ = 0,
        double gamma = 1)
        : base(1, whiteX, whiteY, whiteZ, blackX, blackY, blackZ)
    {
        if (!double.IsFinite(gamma) || gamma <= 0)
            throw new ArgumentOutOfRangeException(nameof(gamma));
        Gamma = gamma;
    }
    public double Gamma { get; }
}

public sealed class PdfCalRgbColorSpace : PdfCalibratedColorSpace
{
    private readonly double[] _gamma;
    private readonly double[] _matrix;

    public PdfCalRgbColorSpace(
        double whiteX = 0.9642, double whiteY = 1, double whiteZ = 0.8249,
        double blackX = 0, double blackY = 0, double blackZ = 0,
        IReadOnlyList<double>? gamma = null,
        IReadOnlyList<double>? matrix = null)
        : base(3, whiteX, whiteY, whiteZ, blackX, blackY, blackZ)
    {
        gamma ??= [1, 1, 1];
        matrix ??= [1, 0, 0, 0, 1, 0, 0, 0, 1];
        if (gamma.Count != 3 || gamma.Any(value => !double.IsFinite(value) || value <= 0))
            throw new ArgumentException("CalRGB gamma requires three positive finite values.", nameof(gamma));
        if (matrix.Count != 9 || matrix.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("A CalRGB matrix requires nine finite values.", nameof(matrix));
        double determinant =
            matrix[0] * (matrix[4] * matrix[8] - matrix[5] * matrix[7])
            - matrix[1] * (matrix[3] * matrix[8] - matrix[5] * matrix[6])
            + matrix[2] * (matrix[3] * matrix[7] - matrix[4] * matrix[6]);
        if (determinant == 0)
            throw new ArgumentException("A CalRGB matrix must be invertible.", nameof(matrix));
        _gamma = gamma.ToArray();
        _matrix = matrix.ToArray();
    }

    public IReadOnlyList<double> Gamma => _gamma;
    public IReadOnlyList<double> Matrix => _matrix;
}
