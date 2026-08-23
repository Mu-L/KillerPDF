namespace KillerPdf.Engine.Syntax;

/// <summary>A malformed lexical construct at a known byte offset.</summary>
public sealed class PdfSyntaxException : FormatException
{
    public PdfSyntaxException(string message, int offset)
        : base($"{message} (byte offset {offset}).")
    {
        Offset = offset;
    }

    public int Offset { get; }
}
