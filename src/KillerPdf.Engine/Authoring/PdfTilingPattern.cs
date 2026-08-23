using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Authoring;

/// <summary>Reusable coloured artwork or an uncoloured stencil repeated over a filled area.</summary>
public sealed class PdfTilingPattern
{
    public PdfTilingPattern(
        double width, double height, PdfContentStreamBuilder content,
        double? horizontalStep = null, double? verticalStep = null,
        PdfTilingPatternType tilingType = PdfTilingPatternType.ConstantSpacing,
        PdfTilingPatternPaintType paintType = PdfTilingPatternPaintType.Colored,
        PdfPatternMatrix? matrix = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Width = Positive(width, nameof(width));
        Height = Positive(height, nameof(height));
        HorizontalStep = NonZero(horizontalStep ?? width, nameof(horizontalStep));
        VerticalStep = NonZero(verticalStep ?? height, nameof(verticalStep));
        if (!Enum.IsDefined(tilingType)) throw new ArgumentOutOfRangeException(nameof(tilingType));
        if (!Enum.IsDefined(paintType)) throw new ArgumentOutOfRangeException(nameof(paintType));
        if (paintType == PdfTilingPatternPaintType.Uncolored && content.HasColorOperators)
            throw new ArgumentException(
                "An uncolored tiling pattern cannot contain color-selection or shading operators.",
                nameof(content));
        if (content.MarkedContentIds.Count > 0)
            throw new ArgumentException(
                "Tagged marked content belongs to a page structure tree and cannot be captured inside a reusable pattern.",
                nameof(content));

        TilingType = tilingType;
        PaintType = paintType;
        Matrix = matrix ?? PdfPatternMatrix.Identity;
        Content = content.Build();
        Fonts = content.FontResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        EmbeddedFonts = content.EmbeddedFontResources.ToArray();
        Images = content.ImageResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        OptionalContentGroups = content.OptionalContentResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        GraphicsStates = content.GraphicsStateResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        Shadings = content.ShadingResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        Forms = content.FormResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        Patterns = content.PatternResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        IccColorSpaces = content.IccColorSpaceResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        SpotColors = content.SpotColorResources.ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    public double Width { get; }
    public double Height { get; }
    public double HorizontalStep { get; }
    public double VerticalStep { get; }
    public PdfTilingPatternType TilingType { get; }
    public PdfTilingPatternPaintType PaintType { get; }
    public PdfPatternMatrix Matrix { get; }

    internal byte[] Content { get; }
    internal IReadOnlyDictionary<PdfStandardFont, PdfName> Fonts { get; }
    internal IReadOnlyCollection<EmbeddedFontUsage> EmbeddedFonts { get; }
    internal IReadOnlyDictionary<PdfImage, PdfName> Images { get; }
    internal IReadOnlyDictionary<PdfOptionalContentGroup, PdfName> OptionalContentGroups { get; }
    internal IReadOnlyDictionary<PdfGraphicsState, PdfName> GraphicsStates { get; }
    internal IReadOnlyDictionary<PdfShading, PdfName> Shadings { get; }
    internal IReadOnlyDictionary<PdfFormXObject, PdfName> Forms { get; }
    internal IReadOnlyDictionary<PdfTilingPattern, PdfName> Patterns { get; }
    internal IReadOnlyDictionary<PdfIccProfile, PdfName> IccColorSpaces { get; }
    internal IReadOnlyDictionary<PdfSpotColor, PdfName> SpotColors { get; }

    private static double Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
        return value;
    }

    private static double NonZero(double value, string name)
    {
        if (!double.IsFinite(value) || value == 0) throw new ArgumentOutOfRangeException(name);
        return value;
    }
}

/// <summary>Controls the spacing-versus-distortion tradeoff used by a tiling pattern.</summary>
public enum PdfTilingPatternType
{
    ConstantSpacing = 1,
    NoDistortion = 2,
    FasterTiling = 3
}

public enum PdfTilingPatternPaintType
{
    Colored = 1,
    Uncolored = 2
}

/// <summary>Maps pattern space into the default user space.</summary>
public readonly record struct PdfPatternMatrix
{
    public PdfPatternMatrix(double a, double b, double c, double d, double e, double f)
    {
        if (!double.IsFinite(a)) throw new ArgumentOutOfRangeException(nameof(a));
        if (!double.IsFinite(b)) throw new ArgumentOutOfRangeException(nameof(b));
        if (!double.IsFinite(c)) throw new ArgumentOutOfRangeException(nameof(c));
        if (!double.IsFinite(d)) throw new ArgumentOutOfRangeException(nameof(d));
        if (!double.IsFinite(e)) throw new ArgumentOutOfRangeException(nameof(e));
        if (!double.IsFinite(f)) throw new ArgumentOutOfRangeException(nameof(f));
        if (a * d - b * c == 0)
            throw new ArgumentException("A pattern matrix must be invertible.");
        A = a; B = b; C = c; D = d; E = e; F = f;
    }

    public static PdfPatternMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);
    public double A { get; }
    public double B { get; }
    public double C { get; }
    public double D { get; }
    public double E { get; }
    public double F { get; }
}
