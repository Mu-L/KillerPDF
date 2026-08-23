namespace KillerPdf.Engine.Authoring;

public enum PdfLineEndingStyle
{
    None,
    Square,
    Circle,
    Diamond,
    OpenArrow,
    ClosedArrow,
    Butt,
    ReverseOpenArrow,
    ReverseClosedArrow,
    Slash
}

internal static class PdfLineEndingStyleNames
{
    public static string Name(PdfLineEndingStyle style) => style switch
    {
        PdfLineEndingStyle.None => "None",
        PdfLineEndingStyle.Square => "Square",
        PdfLineEndingStyle.Circle => "Circle",
        PdfLineEndingStyle.Diamond => "Diamond",
        PdfLineEndingStyle.OpenArrow => "OpenArrow",
        PdfLineEndingStyle.ClosedArrow => "ClosedArrow",
        PdfLineEndingStyle.Butt => "Butt",
        PdfLineEndingStyle.ReverseOpenArrow => "ROpenArrow",
        PdfLineEndingStyle.ReverseClosedArrow => "RClosedArrow",
        PdfLineEndingStyle.Slash => "Slash",
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };
}
