namespace KillerPdf.Engine.Authoring;

public enum PdfLinkBorderStyle { Solid, Dashed, Beveled, Inset, Underline }
public enum PdfLinkHighlightMode { None, Invert, Outline, Push }

public sealed class PdfLinkAppearance
{
    public PdfLinkAppearance(
        double borderWidth = 0,
        PdfLinkBorderStyle borderStyle = PdfLinkBorderStyle.Solid,
        IReadOnlyList<double>? dashPattern = null,
        PdfRgbColor? color = null,
        PdfLinkHighlightMode highlightMode = PdfLinkHighlightMode.Invert,
        double horizontalCornerRadius = 0,
        double verticalCornerRadius = 0)
    {
        if (!double.IsFinite(borderWidth) || borderWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(borderWidth));
        if (!Enum.IsDefined(borderStyle)) throw new ArgumentOutOfRangeException(nameof(borderStyle));
        if (!Enum.IsDefined(highlightMode)) throw new ArgumentOutOfRangeException(nameof(highlightMode));
        if (!double.IsFinite(horizontalCornerRadius) || horizontalCornerRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalCornerRadius));
        if (!double.IsFinite(verticalCornerRadius) || verticalCornerRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(verticalCornerRadius));
        dashPattern ??= [3];
        if (dashPattern.Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentOutOfRangeException(nameof(dashPattern));
        if (dashPattern.Count == 0 || dashPattern.All(value => value == 0))
            throw new ArgumentException("A link dash pattern requires a nonzero length.", nameof(dashPattern));
        if (borderStyle != PdfLinkBorderStyle.Dashed && dashPattern is not [3])
            throw new ArgumentException(
                "A dash pattern can only be used with a dashed link border.", nameof(dashPattern));

        BorderWidth = borderWidth;
        BorderStyle = borderStyle;
        DashPattern = dashPattern.ToArray();
        Color = color;
        HighlightMode = highlightMode;
        HorizontalCornerRadius = horizontalCornerRadius;
        VerticalCornerRadius = verticalCornerRadius;
    }

    public double BorderWidth { get; }
    public PdfLinkBorderStyle BorderStyle { get; }
    public IReadOnlyList<double> DashPattern { get; }
    public PdfRgbColor? Color { get; }
    public PdfLinkHighlightMode HighlightMode { get; }
    public double HorizontalCornerRadius { get; }
    public double VerticalCornerRadius { get; }
}
