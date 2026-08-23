using System.Collections;

namespace KillerPdf.Engine.Objects;

public sealed class PdfDictionary : PdfObject, IReadOnlyDictionary<PdfName, PdfObject>
{
    private readonly Dictionary<PdfName, PdfObject> _entries;

    public PdfDictionary(IEnumerable<KeyValuePair<PdfName, PdfObject>> entries)
    {
        _entries = new Dictionary<PdfName, PdfObject>();
        foreach ((PdfName key, PdfObject value) in entries)
        {
            if (!_entries.TryAdd(key, value))
                throw new ArgumentException($"The dictionary contains the duplicate key {key}.", nameof(entries));
        }
    }

    public int Count => _entries.Count;
    public IEnumerable<PdfName> Keys => _entries.Keys;
    public IEnumerable<PdfObject> Values => _entries.Values;
    public PdfObject this[PdfName key] => _entries[key];

    public bool ContainsKey(PdfName key) => _entries.ContainsKey(key);
    public bool TryGetValue(PdfName key, out PdfObject value) => _entries.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<PdfName, PdfObject>> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
