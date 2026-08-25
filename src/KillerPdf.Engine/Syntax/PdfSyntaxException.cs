namespace KillerPdf.Engine.Syntax;

/// <summary>A malformed lexical construct at a known byte offset.</summary>
public sealed class PdfSyntaxException : FormatException
{
    /// <summary>Creates a syntax error associated with an exact byte offset.</summary>
    public PdfSyntaxException(string message, int offset)
        : base($"{message} (byte offset {offset}).")
    {
        Offset = offset;
    }

    /// <summary>Gets the zero-based byte offset at which parsing failed.</summary>
    public int Offset { get; }
}
