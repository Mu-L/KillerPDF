using System.Text;
using KillerPdf.Engine.Validation;
using Xunit;

namespace KillerPdf.Engine.Tests.Validation;

public sealed class PdfRoundTripValidatorTests
{
    [Fact]
    public void Validate_ReturnsReopenableBytesAndAStableSha256()
    {
        PdfRoundTripResult result = PdfRoundTripValidator.Validate(ValidPdf());

        Assert.True(result.Succeeded);
        Assert.True(result.IsDeterministic);
        Assert.NotNull(result.RewrittenBytes);
        Assert.Equal(64, result.RewrittenSha256!.Length);
        Assert.True(result.RewrittenInspection!.IsStructurallyValid);
        Assert.Null(result.FailureMessage);
    }

    [Fact]
    public void Validate_ReturnsDiagnosticsInsteadOfThrowingForDamage()
    {
        PdfRoundTripResult result = PdfRoundTripValidator.Validate("broken"u8.ToArray());

        Assert.False(result.Succeeded);
        Assert.True(result.SourceInspection.RequiresRepair);
        Assert.Null(result.RewrittenBytes);
    }

    private static byte[] ValidPdf()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int objectOffset = source.Length;
        source.Append("1 0 obj << /Type /Catalog >> endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 2\n0000000000 65535 f\n");
        source.Append($"{objectOffset:0000000000} 00000 n\n");
        source.Append("trailer << /Size 2 /Root 1 0 R >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }
}
