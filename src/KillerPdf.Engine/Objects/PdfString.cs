namespace KillerPdf.Engine.Objects;

public enum PdfStringForm
{
    Literal,
    Hexadecimal
}

/// <summary>A PDF string's decoded bytes and the lexical form used to represent it.</summary>
public sealed class PdfString : PdfObject
{
    private readonly byte[] _bytes;

    public PdfString(ReadOnlySpan<byte> bytes, PdfStringForm form)
    {
        _bytes = bytes.ToArray();
        Form = form;
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;
    public PdfStringForm Form { get; }
}
