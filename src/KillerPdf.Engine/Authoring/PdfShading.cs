namespace KillerPdf.Engine.Authoring;

/// <summary>Base type for reusable PDF axial and radial gradient shadings.</summary>
public abstract class PdfShading
{
    private readonly PdfGradientStop[] _stops;

    private protected PdfShading(
        IEnumerable<PdfGradientStop> stops, bool extendStart, bool extendEnd)
    {
        ArgumentNullException.ThrowIfNull(stops);
        _stops = stops.ToArray();
        if (_stops.Length < 2)
            throw new ArgumentException("A gradient requires at least two color stops.", nameof(stops));
        if (_stops[0].Offset != 0 || _stops[^1].Offset != 1)
            throw new ArgumentException(
                "A gradient must begin at offset zero and end at offset one.", nameof(stops));
        for (int index = 1; index < _stops.Length; index++)
            if (_stops[index].Offset <= _stops[index - 1].Offset)
                throw new ArgumentException(
                    "Gradient-stop offsets must be strictly increasing.", nameof(stops));
        ExtendStart = extendStart;
        ExtendEnd = extendEnd;
    }

    public IReadOnlyList<PdfGradientStop> Stops => _stops;
    public bool ExtendStart { get; }
    public bool ExtendEnd { get; }

    protected static double Coordinate(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, "Gradient coordinates must be finite.");
        return value;
    }
}

public sealed class PdfAxialGradient : PdfShading
{
    public PdfAxialGradient(
        double startX, double startY, double endX, double endY,
        IEnumerable<PdfGradientStop> stops,
        bool extendStart = false, bool extendEnd = false)
        : base(stops, extendStart, extendEnd)
    {
        StartX = Coordinate(startX, nameof(startX));
        StartY = Coordinate(startY, nameof(startY));
        EndX = Coordinate(endX, nameof(endX));
        EndY = Coordinate(endY, nameof(endY));
        if (StartX == EndX && StartY == EndY)
            throw new ArgumentException("An axial gradient requires two different points.");
    }

    public double StartX { get; }
    public double StartY { get; }
    public double EndX { get; }
    public double EndY { get; }
}

public sealed class PdfRadialGradient : PdfShading
{
    public PdfRadialGradient(
        double startX, double startY, double startRadius,
        double endX, double endY, double endRadius,
        IEnumerable<PdfGradientStop> stops,
        bool extendStart = false, bool extendEnd = false)
        : base(stops, extendStart, extendEnd)
    {
        StartX = Coordinate(startX, nameof(startX));
        StartY = Coordinate(startY, nameof(startY));
        EndX = Coordinate(endX, nameof(endX));
        EndY = Coordinate(endY, nameof(endY));
        StartRadius = Radius(startRadius, nameof(startRadius));
        EndRadius = Radius(endRadius, nameof(endRadius));
        if (StartX == EndX && StartY == EndY && StartRadius == EndRadius)
            throw new ArgumentException("A radial gradient requires different circles.");
    }

    public double StartX { get; }
    public double StartY { get; }
    public double StartRadius { get; }
    public double EndX { get; }
    public double EndY { get; }
    public double EndRadius { get; }

    private static double Radius(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name,
                "A radial-gradient radius must be finite and non-negative.");
        return value;
    }
}
