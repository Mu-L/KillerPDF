namespace KillerPdf.Engine.Authoring;

public enum PdfTextNoteIcon
{
    Note,
    Comment,
    Key,
    Help,
    NewParagraph,
    Paragraph,
    Insert
}

internal static class PdfTextNoteIconNames
{
    public static string Name(PdfTextNoteIcon icon) => icon switch
    {
        PdfTextNoteIcon.Note => "Note",
        PdfTextNoteIcon.Comment => "Comment",
        PdfTextNoteIcon.Key => "Key",
        PdfTextNoteIcon.Help => "Help",
        PdfTextNoteIcon.NewParagraph => "NewParagraph",
        PdfTextNoteIcon.Paragraph => "Paragraph",
        PdfTextNoteIcon.Insert => "Insert",
        _ => throw new ArgumentOutOfRangeException(nameof(icon))
    };
}
