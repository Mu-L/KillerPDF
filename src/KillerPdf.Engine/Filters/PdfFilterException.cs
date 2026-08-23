namespace KillerPdf.Engine.Filters;

public sealed class PdfFilterException : Exception
{
    public PdfFilterException(string message) : base(message) { }
    public PdfFilterException(string message, Exception innerException) : base(message, innerException) { }
}
