using System.Text;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Editing;

/// <summary>Edits an existing document's pages through a byte-preserving incremental revision.</summary>
public sealed class PdfIncrementalPageEditor
{
    private static readonly PdfName PagesName = Name("Pages");
    private static readonly PdfName ParentName = Name("Parent");
    private static readonly PdfName RotateName = Name("Rotate");
    private static readonly PdfName MediaBoxName = Name("MediaBox");
    private static readonly PdfName CropBoxName = Name("CropBox");
    private static readonly PdfName AnnotsName = Name("Annots");
    private static readonly PdfName SubtypeName = Name("Subtype");
    private static readonly PdfName WidgetName = Name("Widget");
    private static readonly PdfName StructParentsName = Name("StructParents");
    private static readonly PdfName AcroFormName = Name("AcroForm");
    private static readonly PdfName NamesName = Name("Names");
    private static readonly PdfName DestsName = Name("Dests");
    private static readonly PdfName PageLabelsName = Name("PageLabels");
    private static readonly PdfName DecimalName = Name("D");
    private static readonly PdfName StyleName = Name("S");
    private static readonly PdfName PrefixName = Name("P");
    private static readonly PdfName StartName = Name("St");
    private static readonly PdfName[] InheritableNames =
    [
        Name("Resources"), MediaBoxName, Name("CropBox"), RotateName
    ];

    private readonly PdfDocument _document;
    private readonly PdfPageTree _tree;
    private readonly List<PageState> _pages;
    private bool _orderChanged;
    private bool _rotationChanged;
    private bool _pageBoxesChanged;

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

    /// <summary>Removes a page from the current page order.</summary>
    public PdfIncrementalPageEditor RemovePage(int pageIndex)
    {
        ValidateIndex(pageIndex, nameof(pageIndex));
        _pages.RemoveAt(pageIndex);
        _orderChanged = true;
        return this;
    }

    /// <summary>Inserts a blank page at a zero-based position in the current page order.</summary>
    public PdfIncrementalPageEditor InsertBlankPage(
        int pageIndex, double width = 612, double height = 792)
    {
        ValidateInsertionIndex(pageIndex, nameof(pageIndex));
        _pages.Insert(pageIndex, new PageState(Rectangle(0, 0, width, height)));
        _orderChanged = true;
        return this;
    }

    /// <summary>Appends a blank page to the current page order.</summary>
    public PdfIncrementalPageEditor AddBlankPage(double width = 612, double height = 792) =>
        InsertBlankPage(_pages.Count, width, height);

    /// <summary>Copies a page from another document into the current page order.</summary>
    public PdfIncrementalPageEditor InsertImportedPage(
        int pageIndex, PdfDocument source, int sourcePageIndex)
    {
        ValidateInsertionIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(source);
        PdfPageTree sourceTree = PdfPageTree.Read(source);
        if (sourcePageIndex < 0 || sourcePageIndex >= sourceTree.Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(sourcePageIndex));
        PdfPageTreeEntry sourcePage = sourceTree.Pages[sourcePageIndex];
        ValidateImportablePage(source, sourcePage, allowFormWidgets: false);
        EnsureNotAlreadyImported(source, sourcePage);
        _pages.Insert(pageIndex, new PageState(source, sourceTree, sourcePage, wholeDocument: false));
        _orderChanged = true;
        return this;
    }

    /// <summary>Copies a page from another document to the end of the current page order.</summary>
    public PdfIncrementalPageEditor AddImportedPage(PdfDocument source, int sourcePageIndex) =>
        InsertImportedPage(_pages.Count, source, sourcePageIndex);

