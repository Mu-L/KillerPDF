using System.Collections;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.CrossReference;

public sealed class PdfCrossReferenceSection : IReadOnlyDictionary<int, PdfCrossReferenceEntry>
{
    private static readonly PdfName PrevName = new("Prev"u8);
    private static readonly PdfName XRefStmName = new("XRefStm"u8);

    private readonly Dictionary<int, PdfCrossReferenceEntry> _entries;

    internal PdfCrossReferenceSection(
        long offset,
        IEnumerable<PdfCrossReferenceEntry> entries,
        PdfDictionary trailer,
        bool isStream,
        int? streamObjectNumber = null)
    {
        Offset = offset;
        Trailer = trailer;
        IsStream = isStream;
        StreamObjectNumber = streamObjectNumber;
        _entries = entries.ToDictionary(entry => entry.ObjectNumber);
        PreviousOffset = OptionalOffset(trailer, PrevName);
        HybridStreamOffset = OptionalOffset(trailer, XRefStmName);
    }

    public long Offset { get; }
    public PdfDictionary Trailer { get; }
    public bool IsStream { get; }
    public int? StreamObjectNumber { get; }
    public long? PreviousOffset { get; }
    public long? HybridStreamOffset { get; }

    public int Count => _entries.Count;
    public IEnumerable<int> Keys => _entries.Keys;
    public IEnumerable<PdfCrossReferenceEntry> Values => _entries.Values;
    public PdfCrossReferenceEntry this[int key] => _entries[key];

    public bool ContainsKey(int key) => _entries.ContainsKey(key);
    public bool TryGetValue(int key, out PdfCrossReferenceEntry value) => _entries.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<int, PdfCrossReferenceEntry>> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static long? OptionalOffset(PdfDictionary trailer, PdfName name)
    {
        if (!trailer.TryGetValue(name, out PdfObject value))
            return null;
        if (value is not PdfInteger integer || integer.Value < 0)
            throw new ArgumentException($"Trailer {name} must be a non-negative integer.", nameof(trailer));
        return integer.Value;
    }
}
