namespace KillerPdf.Engine.Authoring;

/// <summary>The page-space window used to display an annotation's text.</summary>
public sealed record PdfAnnotationPopup
{
    public PdfAnnotationPopup(double x, double y, double width, double height, bool open = false)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Open = open;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public bool Open { get; }
}
