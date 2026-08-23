using KillerPdf.Engine.Documents;
using KillerPdf.Engine.CrossReference;

namespace KillerPdf.Engine.Signing;

/// <summary>Locates valid incremental PDF revisions added after a signed byte range.</summary>
public static class PdfSignedRevisionAnalyzer
{
    public static PdfSignedRevisionAnalysis Analyze(
        PdfDocument document, PdfSignatureInfo signature)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signature);
        if (!signature.HasValidByteRange || signature.ByteRange is not { Count: 4 } range)
            throw new InvalidOperationException("The signature does not have a valid byte range.");
        long signedLength = checked(range[2] + range[3]);
        bool validRevision = false;
        PdfDocument? signedDocument = null;
        if (signedLength <= int.MaxValue)
        {
            try
            {
                signedDocument = PdfDocument.Open(
                    document.Source[..checked((int)signedLength)]);
                validRevision = true;
            }
            catch (Exception exception) when (exception is FormatException
                or ArgumentException
                or InvalidOperationException)
            {
                validRevision = false;
            }
        }
        var laterSections = document.CrossReferences.Sections
            .Where(section => section.Offset >= signedLength)
            .ToArray();
        int[] changedObjects = document.CrossReferences.AllSections
            .Where(section => section.Offset >= signedLength)
            .SelectMany(section => section.Keys)
            .Where(number => number > 0).Distinct().Order().ToArray();
        int[] freedObjects = changedObjects.Where(number =>
            document.CrossReferences.TryGetValue(number, out PdfCrossReferenceEntry entry)
            && entry.Type == PdfCrossReferenceEntryType.Free).ToArray();
        int[] updatedObjects = signedDocument is null ? [] : changedObjects.Where(number =>
            !freedObjects.Contains(number)
            && signedDocument.CrossReferences.TryGetValue(number, out PdfCrossReferenceEntry entry)
            && entry.Type is PdfCrossReferenceEntryType.InUse
                or PdfCrossReferenceEntryType.Compressed).ToArray();
        int[] addedObjects = changedObjects.Except(freedObjects).Except(updatedObjects).ToArray();
        return new PdfSignedRevisionAnalysis
        {
            SignedRevisionLength = signedLength,
            CurrentDocumentLength = document.Source.Length,
            SignedRevisionIsValidPdf = validRevision,
            LaterRevisionCount = laterSections.Length,
            ChangedObjectNumbers = changedObjects,
            AddedObjectNumbers = addedObjects,
            UpdatedObjectNumbers = updatedObjects,
            FreedObjectNumbers = freedObjects
        };
    }
}
