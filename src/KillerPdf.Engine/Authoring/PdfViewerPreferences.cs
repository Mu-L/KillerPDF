using System.Text;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;

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

    internal PdfVersion MinimumVersion(bool requireDocumentTitle = false)
    {
        if (Duplex != PdfDuplexMode.Default || PickTrayByPdfSize)
            return PdfVersion.Pdf17;
        if (PrintScaling == PdfPrintScaling.None)
            return new PdfVersion(1, 6);
        if (DisplayDocumentTitle || requireDocumentTitle)
            return new PdfVersion(1, 4);
        if (ReadingDirection == PdfReadingDirection.RightToLeft)
            return new PdfVersion(1, 3);
        return new PdfVersion(1, 2);
    }

    internal PdfDictionary ToDictionary(bool requireDocumentTitle = false)
    {
        var entries = new List<KeyValuePair<PdfName, PdfObject>>();
        void AddTrue(string name, bool value)
        {
            if (value)
                entries.Add(new KeyValuePair<PdfName, PdfObject>(
                    Name(name), new PdfBoolean(true)));
        }
        AddTrue("HideToolbar", HideToolbar);
        AddTrue("HideMenubar", HideMenuBar);
        AddTrue("HideWindowUI", HideWindowUi);
        AddTrue("FitWindow", FitWindow);
        AddTrue("CenterWindow", CenterWindow);
        AddTrue("DisplayDocTitle", DisplayDocumentTitle || requireDocumentTitle);
        AddTrue("PickTrayByPDFSize", PickTrayByPdfSize);
        if (ReadingDirection == PdfReadingDirection.RightToLeft)
            entries.Add(new KeyValuePair<PdfName, PdfObject>(
                Name("Direction"), Name("R2L")));
        if (PrintScaling == PdfPrintScaling.None)
            entries.Add(new KeyValuePair<PdfName, PdfObject>(
                Name("PrintScaling"), Name("None")));
        if (Duplex != PdfDuplexMode.Default)
            entries.Add(new KeyValuePair<PdfName, PdfObject>(
                Name("Duplex"), Name(Duplex switch
                {
                    PdfDuplexMode.Simplex => "Simplex",
                    PdfDuplexMode.DuplexFlipShortEdge => "DuplexFlipShortEdge",
                    PdfDuplexMode.DuplexFlipLongEdge => "DuplexFlipLongEdge",
                    _ => throw new ArgumentOutOfRangeException(nameof(Duplex))
                })));
        return new PdfDictionary(entries);
    }

    private static PdfName Name(string value) =>
        new(Encoding.ASCII.GetBytes(value));
}
