namespace KillerPdf.Engine.Authoring;

/// <summary>Reusable transparency, blending, overprint, and compositing settings.</summary>
public sealed record PdfGraphicsState
{
    public PdfGraphicsState(
        double fillOpacity = 1,
        double strokeOpacity = 1,
        PdfBlendMode blendMode = PdfBlendMode.Normal,
        bool fillOverprint = false,
        bool strokeOverprint = false,
        PdfOverprintMode overprintMode = PdfOverprintMode.Zero,
        bool alphaIsShape = false,
        bool textKnockout = true,
        PdfSoftMask? softMask = null)
    {
        ValidateOpacity(fillOpacity, nameof(fillOpacity));
        ValidateOpacity(strokeOpacity, nameof(strokeOpacity));
        if (!Enum.IsDefined(blendMode))
            throw new ArgumentOutOfRangeException(nameof(blendMode));
        if (!Enum.IsDefined(overprintMode))
            throw new ArgumentOutOfRangeException(nameof(overprintMode));
        FillOpacity = fillOpacity;
        StrokeOpacity = strokeOpacity;
        BlendMode = blendMode;
        FillOverprint = fillOverprint;
        StrokeOverprint = strokeOverprint;
        OverprintMode = overprintMode;
        AlphaIsShape = alphaIsShape;
        TextKnockout = textKnockout;
        SoftMask = softMask;
    }

    public double FillOpacity { get; }
    public double StrokeOpacity { get; }
    public PdfBlendMode BlendMode { get; }
    public bool FillOverprint { get; }
    public bool StrokeOverprint { get; }
    public PdfOverprintMode OverprintMode { get; }
    public bool AlphaIsShape { get; }
    public bool TextKnockout { get; }
    public PdfSoftMask? SoftMask { get; }

    private static void ValidateOpacity(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name,
                "Graphics-state opacity must be between zero and one.");
    }
}

public enum PdfOverprintMode
{
    Zero = 0,
    One = 1
}
