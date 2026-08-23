using System.Text;

namespace KillerPdf.Engine.Objects;

/// <summary>A PDF name, stored as its decoded bytes rather than assuming a text encoding.</summary>
public sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    private readonly byte[] _bytes;

    public PdfName(ReadOnlySpan<byte> bytes) => _bytes = bytes.ToArray();

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public string ValueAsLatin1() => Encoding.Latin1.GetString(_bytes);

    public bool Equals(PdfName? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as PdfName);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (byte value in _bytes)
            hash.Add(value);
        return hash.ToHashCode();
    }

    public override string ToString() => "/" + ValueAsLatin1();
}
