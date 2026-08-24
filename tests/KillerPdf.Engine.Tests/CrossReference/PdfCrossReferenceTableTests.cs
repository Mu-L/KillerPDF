using System.Text;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;
using Xunit;

namespace KillerPdf.Engine.Tests.CrossReference;

public sealed class PdfCrossReferenceTableTests
{
    [Fact]
    public void Read_MergesIncrementalRevisionsNewestFirstAndInheritsTrailerValues()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int objectOffset = source.Length;
        source.Append("1 0 obj\n(old)\nendobj\n");

        int oldXrefOffset = source.Length;
        source.Append("xref\n0 2\n");
        source.Append("0000000000 65535 f\n");
        source.Append($"{objectOffset:0000000000} 00000 n\n");
        source.Append("trailer\n<< /Size 2 /Root 1 0 R >>\n");

        int newObjectOffset = source.Length;
        source.Append("1 0 obj\n(new)\nendobj\n");
        int newXrefOffset = source.Length;
        source.Append("xref\n1 1\n");
        source.Append($"{newObjectOffset:0000000000} 00000 n\n");
        source.Append($"trailer\n<< /Size 2 /Prev {oldXrefOffset} >>\n");
        source.Append($"startxref\n{newXrefOffset}\n%%EOF\n");

        PdfCrossReferenceTable table = PdfCrossReferenceTable.Read(
            Encoding.ASCII.GetBytes(source.ToString()));

        Assert.Equal(PdfVersion.Pdf20, table.Header.Version);
        Assert.Equal(2, table.Sections.Count);
        Assert.Equal(newObjectOffset, table[1].Field1);
        Assert.True(table.TryGetTrailerValue(Name("Root"), out PdfObject root));
        Assert.Equal(1, Assert.IsType<PdfIndirectReference>(root).ObjectNumber);
    }

    [Fact]
    public void Read_RejectsRevisionCycles()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 1\n0000000000 65535 f\n");
        source.Append($"trailer\n<< /Size 1 /Prev {xrefOffset} >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");

        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() =>
            PdfCrossReferenceTable.Read(Encoding.ASCII.GetBytes(source.ToString())));

        Assert.Contains("cycle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsRevisionChainsBeyondConfiguredLimit()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int? previousOffset = null;
        int latestOffset = 0;
        for (int revision = 0;
             revision <= PdfCrossReferenceTable.MaximumRevisionCount;
             revision++)
        {
            latestOffset = source.Length;
            source.Append("xref\n0 1\n0000000000 65535 f\n");
            source.Append("trailer\n<< /Size 1");
            if (previousOffset.HasValue)
                source.Append($" /Prev {previousOffset.Value}");
            source.Append(" >>\n");
            previousOffset = latestOffset;
        }
        source.Append($"startxref\n{latestOffset}\n%%EOF\n");

        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() =>
            PdfCrossReferenceTable.Read(Encoding.ASCII.GetBytes(source.ToString())));

        Assert.Contains("too many incremental revisions",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsHybridReferenceThatReusesPrimaryOffset()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 1\n0000000000 65535 f\n");
        source.Append($"trailer\n<< /Size 1 /XRefStm {xrefOffset} >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");

        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() =>
            PdfCrossReferenceTable.Read(Encoding.ASCII.GetBytes(source.ToString())));

        Assert.Contains("hybrid cross-reference chain reuses an offset",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsInvalidMergedFreeListTopology()
    {
        PdfSyntaxException activeHead = Assert.Throws<PdfSyntaxException>(() =>
            PdfCrossReferenceTable.Read(InvalidFreeListPdf(cyclic: false)));
        PdfSyntaxException cycle = Assert.Throws<PdfSyntaxException>(() =>
            PdfCrossReferenceTable.Read(InvalidFreeListPdf(cyclic: true)));

        Assert.Contains("free-list points to active or missing object 1",
            activeHead.Message, StringComparison.Ordinal);
        Assert.Contains("free-list chain contains a cycle",
            cycle.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsStartxrefThatDoesNotPointToCrossReferenceData()
    {
        string source = "%PDF-2.0\n1 0 obj true endobj\nstartxref\n9\n%%EOF\n";

        Assert.Throws<PdfSyntaxException>(() =>
            PdfCrossReferenceTable.Read(Encoding.ASCII.GetBytes(source)));
    }

    [Fact]
    public void Read_MergedTrailerPrefersPrimaryOverHybridStreamInSameRevision()
    {
        PdfCrossReferenceTable table = PdfCrossReferenceTable.Read(
            HybridReferencePdf(hybridHasPreviousOffset: false));

        PdfDictionary state = Assert.IsType<PdfDictionary>(
            table.MergedTrailer[Name("PrivateState")]);
        Assert.True(Assert.IsType<PdfBoolean>(state[Name("Enabled")]).Value);
        Assert.True(table.TryGetTrailerValue(Name("PrivateState"), out PdfObject value));
        Assert.True(Assert.IsType<PdfBoolean>(
            Assert.IsType<PdfDictionary>(value)[Name("Enabled")]).Value);

    }

    [Fact]
    public void Read_RejectsPreviousOffsetInHybridCrossReferenceStream()
    {
        PdfSyntaxException error = Assert.Throws<PdfSyntaxException>(() =>
            PdfCrossReferenceTable.Read(
                HybridReferencePdf(hybridHasPreviousOffset: true)));

        Assert.Contains("hybrid cross-reference stream cannot contain /Prev",
            error.Message, StringComparison.Ordinal);
    }

    private static byte[] HybridReferencePdf(bool hybridHasPreviousOffset)
    {
        using var source = new MemoryStream();
        Write("%PDF-2.0\n");
        int catalogOffset = checked((int)source.Position);
        Write("1 0 obj\n<< /Type /Catalog >>\nendobj\n");
        int streamOffset = checked((int)source.Position);
        byte[] rows =
        [
            0, 0, 0, 0, 0, 255, 255,
            1, (byte)(catalogOffset >> 24), (byte)(catalogOffset >> 16),
                (byte)(catalogOffset >> 8), (byte)catalogOffset, 0, 0,
            1, (byte)(streamOffset >> 24), (byte)(streamOffset >> 16),
                (byte)(streamOffset >> 8), (byte)streamOffset, 0, 0
        ];
        Write("2 0 obj\n<< /Type /XRef /Size 3 /W [1 4 2] /Length 21 " +
            (hybridHasPreviousOffset ? "/Prev 0 " : string.Empty) +
            "/PrivateState << /Enabled false >> >>\nstream\n");
        source.Write(rows);
        Write("\nendstream\nendobj\n");
        int tableOffset = checked((int)source.Position);
        Write("xref\n0 3\n");
        Write("0000000000 65535 f\n");
        Write($"{catalogOffset:0000000000} 00000 n\n");
        Write($"{streamOffset:0000000000} 00000 n\n");
        Write($"trailer\n<< /Size 3 /Root 1 0 R /XRefStm {streamOffset} " +
            "/PrivateState << /Enabled true >> >>\n");
        Write($"startxref\n{tableOffset}\n%%EOF\n");
        return source.ToArray();

        void Write(string value) => source.Write(Encoding.ASCII.GetBytes(value));
    }

    private static byte[] InvalidFreeListPdf(bool cyclic)
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int objectOffset = source.Length;
        if (!cyclic)
            source.Append("1 0 obj\ntrue\nendobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 2\n");
        source.Append("0000000001 65535 f\n");
        source.Append(cyclic
            ? "0000000001 00000 f\n"
            : $"{objectOffset:0000000000} 00000 n\n");
        source.Append("trailer\n<< /Size 2 >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
