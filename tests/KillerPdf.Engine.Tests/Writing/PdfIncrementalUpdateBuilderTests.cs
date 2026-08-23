using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;
using Xunit;

namespace KillerPdf.Engine.Tests.Writing;

public sealed class PdfIncrementalUpdateBuilderTests
{
    [Fact]
    public void Build_PreservesSourceBytesAndAppendsResolvableRevision()
    {
        byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
        PdfDocument original = PdfDocument.Open(source);
        var rootReference = Assert.IsType<PdfIndirectReference>(original.Trailer[Name("Root")]);
        var root = Assert.IsType<PdfDictionary>(original.Resolve(rootReference));
        var update = new PdfIncrementalUpdateBuilder(original);
        PdfIndirectReference customReference = update.AddObject(Latin1("incremental value"));
        var updatedRoot = new PdfDictionary(root.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("KillerTest"), customReference)));

        byte[] result = update.ReplaceObject(rootReference.ObjectNumber, updatedRoot).Build();
        PdfDocument reopened = PdfDocument.Open(result);
        var reopenedRoot = Assert.IsType<PdfDictionary>(reopened.Resolve(rootReference));
        var oldIds = Assert.IsType<PdfArray>(original.Trailer[Name("ID")]);
        var newIds = Assert.IsType<PdfArray>(reopened.Trailer[Name("ID")]);

        Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
        Assert.Equal(2, reopened.CrossReferences.Sections.Count);
        Assert.Equal("incremental value", DecodeLatin1(
            Assert.IsType<PdfString>(reopened.Resolve(
                Assert.IsType<PdfIndirectReference>(reopenedRoot[Name("KillerTest")])))));
        Assert.Equal(original.CrossReferences.StartXref.Offset,
            Assert.IsType<PdfInteger>(reopened.Trailer[Name("Prev")]).Value);
        Assert.Equal(
            Assert.IsType<PdfString>(oldIds[0]).Bytes.ToArray(),
            Assert.IsType<PdfString>(newIds[0]).Bytes.ToArray());
        Assert.NotEqual(
            Assert.IsType<PdfString>(oldIds[1]).Bytes.ToArray(),
            Assert.IsType<PdfString>(newIds[1]).Bytes.ToArray());
    }

    [Fact]
    public void ReservedObjects_CanReferToEachOther()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        var update = new PdfIncrementalUpdateBuilder(original);
        PdfIndirectReference first = update.ReserveObject();
        PdfIndirectReference second = update.ReserveObject();
        update.SetObject(first, new PdfDictionary([
            new KeyValuePair<PdfName, PdfObject>(Name("Next"), second)]));
        update.SetObject(second, new PdfDictionary([
            new KeyValuePair<PdfName, PdfObject>(Name("Prev"), first)]));

        PdfDocument reopened = PdfDocument.Open(update.Build());
        var firstValue = Assert.IsType<PdfDictionary>(reopened.Resolve(first));
        var secondValue = Assert.IsType<PdfDictionary>(reopened.Resolve(second));

        Assert.Equal(second.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(firstValue[Name("Next")]).ObjectNumber);
        Assert.Equal(first.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(secondValue[Name("Prev")]).ObjectNumber);
    }

    [Fact]
    public void ReplacingAnObject_PreservesItsCurrentGeneration()
    {
        byte[] source = SourceWithGenerationTwo();
        PdfDocument original = PdfDocument.Open(source);
        byte[] result = new PdfIncrementalUpdateBuilder(original)
            .ReplaceObject(1, new PdfInteger(99))
            .Build();
        PdfDocument reopened = PdfDocument.Open(result);

        Assert.Equal(99, Assert.IsType<PdfInteger>(reopened.Resolve(new PdfIndirectReference(1, 2))).Value);
        Assert.IsType<PdfNull>(reopened.Resolve(new PdfIndirectReference(1, 0)));
    }

    [Fact]
    public void Build_IsDeterministicForTheSameSourceAndChanges()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        static byte[] Update(PdfDocument document) => new PdfIncrementalUpdateBuilder(document)
            .AddAndBuild(new PdfInteger(42));

        Assert.Equal(Update(original), Update(original));
    }

    [Fact]
    public void Build_RejectsEmptyOrIncompleteUpdates()
    {
        PdfDocument original = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        Assert.Throws<InvalidOperationException>(() =>
            new PdfIncrementalUpdateBuilder(original).Build());
        var incomplete = new PdfIncrementalUpdateBuilder(original);
        incomplete.ReserveObject();
        Assert.Throws<InvalidOperationException>(() => incomplete.Build());
        Assert.Throws<ArgumentException>(() =>
            new PdfIncrementalUpdateBuilder(original).ReplaceObject(999, new PdfInteger(1)));
    }

    [Fact]
    public void Build_AppendsClassicRevisionToXrefStreamAndReplacesCompressedObject()
    {
        PdfDocument original = PdfDocument.Open(ObjectStreamPdf());
        byte[] result = new PdfIncrementalUpdateBuilder(original)
            .ReplaceObject(2, new PdfDictionary([
                new KeyValuePair<PdfName, PdfObject>(Name("Updated"), new PdfBoolean(true))]))
            .Build();
        PdfDocument reopened = PdfDocument.Open(result);
        var updated = Assert.IsType<PdfDictionary>(reopened.Resolve(2));

        Assert.True(Assert.IsType<PdfBoolean>(updated[Name("Updated")]).Value);
        Assert.Equal(2, reopened.CrossReferences.Sections.Count);
        Assert.False(reopened.Trailer.ContainsKey(Name("Type")));
        Assert.False(reopened.Trailer.ContainsKey(Name("W")));
        Assert.Equal(original.CrossReferences.StartXref.Offset,
            Assert.IsType<PdfInteger>(reopened.Trailer[Name("Prev")]).Value);
    }

    private static byte[] SourceWithGenerationTwo()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int objectOffset = source.Length;
        source.Append("1 2 obj\n7\nendobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 2\n0000000000 65535 f \n");
        source.Append($"{objectOffset:0000000000} 00002 n \n");
        source.Append("trailer\n<< /Size 2 /Root 1 2 R >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }

    private static byte[] ObjectStreamPdf()
    {
        byte[] header = "1 0 2 7 "u8.ToArray();
        byte[] body = "(root)<< /Answer 42 >>"u8.ToArray();
        byte[] objectStreamData = [.. header, .. body];
        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-2.0\n");
        int objectStreamOffset = checked((int)output.Position);
        WriteAscii(output,
            $"5 0 obj << /Type /ObjStm /N 2 /First {header.Length} /Length {objectStreamData.Length} >> stream\n");
        output.Write(objectStreamData);
        WriteAscii(output, "\nendstream endobj\n");
        int xrefOffset = checked((int)output.Position);
        byte[] rows =
        [
            .. XrefRow(0, 0, 65_535),
            .. XrefRow(2, 5, 0),
            .. XrefRow(2, 5, 1),
            .. XrefRow(0, 0, 0),
            .. XrefRow(0, 0, 0),
            .. XrefRow(1, objectStreamOffset, 0),
            .. XrefRow(1, xrefOffset, 0)
        ];
        WriteAscii(output,
            $"6 0 obj << /Type /XRef /Size 7 /Root 1 0 R /W [1 4 2] /Length {rows.Length} >> stream\n");
        output.Write(rows);
        WriteAscii(output, $"\nendstream endobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] XrefRow(byte type, int field1, int field2) =>
    [
        type,
        (byte)(field1 >> 24), (byte)(field1 >> 16), (byte)(field1 >> 8), (byte)field1,
        (byte)(field2 >> 8), (byte)field2
    ];

    private static void WriteAscii(Stream output, string value) =>
        output.Write(Encoding.ASCII.GetBytes(value));

    private static PdfString Latin1(string value) =>
        new(Encoding.Latin1.GetBytes(value), PdfStringForm.Literal);
    private static string DecodeLatin1(PdfString value) => Encoding.Latin1.GetString(value.Bytes.Span);
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}

internal static class PdfIncrementalUpdateBuilderTestExtensions
{
    public static byte[] AddAndBuild(this PdfIncrementalUpdateBuilder builder, PdfObject value)
    {
        builder.AddObject(value);
        return builder.Build();
    }
}
