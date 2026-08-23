using System.Security.Cryptography;
using KillerPdf.Engine.Diagnostics;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Validation;

public sealed record PdfRoundTripResult(
    bool Succeeded,
    bool IsDeterministic,
    string? RewrittenSha256,
    byte[]? RewrittenBytes,
    PdfInspectionReport SourceInspection,
    PdfInspectionReport? RewrittenInspection,
    string? FailureMessage);

/// <summary>Runs the preservation writer through reopen and second-write verification.</summary>
public static class PdfRoundTripValidator
{
    public static PdfRoundTripResult Validate(
        ReadOnlyMemory<byte> source,
        PdfDocumentWriteOptions? options = null)
    {
        PdfInspectionReport sourceInspection = PdfDocumentInspector.Inspect(source);
        if (!sourceInspection.IsStructurallyValid)
        {
            return new PdfRoundTripResult(
                false, false, null, null, sourceInspection, null,
                "The source PDF failed structural inspection.");
        }

        try
        {
            byte[] rewritten = PdfDocumentWriter.Write(PdfDocument.Open(source), options);
            PdfInspectionReport rewrittenInspection = PdfDocumentInspector.Inspect(rewritten);
            if (!rewrittenInspection.IsStructurallyValid)
            {
                return new PdfRoundTripResult(
                    false, false, Hex(rewritten), rewritten, sourceInspection, rewrittenInspection,
                    "The rewritten PDF failed structural inspection.");
            }

            byte[] secondPass = PdfDocumentWriter.Write(PdfDocument.Open(rewritten), options);
            bool deterministic = rewritten.AsSpan().SequenceEqual(secondPass);
            return new PdfRoundTripResult(
                deterministic,
                deterministic,
                Hex(rewritten),
                rewritten,
                sourceInspection,
                rewrittenInspection,
                deterministic ? null : "The second rewrite did not produce identical bytes.");
        }
        catch (Exception error)
        {
            return new PdfRoundTripResult(
                false, false, null, null, sourceInspection, null, error.Message);
        }
    }

    private static string Hex(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
