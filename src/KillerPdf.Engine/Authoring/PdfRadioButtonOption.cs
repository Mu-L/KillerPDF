namespace KillerPdf.Engine.Authoring;

public sealed record PdfRadioButtonOption(
    int PageIndex,
    double X,
    double Y,
    double Width,
    double Height,
    string ExportValue);
