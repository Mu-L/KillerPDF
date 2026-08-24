using System.Security.Cryptography;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Diagnostics;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
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
        => ValidateCore(source, password: null, options);

    /// <summary>Validates a password-encrypted PDF through two authenticated rewrites.</summary>
    public static PdfRoundTripResult ValidateAuthenticated(
        ReadOnlyMemory<byte> source,
        string password,
        PdfDocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(password);
        return ValidateCore(source, password, options);
    }

    private static PdfRoundTripResult ValidateCore(
        ReadOnlyMemory<byte> source,
        string? password,
        PdfDocumentWriteOptions? options)
    {
        PdfInspectionReport sourceInspection = password is null
            ? PdfDocumentInspector.Inspect(source)
            : PdfDocumentInspector.InspectAuthenticated(source, password);
        if (!sourceInspection.IsStructurallyValid || sourceInspection.RequiresAuthentication)
        {
            string sourceFailure = sourceInspection.Diagnostics.FirstOrDefault(
                item => item.Code == PdfDiagnosticCode.AuthenticationFailed)?.Message
                ?? "The source PDF failed structural inspection.";
            return new PdfRoundTripResult(
                false, false, null, null, sourceInspection, null,
                sourceFailure);
        }

        try
        {
            PdfDocument document = password is null
                ? PdfDocument.Open(source)
                : PdfDocument.Open(source, password);
            byte[] rewritten = PdfDocumentWriter.Write(document, options);
            PdfInspectionReport rewrittenInspection = password is null
                ? PdfDocumentInspector.Inspect(rewritten)
                : PdfDocumentInspector.InspectAuthenticated(rewritten, password);
            if (!rewrittenInspection.IsStructurallyValid
                || rewrittenInspection.RequiresAuthentication)
            {
                return new PdfRoundTripResult(
                    false, false, Hex(rewritten), rewritten, sourceInspection, rewrittenInspection,
                    "The rewritten PDF failed structural inspection.");
            }

            PdfDocument reopened = password is null
                ? PdfDocument.Open(rewritten)
                : PdfDocument.Open(rewritten, password);
            byte[] secondPass = PdfDocumentWriter.Write(reopened, options);
            bool deterministic = rewritten.AsSpan().SequenceEqual(secondPass);
            if (password is not null)
            {
                PdfInspectionReport secondInspection =
                    PdfDocumentInspector.InspectAuthenticated(secondPass, password);
                if (!secondInspection.IsStructurallyValid
                    || secondInspection.RequiresAuthentication)
                    return new PdfRoundTripResult(
                        false, false, Hex(rewritten), rewritten, sourceInspection,
                        rewrittenInspection,
                        "The second authenticated rewrite failed structural inspection.");
                PdfDocument secondDocument = PdfDocument.Open(secondPass, password);
                if (!EquivalentResolvedObjects(reopened, secondDocument))
                    return new PdfRoundTripResult(
                        false, false, Hex(rewritten), rewritten, sourceInspection,
                        rewrittenInspection,
                        "The authenticated rewrites do not contain the same resolved object graph.");
                return new PdfRoundTripResult(
                    true, deterministic, Hex(rewritten), rewritten, sourceInspection,
                    rewrittenInspection, null);
            }
            int firstDifference = deterministic ? -1 : FirstDifference(rewritten, secondPass);
            return new PdfRoundTripResult(
                deterministic,
                deterministic,
                Hex(rewritten),
                rewritten,
                sourceInspection,
                rewrittenInspection,
                deterministic ? null
                    : $"The second rewrite first differs at byte {firstDifference:N0}; "
                        + $"the outputs contain {rewritten.Length:N0} and {secondPass.Length:N0} bytes.");
        }
        catch (Exception error)
        {
            return new PdfRoundTripResult(
                false, false, null, null, sourceInspection, null, error.Message);
        }
    }

    private static string Hex(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static int FirstDifference(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        int sharedLength = Math.Min(first.Length, second.Length);
        for (int index = 0; index < sharedLength; index++)
            if (first[index] != second[index]) return index;
        return sharedLength;
    }

    private static bool EquivalentResolvedObjects(PdfDocument first, PdfDocument second)
    {
        Dictionary<(int ObjectNumber, int Generation), byte[]> firstObjects = CanonicalObjects(first);
        Dictionary<(int ObjectNumber, int Generation), byte[]> secondObjects = CanonicalObjects(second);
        return firstObjects.Count == secondObjects.Count
            && firstObjects.All(entry => secondObjects.TryGetValue(entry.Key, out byte[]? value)
                && entry.Value.AsSpan().SequenceEqual(value))
            && CanonicalTrailer(first).AsSpan().SequenceEqual(CanonicalTrailer(second));

        static Dictionary<(int ObjectNumber, int Generation), byte[]> CanonicalObjects(
            PdfDocument document)
        {
            var result = new Dictionary<(int ObjectNumber, int Generation), byte[]>();
            foreach (PdfCrossReferenceEntry entry in document.CrossReferences.Values.Where(entry =>
                         entry.Type is PdfCrossReferenceEntryType.InUse
                             or PdfCrossReferenceEntryType.Compressed))
            {
                PdfObject value = document.Resolve(entry.ObjectNumber);
                if (value is PdfStream stream
                    && stream.Dictionary.TryGetValue(new PdfName("Type"u8), out PdfObject? type)
                    && type is PdfName name
                    && name.ValueAsLatin1() is "XRef" or "ObjStm")
                    continue;
                int generation = entry.Type == PdfCrossReferenceEntryType.InUse
                    ? checked((int)entry.Field2) : 0;
                result[(entry.ObjectNumber, generation)] = PdfObjectWriter.Write(
                    new PdfIndirectObject(
                        entry.ObjectNumber, generation, value, offset: 0));
            }
            return result;
        }

        static byte[] CanonicalTrailer(PdfDocument document)
        {
            string[] structuralNames =
            ["Type", "Length", "Filter", "DecodeParms", "W", "Index", "Size", "Prev", "XRefStm"];
            var structural = structuralNames.Select(name => new PdfName(
                System.Text.Encoding.ASCII.GetBytes(name))).ToHashSet();
            return PdfObjectWriter.Write(new PdfDictionary(
                document.Trailer.Where(entry => !structural.Contains(entry.Key))));
        }
    }
}
