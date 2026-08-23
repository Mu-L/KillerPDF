using System.IO.Compression;
using System.Text;
using KillerPdf.Engine.Filters;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Filters;

public sealed class PdfStreamDecoderTests
{
    [Fact]
    public void Decode_ReturnsUnfilteredBytes()
    {
        byte[] source = [0x00, 0xFF, 0x41];

        Assert.Equal(source, PdfStreamDecoder.Decode(Stream(source)));
    }

    [Fact]
    public void Decode_InflatesZlibData()
    {
        byte[] expected = Encoding.ASCII.GetBytes("PDF 2.0 object stream");
        PdfStream stream = Stream(Compress(expected), Pair("Filter", Name("FlateDecode")));

        Assert.Equal(expected, PdfStreamDecoder.Decode(stream));
    }

    [Fact]
    public void Decode_ReversesPngUpPrediction()
    {
        byte[] predicted = [2, 10, 20, 30, 2, 5, 5, 5];
        PdfDictionary parameters = Dictionary(
            Pair("Predictor", new PdfInteger(12)),
            Pair("Columns", new PdfInteger(3)));
        PdfStream stream = Stream(
            Compress(predicted),
            Pair("Filter", Name("FlateDecode")),
            Pair("DecodeParms", parameters));

        Assert.Equal(new byte[] { 10, 20, 30, 15, 25, 35 }, PdfStreamDecoder.Decode(stream));
    }

    [Fact]
    public void Decode_ReversesTiffPrediction()
    {
        byte[] predicted = [10, 10, 10, 40, 10, 10];
        PdfDictionary parameters = Dictionary(
            Pair("Predictor", new PdfInteger(2)),
            Pair("Columns", new PdfInteger(3)));
        PdfStream stream = Stream(
            Compress(predicted),
            Pair("Filter", Name("FlateDecode")),
            Pair("DecodeParms", parameters));

        Assert.Equal(new byte[] { 10, 20, 30, 40, 50, 60 }, PdfStreamDecoder.Decode(stream));
    }

    [Fact]
    public void Decode_EnforcesOutputLimitWhileInflating()
    {
        PdfStream stream = Stream(Compress(new byte[10_000]), Pair("Filter", Name("FlateDecode")));

        PdfFilterException error = Assert.Throws<PdfFilterException>(() => PdfStreamDecoder.Decode(stream, 100));
        Assert.Contains("safety limit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LZWDecode")]
    [InlineData("DCTDecode")]
    public void Decode_ReportsUnsupportedFilters(string filter)
    {
        PdfStream stream = Stream([], Pair("Filter", Name(filter)));

        Assert.Throws<PdfFilterException>(() => PdfStreamDecoder.Decode(stream));
    }

    [Fact]
    public void Decode_RejectsInvalidZlibData()
    {
        PdfStream stream = Stream("not zlib"u8.ToArray(), Pair("Filter", Name("FlateDecode")));

        Assert.Throws<PdfFilterException>(() => PdfStreamDecoder.Decode(stream));
    }

    private static byte[] Compress(byte[] source)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(source);
        return output.ToArray();
    }

    private static PdfStream Stream(byte[] data, params KeyValuePair<PdfName, PdfObject>[] entries) =>
        new(Dictionary(entries), data);

    private static PdfDictionary Dictionary(params KeyValuePair<PdfName, PdfObject>[] entries) => new(entries);
    private static KeyValuePair<PdfName, PdfObject> Pair(string name, PdfObject value) => new(Name(name), value);
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
