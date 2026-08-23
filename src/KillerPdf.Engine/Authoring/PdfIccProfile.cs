using System.Buffers.Binary;
using System.Text;

namespace KillerPdf.Engine.Authoring;

/// <summary>A bounded ICC profile suitable for an ICCBased colour space or output intent.</summary>
public sealed class PdfIccProfile
{
    private readonly byte[] _data;

    private PdfIccProfile(byte[] data, int componentCount, string colorSpace)
    {
        _data = data;
        ComponentCount = componentCount;
        ColorSpace = colorSpace;
    }

    public ReadOnlyMemory<byte> Data => _data;
    public int ComponentCount { get; }
    public string ColorSpace { get; }

    public static PdfIccProfile Load(ReadOnlyMemory<byte> source)
    {
        byte[] data = source.ToArray();
        if (data.Length < 128)
            throw new FormatException("An ICC profile must contain its complete 128-byte header.");
        uint declaredSize = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (declaredSize < 128 || declaredSize > data.Length)
            throw new FormatException("The ICC profile's declared size points outside the supplied data.");
        if (!data.AsSpan(36, 4).SequenceEqual("acsp"u8))
            throw new FormatException("The ICC profile signature is missing.");
        string colorSpace = Encoding.ASCII.GetString(data, 16, 4);
        int components = colorSpace switch
        {
            "GRAY" => 1,
            "RGB " => 3,
            "CMYK" => 4,
            _ => throw new NotSupportedException(
                $"ICC colour space '{colorSpace.Trim()}' is not yet supported for PDF output intents.")
        };
        if (declaredSize < data.Length)
            Array.Resize(ref data, checked((int)declaredSize));
        return new PdfIccProfile(data, components, colorSpace.TrimEnd());
    }
}