    /// <summary>Copies every page from another document into the current page order.</summary>
    public PdfIncrementalPageEditor InsertImportedDocument(int pageIndex, PdfDocument source)
    {
        ValidateInsertionIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(source);
        PdfPageTree sourceTree = PdfPageTree.Read(source);
        bool hasAcroForm = sourceTree.Catalog.ContainsKey(AcroFormName);
        foreach (PdfPageTreeEntry page in sourceTree.Pages)
        {
            ValidateImportablePage(source, page, allowFormWidgets: hasAcroForm);
            EnsureNotAlreadyImported(source, page);
        }
        if (sourceTree.Pages.Count == 0) return this;
        _pages.InsertRange(pageIndex,
            sourceTree.Pages.Select(page =>
                new PageState(source, sourceTree, page, wholeDocument: true)));
        _orderChanged = true;
        return this;
    }

    /// <summary>Copies every page from another document to the end of the current page order.</summary>
    public PdfIncrementalPageEditor AddImportedDocument(PdfDocument source) =>
        InsertImportedDocument(_pages.Count, source);

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

    /// <summary>Sets the page's media box from an origin, width, and height in PDF points.</summary>
    public PdfIncrementalPageEditor SetMediaBox(
        int pageIndex, double x, double y, double width, double height)
    {
        ValidateIndex(pageIndex, nameof(pageIndex));
        _pages[pageIndex].MediaBox = Rectangle(x, y, width, height);
        _pageBoxesChanged = true;
        return this;
    }

    /// <summary>Sets the page's visible crop box from an origin, width, and height in PDF points.</summary>
    public PdfIncrementalPageEditor SetCropBox(
        int pageIndex, double x, double y, double width, double height)
    {
        ValidateIndex(pageIndex, nameof(pageIndex));
        _pages[pageIndex].CropBox = Rectangle(x, y, width, height);
        _pageBoxesChanged = true;
        return this;
    }

