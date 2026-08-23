namespace KillerPdf.Engine.Objects;

/// <summary>A PDF stream dictionary and its encoded, undecoded payload bytes.</summary>
public sealed class PdfStream : PdfObject
{
    private readonly byte[] _encodedData;

    public PdfStream(PdfDictionary dictionary, ReadOnlySpan<byte> encodedData)
    {
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _encodedData = encodedData.ToArray();
    }

    public PdfDictionary Dictionary { get; }
    public ReadOnlyMemory<byte> EncodedData => _encodedData;
}
