namespace KillerPdf.Engine.Authoring;

public enum PdfPageLayout
{
    SinglePage,
    OneColumn,
    TwoColumnLeft,
    TwoColumnRight,
    TwoPageLeft,
    TwoPageRight
}

public enum PdfPageMode
{
    UseNone,
    UseOutlines,
    UseThumbs,
    FullScreen,
    UseOptionalContent,
    UseAttachments
}

public enum PdfReadingDirection { LeftToRight, RightToLeft }
public enum PdfPrintScaling { ApplicationDefault, None }
public enum PdfDuplexMode { Default, Simplex, DuplexFlipShortEdge, DuplexFlipLongEdge }

public sealed record PdfViewerPreferences
{
    public bool HideToolbar { get; init; }
    public bool HideMenuBar { get; init; }
    public bool HideWindowUi { get; init; }
    public bool FitWindow { get; init; }
    public bool CenterWindow { get; init; }
    public bool DisplayDocumentTitle { get; init; }
    public bool PickTrayByPdfSize { get; init; }
    public PdfReadingDirection ReadingDirection { get; init; }
    public PdfPrintScaling PrintScaling { get; init; }
    public PdfDuplexMode Duplex { get; init; }
}
