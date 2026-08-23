using System.Collections;

namespace KillerPdf.Engine.Objects;

public sealed class PdfArray : PdfObject, IReadOnlyList<PdfObject>
{
    private readonly PdfObject[] _items;

    public PdfArray(IEnumerable<PdfObject> items) => _items = items.ToArray();

    public int Count => _items.Length;
    public PdfObject this[int index] => _items[index];

    public IEnumerator<PdfObject> GetEnumerator() => ((IEnumerable<PdfObject>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
