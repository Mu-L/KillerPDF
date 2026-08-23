namespace KillerPdf.Engine.Authoring;

public enum PdfLineEndingStyle
{
    None,
    OpenArrow,
    ClosedArrow
}

internal static class PdfLineEndingStyleNames
{
    public static string Name(PdfLineEndingStyle style) => style switch
    {
        PdfLineEndingStyle.None => "None",
        PdfLineEndingStyle.OpenArrow => "OpenArrow",
        PdfLineEndingStyle.ClosedArrow => "ClosedArrow",
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };
}