    public byte[] Build()
    {
        if (!_orderChanged && !_rotationChanged && !_pageBoxesChanged)
            throw new InvalidOperationException("The incremental page update is empty.");
        var update = new PdfIncrementalUpdateBuilder(_document);
        if (_orderChanged)
            BuildReorderedTree(update);
        else
            BuildPageChanges(update);
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

    private void BuildPageChanges(PdfIncrementalUpdateBuilder update)
    {
        foreach (PageState state in _pages.Where(page =>
                     page.Rotation.HasValue || page.MediaBox is not null || page.CropBox is not null))
        {
            PdfPageTreeEntry entry = state.Entry
                ?? throw new InvalidOperationException("New pages require a rebuilt page tree.");
            var replacements = new Dictionary<PdfName, PdfObject>();
            if (state.Rotation.HasValue)
                replacements[RotateName] = new PdfInteger(state.Rotation.Value);
            if (state.MediaBox is not null)
                replacements[MediaBoxName] = state.MediaBox;
            if (state.CropBox is not null)
                replacements[CropBoxName] = state.CropBox;
            update.ReplaceObject(entry.Reference.ObjectNumber,
                ReplaceMany(entry.Dictionary, replacements));
        }
    }

    private void BuildReorderedTree(PdfIncrementalUpdateBuilder update)
    {
        PdfIndirectReference newRoot = update.ReserveObject();
        var references = new Dictionary<PageState, PdfIndirectReference>();
        foreach (PageState state in _pages)
            references[state] = state.Entry?.Reference ?? update.ReserveObject();
        var importers = new Dictionary<PageState, PdfObjectGraphImporter>();
        List<PageState[]> importedGroups = _pages
                     .Where(page => page.ImportedDocument is not null)
                     .GroupBy(page => page.ImportedDocument!)
                     .Select(group => group.ToArray())
                     .ToList();
        foreach (PageState[] group in importedGroups)
        {
            PageState first = group[0];
            var importer = new PdfObjectGraphImporter(
                first.ImportedDocument!, update,
                first.ImportedTree!.Pages.Select(page => page.Reference.ObjectNumber));
            foreach (PageState state in group)
            {
                importer.SeedPage(state.ImportedEntry!.Reference, references[state]);
                importers[state] = importer;
            }
        }
        var kids = new PdfArray(_pages.Select(page => (PdfObject)references[page]));
        update.SetObject(newRoot, Dictionary(
            ("Type", Name("Pages")), ("Kids", kids), ("Count", new PdfInteger(_pages.Count))));
        var catalogReplacements = new Dictionary<PdfName, PdfObject> { [PagesName] = newRoot };
        AddImportedAcroForm(importedGroups, importers, catalogReplacements);
        AddImportedNamedDestinations(importedGroups, importers, catalogReplacements);
        bool removePageLabels = AddPageLabels(importedGroups, catalogReplacements);
        update.ReplaceObject(_tree.CatalogReference.ObjectNumber,
            ReplaceMany(_tree.Catalog, catalogReplacements,
                removePageLabels ? [PageLabelsName] : null));

        foreach (PageState state in _pages)
        {
            if (state.ImportedEntry is not null)
            {
                BuildImportedPage(update, state, references[state], newRoot, importers[state]);
                continue;
            }
            if (state.Entry is null)
            {
                var entries = new List<(string Name, PdfObject Value)>
                {
                    ("Type", Name("Page")),
                    ("Parent", newRoot),
                    ("MediaBox", state.MediaBox
                        ?? throw new InvalidOperationException("A new page has no /MediaBox.")),
                    ("Resources", new PdfDictionary([]))
                };
                if (state.CropBox is not null) entries.Add(("CropBox", state.CropBox));
                if (state.Rotation.HasValue)
                    entries.Add(("Rotate", new PdfInteger(state.Rotation.Value)));
                update.SetObject(references[state], Dictionary(entries.ToArray()));
                continue;
            }
            PdfPageTreeEntry entry = state.Entry;
            if (state.MediaBox is null && !entry.InheritedValues.ContainsKey(MediaBoxName))
                throw new InvalidOperationException(
                    $"Page {entry.Index + 1} has no effective /MediaBox and cannot be reparented.");
            var replacements = new Dictionary<PdfName, PdfObject>
            {
                [ParentName] = newRoot
            };
            foreach (PdfName name in InheritableNames)
                if (entry.InheritedValues.TryGetValue(name, out PdfObject? value))
                    replacements[name] = value;
            if (state.Rotation.HasValue)
                replacements[RotateName] = new PdfInteger(state.Rotation.Value);
            if (state.MediaBox is not null)
                replacements[MediaBoxName] = state.MediaBox;
            if (state.CropBox is not null)
                replacements[CropBoxName] = state.CropBox;
            update.ReplaceObject(entry.Reference.ObjectNumber,
                ReplaceMany(entry.Dictionary, replacements));
        }
    }

    private void AddImportedAcroForm(
        IEnumerable<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        PageState[][] formGroups = importedGroups.Where(group =>
            group[0].ImportedTree!.Catalog.ContainsKey(AcroFormName)).ToArray();
        if (formGroups.Length == 0) return;
        if (_tree.Catalog.ContainsKey(AcroFormName))
            throw new NotSupportedException(
                "Merging imported form fields with an existing destination AcroForm is not yet supported.");
        if (formGroups.Length > 1)
            throw new NotSupportedException(
                "Merging AcroForms from more than one source document is not yet supported.");
        PageState[] group = formGroups[0];
        PdfPageTree sourceTree = group[0].ImportedTree!;
        if (group.Length != sourceTree.Pages.Count || group.Any(page => !page.ImportedWholeDocument))
            throw new NotSupportedException(
                "A document containing forms must be imported with all of its pages retained.");
        PdfObject sourceAcroForm = sourceTree.Catalog[AcroFormName];
        catalogReplacements[AcroFormName] = importers[group[0]].Import(sourceAcroForm);
    }

    private void AddImportedNamedDestinations(
        IEnumerable<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        var combined = new List<PdfNameTreeEntry>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var namesEntries = new List<KeyValuePair<PdfName, PdfObject>>();
        if (_tree.Catalog.TryGetValue(NamesName, out PdfObject? targetNamesValue))
        {
            PdfDictionary targetNames = ResolveDictionary(
                _document, targetNamesValue, "The destination catalog /Names value");
            namesEntries.AddRange(targetNames.Where(entry => !entry.Key.Equals(DestsName)));
            if (targetNames.TryGetValue(DestsName, out PdfObject? targetDestinations))
                AddEntries(PdfNameTree.Read(_document, targetDestinations), importer: null);
        }

        bool importedAny = false;
        foreach (PageState[] group in importedGroups)
        {
            PdfPageTree sourceTree = group[0].ImportedTree!;
            if (!sourceTree.Catalog.TryGetValue(NamesName, out PdfObject? sourceNamesValue))
                continue;
            PdfDictionary sourceNames = ResolveDictionary(
                group[0].ImportedDocument!, sourceNamesValue, "The source catalog /Names value");
            if (!sourceNames.TryGetValue(DestsName, out PdfObject? sourceDestinations))
                continue;
            if (group.Length != sourceTree.Pages.Count || group.Any(page => !page.ImportedWholeDocument))
                throw new NotSupportedException(
                    "Named destinations require all pages from their source document to be retained.");
            AddEntries(PdfNameTree.Read(group[0].ImportedDocument!, sourceDestinations),
                importers[group[0]]);
            importedAny = true;
        }
        if (!importedAny) return;

        combined.Sort((left, right) =>
            left.Key.Bytes.Span.SequenceCompareTo(right.Key.Bytes.Span));
        var names = new List<PdfObject>(combined.Count * 2);
        foreach (PdfNameTreeEntry entry in combined)
        {
            names.Add(entry.Key);
            names.Add(entry.Value);
        }
        namesEntries.Add(new KeyValuePair<PdfName, PdfObject>(
            DestsName, Dictionary(("Names", new PdfArray(names)))));
        catalogReplacements[NamesName] = new PdfDictionary(namesEntries);

        void AddEntries(
            IEnumerable<PdfNameTreeEntry> entries, PdfObjectGraphImporter? importer)
        {
            foreach (PdfNameTreeEntry entry in entries)
            {
                string key = Convert.ToBase64String(entry.Key.Bytes.Span);
                if (!keys.Add(key))
                    throw new NotSupportedException(
                        "Named destinations from merged documents must have unique names.");
                combined.Add(new PdfNameTreeEntry(
                    entry.Key, importer?.Import(entry.Value) ?? entry.Value));
            }
        }
    }

    private bool AddPageLabels(
        IEnumerable<PageState[]> importedGroups,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        bool targetHasLabels = _tree.Catalog.ContainsKey(PageLabelsName);
        PageState[][] groups = importedGroups.ToArray();
        bool importedHasLabels = groups.Any(group =>
            group[0].ImportedTree!.Catalog.ContainsKey(PageLabelsName));
        if (!targetHasLabels && !importedHasLabels) return false;
        if (_pages.Count == 0) return true;

        IReadOnlyList<PageLabelSpec>? targetLabels = targetHasLabels
            ? ReadPageLabels(_document, _tree)
            : null;
        var importedLabels = new Dictionary<PdfDocument, IReadOnlyList<PageLabelSpec>>();
        foreach (PageState[] group in groups)
        {
            PdfDocument source = group[0].ImportedDocument!;
            PdfPageTree sourceTree = group[0].ImportedTree!;
            if (sourceTree.Catalog.ContainsKey(PageLabelsName))
                importedLabels[source] = ReadPageLabels(source, sourceTree);
        }

        var effective = new List<PageLabelSpec>(_pages.Count);
        for (int index = 0; index < _pages.Count; index++)
        {
            PageState page = _pages[index];
            if (page.Entry is not null)
                effective.Add(targetLabels?[page.Entry.Index]
                    ?? DefaultPageLabel(page.Entry.Index));
            else if (page.ImportedEntry is not null)
                effective.Add(importedLabels.TryGetValue(page.ImportedDocument!, out var labels)
                    ? labels[page.ImportedEntry.Index]
                    : DefaultPageLabel(page.ImportedEntry.Index));
            else
                effective.Add(DefaultPageLabel(index));
        }

        var numbers = new List<PdfObject>();
        PageLabelSpec? previous = null;
        for (int index = 0; index < effective.Count; index++)
        {
            PageLabelSpec label = effective[index];
            if (previous is not null && Continues(previous, label))
            {
                previous = label;
                continue;
            }
            var entries = new List<KeyValuePair<PdfName, PdfObject>>();
            if (label.Style is not null)
                entries.Add(new KeyValuePair<PdfName, PdfObject>(StyleName, label.Style));
            if (label.Prefix is not null)
                entries.Add(new KeyValuePair<PdfName, PdfObject>(PrefixName, label.Prefix));
            if (label.Style is not null && label.Number != 1)
                entries.Add(new KeyValuePair<PdfName, PdfObject>(StartName, new PdfInteger(label.Number)));
            numbers.Add(new PdfInteger(index));
            numbers.Add(new PdfDictionary(entries));
            previous = label;
        }
        catalogReplacements[PageLabelsName] = Dictionary(("Nums", new PdfArray(numbers)));
        return false;
    }

    private static IReadOnlyList<PageLabelSpec> ReadPageLabels(
        PdfDocument document, PdfPageTree tree)
    {
        IReadOnlyList<PdfNumberTreeEntry> ranges = PdfNumberTree
            .Read(document, tree.Catalog[PageLabelsName])
            .OrderBy(entry => entry.Key)
            .ToArray();
        foreach (PdfNumberTreeEntry range in ranges)
            if (range.Key < 0 || range.Key >= tree.Pages.Count)
                throw new InvalidOperationException("A page-label range starts outside the document.");

        var definitions = ranges.Select(range =>
        {
            PdfDictionary dictionary = ResolveDictionary(
                document, range.Value, "A page-label range value");
            PdfName? style = null;
            if (dictionary.TryGetValue(StyleName, out PdfObject? styleValue))
            {
                style = styleValue as PdfName
                    ?? throw new InvalidOperationException("A page-label /S value is not a name.");
                if (style.ValueAsLatin1() is not ("D" or "R" or "r" or "A" or "a"))
                    throw new InvalidOperationException("A page-label /S value is not supported.");
            }
            PdfString? prefix = null;
            if (dictionary.TryGetValue(PrefixName, out PdfObject? prefixValue))
                prefix = prefixValue as PdfString
                    ?? throw new InvalidOperationException("A page-label /P value is not a string.");
            long start = 1;
            if (dictionary.TryGetValue(StartName, out PdfObject? startValue))
            {
                PdfInteger integer = startValue as PdfInteger
                    ?? throw new InvalidOperationException("A page-label /St value is not an integer.");
                if (integer.Value < 1)
                    throw new InvalidOperationException("A page-label /St value must be positive.");
                start = integer.Value;
            }
            if (style is null && prefix is null)
                throw new InvalidOperationException("A page-label range has neither a style nor a prefix.");
            return new PageLabelRange(range.Key, style, prefix, start);
        }).ToArray();

        var result = new List<PageLabelSpec>(tree.Pages.Count);
        int rangeIndex = -1;
        for (int pageIndex = 0; pageIndex < tree.Pages.Count; pageIndex++)
        {
            while (rangeIndex + 1 < definitions.Length
                && definitions[rangeIndex + 1].PageIndex <= pageIndex)
                rangeIndex++;
            if (rangeIndex < 0)
            {
                result.Add(DefaultPageLabel(pageIndex));
                continue;
            }
            PageLabelRange range = definitions[rangeIndex];
            long number;
            try
            {
                number = checked(range.StartNumber + pageIndex - range.PageIndex);
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException("A page-label number exceeds the supported range.", exception);
            }
            result.Add(new PageLabelSpec(range.Style, range.Prefix, number));
        }
        return result;
    }

    private static PageLabelSpec DefaultPageLabel(int pageIndex) =>
        new(DecimalName, null, checked((long)pageIndex + 1));

    private static bool Continues(PageLabelSpec previous, PageLabelSpec current) =>
        Equal(previous.Style, current.Style)
        && Equal(previous.Prefix, current.Prefix)
        && (current.Style is null || current.Number == previous.Number + 1);

    private static bool Equal(PdfName? left, PdfName? right) =>
        left is null ? right is null : right is not null && left.Equals(right);

    private static bool Equal(PdfString? left, PdfString? right) =>
        left is null ? right is null : right is not null && left.Bytes.Span.SequenceEqual(right.Bytes.Span);

    private static PdfDictionary ResolveDictionary(
        PdfDocument document, PdfObject value, string description)
    {
        PdfObject resolved = value is PdfIndirectReference reference
            ? document.Resolve(reference)
            : value;
        return resolved as PdfDictionary
            ?? throw new InvalidOperationException($"{description} is not a dictionary.");
    }

    private static void BuildImportedPage(
        PdfIncrementalUpdateBuilder update,
        PageState state,
        PdfIndirectReference destinationReference,
        PdfIndirectReference newRoot,
        PdfObjectGraphImporter importer)
    {
        PdfPageTreeEntry source = state.ImportedEntry!;
        var entries = source.Dictionary
            .Where(entry => !entry.Key.Equals(ParentName)
                && !InheritableNames.Contains(entry.Key))
            .Select(entry => new KeyValuePair<PdfName, PdfObject>(
                entry.Key, importer.Import(entry.Value)))
            .ToList();
        entries.Add(new KeyValuePair<PdfName, PdfObject>(ParentName, newRoot));
        foreach (PdfName name in InheritableNames)
        {
            PdfObject? value = name.Equals(MediaBoxName) && state.MediaBox is not null
                ? state.MediaBox
                : name.Equals(CropBoxName) && state.CropBox is not null
                    ? state.CropBox
                    : name.Equals(RotateName) && state.Rotation.HasValue
                        ? new PdfInteger(state.Rotation.Value)
                        : source.InheritedValues.TryGetValue(name, out PdfObject? inherited)
                            ? importer.Import(inherited)
                            : null;
            if (value is not null)
                entries.Add(new KeyValuePair<PdfName, PdfObject>(name, value));
        }
        if (!entries.Any(entry => entry.Key.Equals(MediaBoxName)))
            throw new InvalidOperationException(
                $"Imported page {source.Index + 1} has no effective /MediaBox.");
        update.SetObject(destinationReference, new PdfDictionary(entries));
    }

    private static int CurrentRotation(PageState state)
    {
        if (state.Rotation.HasValue) return state.Rotation.Value;
        PdfPageTreeEntry? entry = state.Entry ?? state.ImportedEntry;
        if (entry is null
            || !entry.InheritedValues.TryGetValue(RotateName, out PdfObject? value)) return 0;
        if (value is not PdfInteger rotation || rotation.Value is < int.MinValue or > int.MaxValue
            || rotation.Value % 90 != 0)
            throw new InvalidOperationException(
                $"Page {entry.Index + 1} has an invalid inherited /Rotate value.");
        return NormalizeRotation((int)rotation.Value);
    }

    private static int NormalizeRotation(int value)
    {
        int normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static PdfArray Rectangle(double x, double y, double width, double height)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        ValidatePositiveFinite(width, nameof(width));
        ValidatePositiveFinite(height, nameof(height));
        double right = x + width;
        double top = y + height;
        ValidateFinite(right, nameof(width));
        ValidateFinite(top, nameof(height));
        return new PdfArray([Number(x), Number(y), Number(right), Number(top)]);
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, "Page-box coordinates must be finite.");
    }

