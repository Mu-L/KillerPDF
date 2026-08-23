namespace KillerPdf.Engine.Authoring;

/// <summary>A standard marked or review workflow state for a text-note annotation.</summary>
public enum PdfTextNoteState
{
    Marked,
    Unmarked,
    Accepted,
    Rejected,
    Cancelled,
    Completed,
    NoReviewState
}

internal static class PdfTextNoteStateNames
{
    internal static string State(PdfTextNoteState value) => value switch
    {
        PdfTextNoteState.Marked => "Marked",
        PdfTextNoteState.Unmarked => "Unmarked",
        PdfTextNoteState.Accepted => "Accepted",
        PdfTextNoteState.Rejected => "Rejected",
        PdfTextNoteState.Cancelled => "Cancelled",
        PdfTextNoteState.Completed => "Completed",
        PdfTextNoteState.NoReviewState => "None",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    internal static string Model(PdfTextNoteState value) => value switch
    {
        PdfTextNoteState.Marked or PdfTextNoteState.Unmarked => "Marked",
        PdfTextNoteState.Accepted or PdfTextNoteState.Rejected or PdfTextNoteState.Cancelled
            or PdfTextNoteState.Completed or PdfTextNoteState.NoReviewState => "Review",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
