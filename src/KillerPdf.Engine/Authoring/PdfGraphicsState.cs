namespace KillerPdf.Engine.Authoring;

/// <summary>Reusable page transparency and blending settings.</summary>
public sealed record PdfGraphicsState
{
    public PdfGraphicsState(
        double fillOpacity = 1,
        double strokeOpacity = 1,
        PdfBlendMode blendMode = PdfBlendMode.Normal)
    {
        ValidateOpacity(fillOpacity, nameof(fillOpacity));
        ValidateOpacity(strokeOpacity, nameof(strokeOpacity));
        if (!Enum.IsDefined(blendMode))
            throw new ArgumentOutOfRangeException(nameof(blendMode));
        FillOpacity = fillOpacity;
        StrokeOpacity = strokeOpacity;
        BlendMode = blendMode;
    }

    public double FillOpacity { get; }
    public double StrokeOpacity { get; }
    public PdfBlendMode BlendMode { get; }

    private static void ValidateOpacity(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name,
                "Graphics-state opacity must be between zero and one.");
    }
}
