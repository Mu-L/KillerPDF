using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Editing;

/// <summary>Copies a source document's reachable object graph into an incremental revision.</summary>
internal sealed class PdfObjectGraphImporter
{
    private static readonly PdfName LengthName = new("Length"u8);
    private const int MaximumImportedObjects = 1_000_000;

    private readonly PdfDocument _source;
    private readonly PdfIncrementalUpdateBuilder _update;
    private readonly HashSet<int> _sourcePageNumbers;
    private readonly Dictionary<SourceReference, PdfIndirectReference> _references = [];
    private Func<PdfIndirectReference?, PdfDictionary, PdfDictionary>? _dictionaryTransform;
    private readonly HashSet<SourceReference> _populated = [];
    private readonly HashSet<SourceReference> _populating = [];
    private int _importedObjectCount;

    internal PdfObjectGraphImporter(
        PdfDocument source,
        PdfIncrementalUpdateBuilder update,
        IEnumerable<int> sourcePageNumbers)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _update = update ?? throw new ArgumentNullException(nameof(update));
        _sourcePageNumbers = new HashSet<int>(sourcePageNumbers);
        if (source.IsEncrypted && !source.IsDecrypted)
            throw new InvalidOperationException(
                "An encrypted source PDF must be opened with a password before its pages can be imported.");
    }

    internal void SeedPage(PdfIndirectReference source, PdfIndirectReference destination)
        => SeedReference(source, destination);

    internal void SeedReference(PdfIndirectReference source, PdfIndirectReference destination)
    {
        var key = new SourceReference(source.ObjectNumber, source.Generation);
        if (!_references.TryAdd(key, destination))
            throw new InvalidOperationException($"Source object {source.ObjectNumber} was mapped more than once.");
        _populated.Add(key);
    }

    internal void AddDictionaryTransform(
        Func<PdfIndirectReference?, PdfDictionary, PdfDictionary> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        Func<PdfIndirectReference?, PdfDictionary, PdfDictionary>? previous = _dictionaryTransform;
        _dictionaryTransform = previous is null
            ? transform
            : (reference, dictionary) => transform(reference, previous(reference, dictionary));
    }

    internal PdfIndirectReference ReserveReference(PdfIndirectReference sourceReference)
    {
        var key = new SourceReference(sourceReference.ObjectNumber, sourceReference.Generation);
        if (_references.TryGetValue(key, out PdfIndirectReference? mapped)) return mapped;
        if (_sourcePageNumbers.Contains(sourceReference.ObjectNumber))
            throw new InvalidOperationException("Page references must be seeded before graph import.");
        PdfIndirectReference destination = _update.ReserveObject();
        _references.Add(key, destination);
        return destination;
    }

    internal PdfObject Import(PdfObject value) => Import(value, 0, null);

    internal PdfDictionary ApplyDictionaryTransform(PdfDictionary dictionary) =>
        _dictionaryTransform?.Invoke(null, dictionary) ?? dictionary;

    private PdfObject Import(PdfObject value, int depth, PdfIndirectReference? context)
    {
        if (depth >= PdfObjectWriter.MaximumNestingDepth)
            throw new InvalidOperationException("The imported PDF object nesting limit was exceeded.");
        return value switch
        {
            PdfIndirectReference reference => ImportReference(reference, depth),
            PdfArray array => new PdfArray(array.Select(item => Import(item, depth + 1, null))),
            PdfDictionary dictionary => ImportDictionary(dictionary, depth + 1, context),
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
        if (_references.TryGetValue(key, out PdfIndirectReference? mapped))
        {
            if (!_populated.Contains(key) && !_populating.Contains(key))
                PopulateReference(key, sourceReference, mapped, depth);
            return mapped;
        }
        if (_sourcePageNumbers.Contains(sourceReference.ObjectNumber))
            throw new NotSupportedException(
                $"The imported page references source page {sourceReference.ObjectNumber}, which was not selected for import.");
        if (_source.Resolve(sourceReference) is PdfNull) return PdfNull.Instance;
        if (_importedObjectCount >= MaximumImportedObjects)
            throw new NotSupportedException("The imported page graph contains too many indirect objects.");
        _importedObjectCount++;
        PdfIndirectReference destinationReference = _update.ReserveObject();
        _references.Add(key, destinationReference);
        PopulateReference(key, sourceReference, destinationReference, depth);
        return destinationReference;
    }

    private void PopulateReference(
        SourceReference key, PdfIndirectReference sourceReference,
        PdfIndirectReference destinationReference, int depth)
    {
        PdfObject sourceValue = _source.Resolve(sourceReference);
        if (sourceValue is PdfNull)
            throw new InvalidOperationException("A reserved imported reference resolves to null.");
        _populating.Add(key);
        try
        {
            _update.SetObject(destinationReference,
                Import(sourceValue, depth + 1, sourceReference));
            _populated.Add(key);
        }
        finally
        {
            _populating.Remove(key);
        }
    }

    private PdfDictionary ImportDictionary(
        PdfDictionary dictionary, int depth, PdfIndirectReference? context)
    {
        var imported = new PdfDictionary(dictionary.Select(entry =>
            new KeyValuePair<PdfName, PdfObject>(entry.Key, Import(entry.Value, depth, null))));
        return _dictionaryTransform?.Invoke(context, imported) ?? imported;
    }

    private PdfDictionary ImportStreamDictionary(PdfDictionary dictionary, int depth) =>
        new(dictionary.Where(entry => !entry.Key.Equals(LengthName)).Select(entry =>
            new KeyValuePair<PdfName, PdfObject>(entry.Key, Import(entry.Value, depth, null))));

    private readonly record struct SourceReference(int ObjectNumber, int Generation);
}
