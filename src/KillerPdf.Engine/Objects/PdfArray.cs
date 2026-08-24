using System.Collections;

namespace KillerPdf.Engine.Objects;

public sealed class PdfArray : PdfObject, IReadOnlyList<PdfObject>
{
    private readonly PdfObject[] _items;

    public PdfArray(IEnumerable<PdfObject> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
        if (_items.Any(item => item is null))
            throw new ArgumentException(
                "A PDF array cannot contain a null object reference; use PdfNull.Instance.",
                nameof(items));
    }

    public int Count => _items.Length;
    public PdfObject this[int index] => _items[index];

    public IEnumerator<PdfObject> GetEnumerator() => ((IEnumerable<PdfObject>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