    private static void ValidatePositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Page-box dimensions must be finite and positive.");
    }

    private void ValidateIndex(int pageIndex, string parameterName)
    {
        if (pageIndex < 0 || pageIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private void ValidateInsertionIndex(int pageIndex, string parameterName)
    {
        if (pageIndex < 0 || pageIndex > _pages.Count)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateImportablePage(
        PdfDocument source, PdfPageTreeEntry page, bool allowFormWidgets)
    {
        if (page.Dictionary.ContainsKey(StructParentsName))
            throw new NotSupportedException(
                "Tagged pages require structure-tree import, which is not yet supported.");
        if (!page.Dictionary.TryGetValue(AnnotsName, out PdfObject? annotationsValue)) return;
        PdfObject annotations = annotationsValue is PdfIndirectReference reference
            ? source.Resolve(reference)
            : annotationsValue;
        if (annotations is not PdfArray array) return;
        foreach (PdfObject item in array)
        {
            PdfObject resolved = item is PdfIndirectReference annotationReference
                ? source.Resolve(annotationReference)
                : item;
            if (resolved is PdfDictionary annotation
                && annotation.TryGetValue(SubtypeName, out PdfObject? subtype)
                && subtype is PdfName name && name.Equals(WidgetName))
            {
                if (allowFormWidgets) continue;
                throw new NotSupportedException(
                    "Pages containing form widgets must be imported as a complete document.");
            }
        }
    }

    private void EnsureNotAlreadyImported(PdfDocument source, PdfPageTreeEntry page)
    {
        if (_pages.Any(existing => ReferenceEquals(existing.ImportedDocument, source)
            && existing.ImportedEntry is PdfPageTreeEntry imported
            && imported.Reference.ObjectNumber == page.Reference.ObjectNumber
            && imported.Reference.Generation == page.Reference.Generation))
            throw new NotSupportedException(
                "Importing the same source page more than once in one update is not yet supported.");
    }

    private static PdfDictionary ReplaceMany(
        PdfDictionary source, IReadOnlyDictionary<PdfName, PdfObject> replacements,
        IReadOnlyCollection<PdfName>? removals = null) =>
        new(source.Where(entry => !replacements.ContainsKey(entry.Key)
                && (removals is null || !removals.Contains(entry.Key)))
            .Concat(replacements));

    private static PdfDictionary Dictionary(params (string Name, PdfObject Value)[] entries) =>
        new(entries.Select(entry => new KeyValuePair<PdfName, PdfObject>(Name(entry.Name), entry.Value)));
    private static PdfObject Number(double value) => value == Math.Truncate(value)
        && value >= long.MinValue && value <= long.MaxValue
            ? new PdfInteger((long)value)
            : new PdfReal(value);
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));

    private sealed record PageLabelRange(
        long PageIndex, PdfName? Style, PdfString? Prefix, long StartNumber);
    private sealed record PageLabelSpec(PdfName? Style, PdfString? Prefix, long Number);

    private sealed class PageState
    {
        internal PageState(PdfPageTreeEntry entry) => Entry = entry;
        internal PageState(PdfArray mediaBox) => MediaBox = mediaBox;
        internal PageState(
            PdfDocument importedDocument, PdfPageTree importedTree,
            PdfPageTreeEntry importedEntry, bool wholeDocument)
        {
            ImportedDocument = importedDocument;
            ImportedTree = importedTree;
            ImportedEntry = importedEntry;
            ImportedWholeDocument = wholeDocument;
        }
        internal PdfPageTreeEntry? Entry { get; }
        internal PdfDocument? ImportedDocument { get; }
        internal PdfPageTree? ImportedTree { get; }
        internal PdfPageTreeEntry? ImportedEntry { get; }
        internal bool ImportedWholeDocument { get; }
        internal int? Rotation { get; set; }
        internal PdfArray? MediaBox { get; set; }
        internal PdfArray? CropBox { get; set; }
    }
}
