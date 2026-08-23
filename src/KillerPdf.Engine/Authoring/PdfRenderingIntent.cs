namespace KillerPdf.Engine.Authoring;

public enum PdfRenderingIntent
{
    AbsoluteColorimetric,
    RelativeColorimetric,
    Saturation,
    Perceptual
}

internal static class PdfRenderingIntentNames
{
    internal static string Name(PdfRenderingIntent intent) => intent switch
    {
        PdfRenderingIntent.AbsoluteColorimetric => "AbsoluteColorimetric",
        PdfRenderingIntent.RelativeColorimetric => "RelativeColorimetric",
        PdfRenderingIntent.Saturation => "Saturation",
        PdfRenderingIntent.Perceptual => "Perceptual",
        _ => throw new ArgumentOutOfRangeException(nameof(intent))
    };
}
