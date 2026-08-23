namespace KillerPdf.Engine.Authoring;

[Flags]
public enum PdfBookmarkStyle
{
    Regular = 0,
    Italic = 1,
    Bold = 2
}

public sealed record PdfBookmarkOptions
{
    private PdfBookmarkStyle _style;

    public PdfBookmarkStyle Style
    {
        get => _style;
        init
        {
            if ((value & ~(PdfBookmarkStyle.Italic | PdfBookmarkStyle.Bold)) != 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            _style = value;
        }
    }

    public PdfRgbColor? Color { get; init; }
    public bool IsOpen { get; init; } = true;
    public PdfDestination Destination { get; init; } = PdfDestination.FitPage();
}
