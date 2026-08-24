namespace KillerPdf.Engine.Authoring;

/// <summary>Base type for reusable PDF axial and radial gradient shadings.</summary>
public abstract class PdfShading
{
    private readonly PdfGradientStop[] _stops;

    private protected PdfShading(
        IEnumerable<PdfGradientStop> stops, bool extendStart, bool extendEnd,
        PdfShadingBounds? bounds, bool antiAlias, PdfGradientBackground? background)
    {
        ArgumentNullException.ThrowIfNull(stops);
        _stops = stops.ToArray();
        if (_stops.Length < 2)
            throw new ArgumentException("A gradient requires at least two color stops.", nameof(stops));
        foreach (PdfGradientStop stop in _stops)
        {
            int expectedComponents = stop.ColorSpace switch
            {
                PdfGradientColorSpace.Gray => 1,
                PdfGradientColorSpace.Rgb => 3,
                PdfGradientColorSpace.Cmyk => 4,
                _ => 0
            };
            if (expectedComponents == 0 || stop.Components is null
                || stop.Components.Count != expectedComponents
                || stop.Components.Any(component =>
                    !double.IsFinite(component) || component is < 0 or > 1))
                throw new ArgumentException(
                    "A gradient contains an uninitialized color stop.", nameof(stops));
        }
        if (_stops[0].Offset != 0 || _stops[^1].Offset != 1)
            throw new ArgumentException(
                "A gradient must begin at offset zero and end at offset one.", nameof(stops));
        for (int index = 1; index < _stops.Length; index++)
        {
            if (_stops[index].Offset <= _stops[index - 1].Offset)
                throw new ArgumentException(
                    "Gradient-stop offsets must be strictly increasing.", nameof(stops));
            if (_stops[index].ColorSpace != _stops[0].ColorSpace)
                throw new ArgumentException(
                    "Every stop in a gradient must use the same color space.", nameof(stops));
        }
        ExtendStart = extendStart;
        ExtendEnd = extendEnd;
        if (bounds is PdfShadingBounds value
            && (!double.IsFinite(value.MinimumX)
                || !double.IsFinite(value.MinimumY)
                || !double.IsFinite(value.MaximumX)
                || !double.IsFinite(value.MaximumY)
                || value.MaximumX <= value.MinimumX
                || value.MaximumY <= value.MinimumY))
            throw new ArgumentOutOfRangeException(nameof(bounds),
                "Shading bounds must be finite and have positive width and height.");
        Bounds = bounds;
        AntiAlias = antiAlias;
        if (background is not null && background.ColorSpace != _stops[0].ColorSpace)
            throw new ArgumentException(
                "A shading background must use the same color space as its stops.",
                nameof(background));
        Background = background;
    }

    public IReadOnlyList<PdfGradientStop> Stops => _stops;
    public PdfGradientColorSpace ColorSpace => _stops[0].ColorSpace;
    public bool ExtendStart { get; }
    public bool ExtendEnd { get; }
    public PdfShadingBounds? Bounds { get; }
    public bool AntiAlias { get; }
    public PdfGradientBackground? Background { get; }

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
        bool extendStart = false, bool extendEnd = false,
        PdfShadingBounds? bounds = null, bool antiAlias = true,
        PdfGradientBackground? background = null)
        : base(stops, extendStart, extendEnd, bounds, antiAlias, background)
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
        bool extendStart = false, bool extendEnd = false,
        PdfShadingBounds? bounds = null, bool antiAlias = true,
        PdfGradientBackground? background = null)
        : base(stops, extendStart, extendEnd, bounds, antiAlias, background)
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

/// <summary>An optional rectangular boundary for evaluating a shading.</summary>
public readonly record struct PdfShadingBounds
{
    public PdfShadingBounds(double minimumX, double minimumY, double maximumX, double maximumY)
    {
        if (!double.IsFinite(minimumX)) throw new ArgumentOutOfRangeException(nameof(minimumX));
        if (!double.IsFinite(minimumY)) throw new ArgumentOutOfRangeException(nameof(minimumY));
        if (!double.IsFinite(maximumX) || maximumX <= minimumX)
            throw new ArgumentOutOfRangeException(nameof(maximumX));
        if (!double.IsFinite(maximumY) || maximumY <= minimumY)
            throw new ArgumentOutOfRangeException(nameof(maximumY));
        MinimumX = minimumX;
        MinimumY = minimumY;
        MaximumX = maximumX;
        MaximumY = maximumY;
    }

    public double MinimumX { get; }
    public double MinimumY { get; }
    public double MaximumX { get; }
    public double MaximumY { get; }
}
