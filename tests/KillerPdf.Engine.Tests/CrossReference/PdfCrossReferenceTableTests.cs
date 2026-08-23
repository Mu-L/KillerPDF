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
    public void Read_RejectsStartxrefThatDoesNotPointToCrossReferenceData()
    {
        string source = "%PDF-2.0\n1 0 obj true endobj\nstartxref\n9\n%%EOF\n";

        Assert.Throws<PdfSyntaxException>(() =>
            PdfCrossReferenceTable.Read(Encoding.ASCII.GetBytes(source)));
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
