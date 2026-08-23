using System.Text;
using KillerPdf.Engine.Diagnostics;
using KillerPdf.Engine.Syntax;
using Xunit;

namespace KillerPdf.Engine.Tests.Diagnostics;

public sealed class PdfDocumentInspectorTests
{
    [Fact]
    public void Inspect_ReportsAValidDocumentWithoutFindings()
    {
        byte[] source = ClassicPdf("1 0 obj << /Type /Catalog >> endobj\n", includeRoot: true);

        PdfInspectionReport report = PdfDocumentInspector.Inspect(source);

        Assert.True(report.IsStructurallyValid);
        Assert.False(report.RequiresRepair);
        Assert.Equal(PdfVersion.Pdf20, report.Version);
        Assert.NotNull(report.StartXrefOffset);
        Assert.Equal(2, report.CrossReferenceEntryCount);
        Assert.Equal(1, report.InspectedObjectCount);
        Assert.Empty(report.Diagnostics);
    }

    [Fact]
    public void Inspect_ReportsHeaderAndStartXrefFailuresWithoutThrowing()
    {
        PdfInspectionReport report = PdfDocumentInspector.Inspect("not a pdf"u8.ToArray());

        Assert.True(report.RequiresRepair);
        Assert.Contains(report.Diagnostics, item => item.Code == PdfDiagnosticCode.InvalidHeader);
        Assert.Contains(report.Diagnostics, item => item.Code == PdfDiagnosticCode.InvalidStartXref);
        Assert.Null(report.CrossReferenceEntryCount);
    }

    [Fact]
    public void Inspect_DistinguishesABrokenXrefTargetFromABrokenStartXrefDeclaration()
    {
        byte[] source = "%PDF-2.0\nstartxref\n9\n%%EOF\n"u8.ToArray();

        PdfInspectionReport report = PdfDocumentInspector.Inspect(source);

        Assert.DoesNotContain(report.Diagnostics, item => item.Code == PdfDiagnosticCode.InvalidStartXref);
        Assert.Contains(report.Diagnostics, item => item.Code == PdfDiagnosticCode.InvalidCrossReference);
    }

    [Fact]
    public void Inspect_ReportsInvalidTrailerOffsetsWithoutThrowing()
    {
        byte[] source = ClassicPdf(
            "1 0 obj << /Type /Catalog >> endobj\n", includeRoot: true);
        string text = Encoding.ASCII.GetString(source)
            .Replace("/Root 1 0 R", "/Root 1 0 R /Prev -1", StringComparison.Ordinal);

        PdfInspectionReport report = PdfDocumentInspector.Inspect(
            Encoding.ASCII.GetBytes(text));

        Assert.True(report.RequiresRepair);
        Assert.Contains(report.Diagnostics,
            item => item.Code == PdfDiagnosticCode.InvalidCrossReference);
    }

    [Fact]
    public void Inspect_DoesNotThrowForDeterministicallyMutatedPdfBytes()
    {
        byte[] valid = ClassicPdf(
            "1 0 obj << /Type /Catalog >> endobj\n", includeRoot: true);
        var random = new Random(18_00_20);

        for (int sample = 0; sample < 500; sample++)
        {
            byte[] mutated = valid.ToArray();
            int changes = random.Next(1, 9);
            for (int change = 0; change < changes; change++)
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);

            Exception? error = Record.Exception(() => PdfDocumentInspector.Inspect(mutated));
            Assert.Null(error);
        }
    }

    [Fact]
    public void Inspect_IdentifiesTheObjectWhoseXrefEntryPointsToTheWrongHeader()
    {
        byte[] source = ClassicPdf("2 0 obj << /Type /Catalog >> endobj\n", includeRoot: true);

        PdfInspectionReport report = PdfDocumentInspector.Inspect(source);

        PdfDiagnostic finding = Assert.Single(
            report.Diagnostics,
            item => item.Code == PdfDiagnosticCode.InvalidIndirectObject);
        Assert.Equal(1, finding.ObjectNumber);
        Assert.NotNull(finding.Offset);
        Assert.Contains("points to object 2 0", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_ReportsAMissingCatalogSeparatelyFromObjectDamage()
    {
        byte[] source = ClassicPdf("1 0 obj << /Type /Example >> endobj\n", includeRoot: false);

        PdfInspectionReport report = PdfDocumentInspector.Inspect(source);

        Assert.Contains(report.Diagnostics, item => item.Code == PdfDiagnosticCode.MissingCatalogRoot);
        Assert.DoesNotContain(report.Diagnostics, item => item.Code == PdfDiagnosticCode.InvalidIndirectObject);
    }

    [Fact]
    public void Inspect_BoundsObjectResolutionAndReportsTheLimit()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int firstOffset = source.Length;
        source.Append("1 0 obj << /Type /Catalog >> endobj\n");
        int secondOffset = source.Length;
        source.Append("2 0 obj true endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 3\n0000000000 65535 f\n");
        source.Append($"{firstOffset:0000000000} 00000 n\n");
        source.Append($"{secondOffset:0000000000} 00000 n\n");
        source.Append("trailer << /Size 3 /Root 1 0 R >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");

        PdfInspectionReport report = PdfDocumentInspector.Inspect(
            Encoding.ASCII.GetBytes(source.ToString()),
            maximumInspectedObjects: 1);

        Assert.Equal(1, report.InspectedObjectCount);
        Assert.Contains(report.Diagnostics, item => item.Code == PdfDiagnosticCode.InspectionLimitReached);
        Assert.True(report.IsStructurallyValid);
    }

    private static byte[] ClassicPdf(string objectDeclaration, bool includeRoot)
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int objectOffset = source.Length;
        source.Append(objectDeclaration);
        int xrefOffset = source.Length;
        source.Append("xref\n0 2\n0000000000 65535 f\n");
        source.Append($"{objectOffset:0000000000} 00000 n\n");
        source.Append(includeRoot
            ? "trailer << /Size 2 /Root 1 0 R >>\n"
            : "trailer << /Size 2 >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }
}
