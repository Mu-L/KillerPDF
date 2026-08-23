using System.Text;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Editing;

/// <summary>Rotates and reorders existing pages through a byte-preserving incremental revision.</summary>
public sealed class PdfIncrementalPageEditor
{
    private static readonly PdfName PagesName = Name("Pages");
    private static readonly PdfName ParentName = Name("Parent");
    private static readonly PdfName RotateName = Name("Rotate");
    private static readonly PdfName MediaBoxName = Name("MediaBox");
    private static readonly PdfName[] InheritableNames =
    [
        Name("Resources"), MediaBoxName, Name("CropBox"), RotateName
    ];

    private readonly PdfDocument _document;
    private readonly PdfPageTree _tree;
    private readonly List<PageState> _pages;
    private bool _orderChanged;
    private bool _rotationChanged;

    public PdfIncrementalPageEditor(PdfDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _tree = PdfPageTree.Read(document);
        _pages = _tree.Pages.Select(page => new PageState(page)).ToList();
    }

    public int PageCount => _pages.Count;

    /// <summary>Moves a page to its final zero-based position.</summary>
    public PdfIncrementalPageEditor MovePage(int sourceIndex, int destinationIndex)
    {
        ValidateIndex(sourceIndex, nameof(sourceIndex));
        ValidateIndex(destinationIndex, nameof(destinationIndex));
        if (sourceIndex == destinationIndex) return this;
        PageState page = _pages[sourceIndex];
        _pages.RemoveAt(sourceIndex);
        _pages.Insert(destinationIndex, page);
        _orderChanged = true;
        return this;
    }

    public PdfIncrementalPageEditor RotateClockwise(int pageIndex) => Rotate(pageIndex, 90);
    public PdfIncrementalPageEditor RotateCounterClockwise(int pageIndex) => Rotate(pageIndex, -90);

    public PdfIncrementalPageEditor SetRotation(int pageIndex, int degreesClockwise)
    {
        ValidateIndex(pageIndex, nameof(pageIndex));
        if (degreesClockwise % 90 != 0)
            throw new ArgumentOutOfRangeException(nameof(degreesClockwise), "Page rotation must be a multiple of 90 degrees.");
        _pages[pageIndex].Rotation = NormalizeRotation(degreesClockwise);
        _rotationChanged = true;
        return this;
    }

    public byte[] Build()
    {
        if (!_orderChanged && !_rotationChanged)
            throw new InvalidOperationException("The incremental page update is empty.");
        var update = new PdfIncrementalUpdateBuilder(_document);
        if (_orderChanged)
            BuildReorderedTree(update);
        else
            BuildRotations(update);
        return update.Build();
    }

    private PdfIncrementalPageEditor Rotate(int pageIndex, int delta)
    {
        ValidateIndex(pageIndex, nameof(pageIndex));
        PageState page = _pages[pageIndex];
        page.Rotation = NormalizeRotation(CurrentRotation(page) + delta);
        _rotationChanged = true;
        return this;
    }

    private void BuildRotations(PdfIncrementalUpdateBuilder update)
    {
        foreach (PageState state in _pages.Where(page => page.Rotation.HasValue))
            update.ReplaceObject(state.Entry.Reference.ObjectNumber,
                Replace(state.Entry.Dictionary, RotateName, new PdfInteger(state.Rotation!.Value)));
    }

    private void BuildReorderedTree(PdfIncrementalUpdateBuilder update)
    {
        PdfIndirectReference newRoot = update.ReserveObject();
        var kids = new PdfArray(_pages.Select(page => (PdfObject)page.Entry.Reference));
        update.SetObject(newRoot, Dictionary(
            ("Type", Name("Pages")), ("Kids", kids), ("Count", new PdfInteger(_pages.Count))));
        update.ReplaceObject(_tree.CatalogReference.ObjectNumber,
            Replace(_tree.Catalog, PagesName, newRoot));

        foreach (PageState state in _pages)
        {
            if (!state.Entry.InheritedValues.ContainsKey(MediaBoxName))
                throw new InvalidOperationException(
                    $"Page {state.Entry.Index + 1} has no effective /MediaBox and cannot be reparented.");
            var replacements = new Dictionary<PdfName, PdfObject>
            {
                [ParentName] = newRoot
            };
            foreach (PdfName name in InheritableNames)
                if (state.Entry.InheritedValues.TryGetValue(name, out PdfObject? value))
                    replacements[name] = value;
            if (state.Rotation.HasValue)
                replacements[RotateName] = new PdfInteger(state.Rotation.Value);
            update.ReplaceObject(state.Entry.Reference.ObjectNumber,
                ReplaceMany(state.Entry.Dictionary, replacements));
        }
    }

    private static int CurrentRotation(PageState state)
    {
        if (state.Rotation.HasValue) return state.Rotation.Value;
        if (!state.Entry.InheritedValues.TryGetValue(RotateName, out PdfObject? value)) return 0;
        if (value is not PdfInteger rotation || rotation.Value is < int.MinValue or > int.MaxValue
            || rotation.Value % 90 != 0)
            throw new InvalidOperationException(
                $"Page {state.Entry.Index + 1} has an invalid inherited /Rotate value.");
        return NormalizeRotation((int)rotation.Value);
    }

    private static int NormalizeRotation(int value)
    {
        int normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private void ValidateIndex(int pageIndex, string parameterName)
    {
        if (pageIndex < 0 || pageIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static PdfDictionary Replace(PdfDictionary source, PdfName name, PdfObject value) =>
        ReplaceMany(source, new Dictionary<PdfName, PdfObject> { [name] = value });

    private static PdfDictionary ReplaceMany(
        PdfDictionary source, IReadOnlyDictionary<PdfName, PdfObject> replacements) =>
        new(source.Where(entry => !replacements.ContainsKey(entry.Key)).Concat(replacements));

    private static PdfDictionary Dictionary(params (string Name, PdfObject Value)[] entries) =>
        new(entries.Select(entry => new KeyValuePair<PdfName, PdfObject>(Name(entry.Name), entry.Value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));

    private sealed class PageState(PdfPageTreeEntry entry)
    {
        internal PdfPageTreeEntry Entry { get; } = entry;
        internal int? Rotation { get; set; }
    }
}
