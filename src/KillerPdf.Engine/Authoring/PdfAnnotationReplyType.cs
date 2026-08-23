namespace KillerPdf.Engine.Authoring;

/// <summary>How a text-note annotation relates to the annotation it references.</summary>
public enum PdfAnnotationReplyType
{
    Reply,
    Group
}

internal static class PdfAnnotationReplyTypeNames
{
    internal static string Name(PdfAnnotationReplyType value) => value switch
    {
        PdfAnnotationReplyType.Reply => "R",
        PdfAnnotationReplyType.Group => "Group",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
