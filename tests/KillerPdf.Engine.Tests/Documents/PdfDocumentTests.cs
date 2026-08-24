using System.Text;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;
using Xunit;

namespace KillerPdf.Engine.Tests.Documents;

public sealed class PdfDocumentTests
{
    [Fact]
    public void Open_ResolvesClassicObjectsAndIndirectStreamLengths()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int lengthOffset = source.Length;
        source.Append("1 0 obj 5 endobj\n");
        int streamOffset = source.Length;
        source.Append("2 0 obj << /Length 1 0 R >> stream\nHello\nendstream endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 3\n");
        source.Append("0000000000 65535 f\n");
        source.Append($"{lengthOffset:0000000000} 00000 n\n");
        source.Append($"{streamOffset:0000000000} 00000 n\n");
        source.Append("trailer << /Size 3 /Root 2 0 R >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");

        PdfDocument document = PdfDocument.Open(Encoding.ASCII.GetBytes(source.ToString()));

        Assert.Equal(PdfVersion.Pdf20, document.Header.Version);
        Assert.Equal(5, Assert.IsType<PdfInteger>(document.Resolve(1)).Value);
        var stream = Assert.IsType<PdfStream>(document.Resolve(new PdfIndirectReference(2, 0)));
        Assert.Equal("Hello", Encoding.ASCII.GetString(stream.EncodedData.Span));
    }

    [Fact]
    public void Open_ResolvesMultipleObjectsFromAnObjectStreamByXrefIndex()
    {
        PdfDocument document = PdfDocument.Open(ObjectStreamPdf());

        Assert.Equal("hello", Text(Assert.IsType<PdfString>(document.Resolve(1))));
        var dictionary = Assert.IsType<PdfDictionary>(document.Resolve(new PdfIndirectReference(2, 0)));
        Assert.Equal(42, Assert.IsType<PdfInteger>(dictionary[Name("Answer")]).Value);
    }

    [Fact]
    public void Resolve_ReturnsNullForMissingFreeAndStaleGenerationReferences()
    {
        PdfDocument document = PdfDocument.Open(ObjectStreamPdf());

        Assert.Same(PdfNull.Instance, document.Resolve(99));
        Assert.Same(PdfNull.Instance, document.Resolve(0));
        Assert.Same(PdfNull.Instance, document.Resolve(new PdfIndirectReference(1, 1)));
    }

    [Fact]
    public void Resolve_RejectsAnObjectStreamIndexThatNamesAnotherObject()
    {
        PdfDocument document = PdfDocument.Open(ObjectStreamPdf(firstCompressedIndex: 1));

        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() => document.Resolve(1));

        Assert.Contains("does not match its compressed cross-reference entry",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsAnObjectStreamWithTrailingHeaderEntries()
    {
        PdfDocument document = PdfDocument.Open(ObjectStreamPdf(header: "1 0 2 7 3 9 "));

        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() => document.Resolve(1));

        Assert.Contains("more entries", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsUnregisteredObjectStreamHeaderEntries()
    {
        PdfDocument document = PdfDocument.Open(ObjectStreamPdf(
            header: "1 0 2 7 3 23 ", objectCount: 3,
            body: "(hello)<< /Answer 42 >>null"));

        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() => document.Resolve(1));

        Assert.Contains("header entry 2 for object 3 does not match",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsOversizedObjectStreamBeforeAllocatingHeaders()
    {
        PdfDocument document = PdfDocument.Open(ObjectStreamPdf(
            objectCount: PdfDocument.MaximumObjectsPerObjectStream + 1));

        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() => document.Resolve(1));

        Assert.Contains("cannot contain more than", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsCyclicIndirectStreamLengths()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int objectOffset = source.Length;
        source.Append("1 0 obj << /Length 1 0 R >> stream\nX\nendstream endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 2\n0000000000 65535 f\n");
        source.Append($"{objectOffset:0000000000} 00000 n\n");
        source.Append("trailer << /Size 2 >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        PdfDocument document = PdfDocument.Open(Encoding.ASCII.GetBytes(source.ToString()));

        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() => document.Resolve(1));

        Assert.Contains("cycle", error.Message, StringComparison.Ordinal);
    }

    private static byte[] ObjectStreamPdf(
        int firstCompressedIndex = 0,
        string header = "1 0 2 7 ",
        int objectCount = 2,
        string body = "(hello)<< /Answer 42 >>")
    {
        byte[] objectStreamData = [
            .. Encoding.ASCII.GetBytes(header), .. Encoding.ASCII.GetBytes(body)];
        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-2.0\n");
        int objectStreamOffset = checked((int)output.Position);
        WriteAscii(
            output,
            $"5 0 obj << /Type /ObjStm /N {objectCount} /First {header.Length} /Length {objectStreamData.Length} >> stream\n");
        output.Write(objectStreamData);
        WriteAscii(output, "\nendstream endobj\n");

        int xrefOffset = checked((int)output.Position);
        byte[] rows =
        [
            .. XrefRow(0, 0, 65_535),
            .. XrefRow(2, 5, firstCompressedIndex),
            .. XrefRow(2, 5, 1),
            .. XrefRow(0, 0, 0),
            .. XrefRow(0, 0, 0),
            .. XrefRow(1, objectStreamOffset, 0),
            .. XrefRow(1, xrefOffset, 0)
        ];
        WriteAscii(output, $"6 0 obj << /Type /XRef /Size 7 /Root 1 0 R /W [1 4 2] /Length {rows.Length} >> stream\n");
        output.Write(rows);
        WriteAscii(output, $"\nendstream endobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] XrefRow(byte type, int field1, int field2) =>
    [
        type,
        (byte)(field1 >> 24),
        (byte)(field1 >> 16),
        (byte)(field1 >> 8),
        (byte)field1,
        (byte)(field2 >> 8),
        (byte)field2
    ];

    private static void WriteAscii(Stream output, string value) =>
        output.Write(Encoding.ASCII.GetBytes(value));

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
    private static string Text(PdfString value) => Encoding.ASCII.GetString(value.Bytes.Span);
}
