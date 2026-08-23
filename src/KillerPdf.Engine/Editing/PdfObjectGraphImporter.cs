using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Editing;

/// <summary>Copies a source document's reachable object graph into an incremental revision.</summary>
internal sealed class PdfObjectGraphImporter
{
    private static readonly PdfName EncryptName = new("Encrypt"u8);
    private static readonly PdfName LengthName = new("Length"u8);
    private const int MaximumImportedObjects = 1_000_000;

    private readonly PdfDocument _source;
    private readonly PdfIncrementalUpdateBuilder _update;
    private readonly HashSet<int> _sourcePageNumbers;
    private readonly Dictionary<SourceReference, PdfIndirectReference> _references = [];
    private int _importedObjectCount;

    internal PdfObjectGraphImporter(
        PdfDocument source,
        PdfIncrementalUpdateBuilder update,
        IEnumerable<int> sourcePageNumbers)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _update = update ?? throw new ArgumentNullException(nameof(update));
        _sourcePageNumbers = new HashSet<int>(sourcePageNumbers);
        if (source.CrossReferences.TryGetTrailerValue(EncryptName, out _))
            throw new NotSupportedException("Importing pages from encrypted PDFs is not yet supported.");
    }

    internal void SeedPage(PdfIndirectReference source, PdfIndirectReference destination)
    {
        var key = new SourceReference(source.ObjectNumber, source.Generation);
        if (!_references.TryAdd(key, destination))
            throw new InvalidOperationException($"Source page {source.ObjectNumber} was mapped more than once.");
    }

    internal PdfObject Import(PdfObject value) => Import(value, 0);

    private PdfObject Import(PdfObject value, int depth)
    {
        if (depth >= PdfObjectWriter.MaximumNestingDepth)
            throw new InvalidOperationException("The imported PDF object nesting limit was exceeded.");
        return value switch
        {
            PdfIndirectReference reference => ImportReference(reference, depth),
            PdfArray array => new PdfArray(array.Select(item => Import(item, depth + 1))),
            PdfDictionary dictionary => ImportDictionary(dictionary, depth + 1),
            PdfStream stream => new PdfStream(ImportStreamDictionary(stream.Dictionary, depth + 1),
                stream.EncodedData.Span),
            PdfNull or PdfBoolean or PdfInteger or PdfReal or PdfName or PdfString => value,
            _ => throw new NotSupportedException(
                $"PDF object type {value.GetType().FullName} cannot be imported.")
        };
    }

    private PdfObject ImportReference(PdfIndirectReference sourceReference, int depth)
    {
        var key = new SourceReference(sourceReference.ObjectNumber, sourceReference.Generation);
        if (_references.TryGetValue(key, out PdfIndirectReference? mapped)) return mapped;
        if (_sourcePageNumbers.Contains(sourceReference.ObjectNumber))
            throw new NotSupportedException(
                $"The imported page references source page {sourceReference.ObjectNumber}, which was not selected for import.");
        PdfObject sourceValue = _source.Resolve(sourceReference);
        if (sourceValue is PdfNull) return PdfNull.Instance;
        if (_importedObjectCount >= MaximumImportedObjects)
            throw new NotSupportedException("The imported page graph contains too many indirect objects.");
        _importedObjectCount++;
        PdfIndirectReference destinationReference = _update.ReserveObject();
        _references.Add(key, destinationReference);
        _update.SetObject(destinationReference, Import(sourceValue, depth + 1));
        return destinationReference;
    }

    private PdfDictionary ImportDictionary(PdfDictionary dictionary, int depth) =>
        new(dictionary.Select(entry => new KeyValuePair<PdfName, PdfObject>(
            entry.Key, Import(entry.Value, depth))));

    private PdfDictionary ImportStreamDictionary(PdfDictionary dictionary, int depth) =>
        new(dictionary.Where(entry => !entry.Key.Equals(LengthName)).Select(entry =>
            new KeyValuePair<PdfName, PdfObject>(entry.Key, Import(entry.Value, depth))));

    private readonly record struct SourceReference(int ObjectNumber, int Generation);
}
