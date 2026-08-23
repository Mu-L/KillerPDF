namespace KillerPdf.Engine.Authoring;

public enum PdfFreeTextIntent
{
    FreeText,
    Callout,
    TypeWriter
}

public enum PdfLineAnnotationIntent
{
    Arrow,
    Dimension
}

public enum PdfVertexAnnotationIntent
{
    Cloud,
    Dimension
}

internal static class PdfAnnotationIntentNames
{
    internal static string Name(PdfFreeTextIntent value) => value switch
    {
        PdfFreeTextIntent.FreeText => "FreeText",
        PdfFreeTextIntent.Callout => "FreeTextCallout",
        PdfFreeTextIntent.TypeWriter => "FreeTextTypeWriter",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    internal static string Name(PdfLineAnnotationIntent value) => value switch
    {
        PdfLineAnnotationIntent.Arrow => "LineArrow",
        PdfLineAnnotationIntent.Dimension => "LineDimension",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    internal static string Name(PdfVertexAnnotationIntent value, bool closed) => value switch
    {
        PdfVertexAnnotationIntent.Cloud when closed => "PolygonCloud",
        PdfVertexAnnotationIntent.Dimension when closed => "PolygonDimension",
        PdfVertexAnnotationIntent.Dimension => "PolyLineDimension",
        PdfVertexAnnotationIntent.Cloud => throw new ArgumentException(
            "Cloud intent is only valid for polygon annotations.", nameof(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
