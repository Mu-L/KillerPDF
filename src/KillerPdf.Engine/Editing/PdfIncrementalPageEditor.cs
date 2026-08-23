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
    private static readonly PdfName StructTreeRootName = Name("StructTreeRoot");
    private static readonly PdfName MarkInfoName = Name("MarkInfo");
    private static readonly PdfName MetadataName = Name("Metadata");
    private static readonly PdfName LanguageName = Name("Lang");
    private static readonly PdfName ViewerPreferencesName = Name("ViewerPreferences");
    private static readonly PdfName OptionalContentPropertiesName = Name("OCProperties");
    private static readonly PdfName OutputIntentsName = Name("OutputIntents");
    private static readonly PdfName AcroFormName = Name("AcroForm");
    private static readonly PdfName FieldsName = Name("Fields");
    private static readonly PdfName DefaultResourcesName = Name("DR");
    private static readonly PdfName DefaultAppearanceName = Name("DA");
    private static readonly PdfName NeedAppearancesName = Name("NeedAppearances");
    private static readonly PdfName SignatureFlagsName = Name("SigFlags");
    private static readonly PdfName CalculationOrderName = Name("CO");
    private static readonly PdfName QuaddingName = Name("Q");
    private static readonly PdfName FieldTypeName = Name("FT");
    private static readonly PdfName FieldName = Name("T");
    private static readonly PdfName KidsName = Name("Kids");
    private static readonly PdfName NamesName = Name("Names");
    private static readonly PdfName DestsName = Name("Dests");
    private static readonly PdfName PageLabelsName = Name("PageLabels");
    private static readonly PdfName OutlinesName = Name("Outlines");
    private static readonly PdfName EmbeddedFilesName = Name("EmbeddedFiles");
    private static readonly PdfName AssociatedFilesName = Name("AF");
    private static readonly PdfName PageModeName = Name("PageMode");
    private static readonly PdfName FirstName = Name("First");
    private static readonly PdfName LastName = Name("Last");
    private static readonly PdfName NextName = Name("Next");
    private static readonly PdfName PrevName = Name("Prev");
    private static readonly PdfName CountName = Name("Count");
    private static readonly PdfName DestinationName = Name("D");
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
    private int _nextImportBatchId;

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
        if (sourceTree.Catalog.ContainsKey(OptionalContentPropertiesName))
            throw new NotSupportedException(
                "Pages using optional-content layers must be imported as a complete document into an empty destination.");
        ValidateImportablePage(source, sourcePage,
            allowFormWidgets: false, allowTaggedPage: false);
        int batchId = _nextImportBatchId++;
        _pages.Insert(pageIndex,
            new PageState(source, sourceTree, sourcePage, wholeDocument: false, batchId));
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
        bool hasStructureTree = sourceTree.Catalog.ContainsKey(StructTreeRootName);
        foreach (PdfPageTreeEntry page in sourceTree.Pages)
            ValidateImportablePage(source, page,
                allowFormWidgets: hasAcroForm, allowTaggedPage: hasStructureTree);
        if (sourceTree.Pages.Count == 0) return this;
        int batchId = _nextImportBatchId++;
        _pages.InsertRange(pageIndex,
            sourceTree.Pages.Select(page =>
                new PageState(source, sourceTree, page, wholeDocument: true, batchId)));
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
        ValidateExistingStructureTreePageSet();
        ValidateExistingOptionalContentPageSet();
        PdfIndirectReference newRoot = update.ReserveObject();
        var references = new Dictionary<PageState, PdfIndirectReference>();
        foreach (PageState state in _pages)
            references[state] = state.Entry?.Reference ?? update.ReserveObject();
        var importers = new Dictionary<PageState, PdfObjectGraphImporter>();
        List<PageState[]> importedGroups = _pages
                     .Where(page => page.ImportedDocument is not null)
                     .GroupBy(page => page.ImportBatchId)
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
        AddImportedDocumentProperties(importedGroups, importers, catalogReplacements);
        AddImportedStructureTree(importedGroups, importers, catalogReplacements);
        AddImportedOptionalContent(importedGroups, importers, catalogReplacements);
        AddImportedAcroForm(importedGroups, importers, catalogReplacements);
        AddImportedNamedDestinations(importedGroups, importers, catalogReplacements);
        AddImportedEmbeddedFiles(importedGroups, importers, catalogReplacements);
        AddImportedLegacyDestinations(importedGroups, importers, catalogReplacements);
        AddImportedOutlines(update, importedGroups, importers, catalogReplacements);
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
        foreach (PageState[] group in formGroups)
            RequireCompleteImport(group, group[0].ImportedTree!, "AcroForms");
        if (!_tree.Catalog.ContainsKey(AcroFormName) && formGroups.Length == 1)
        {
            catalogReplacements[AcroFormName] = importers[formGroups[0][0]].Import(
                formGroups[0][0].ImportedTree!.Catalog[AcroFormName]);
            return;
        }

        PdfDictionary? targetForm = _tree.Catalog.TryGetValue(
            AcroFormName, out PdfObject? targetFormValue)
            ? ResolveDictionary(_document, targetFormValue, "The destination /AcroForm")
            : null;
        var formsToMerge = new List<PdfDictionary>();
        if (targetForm is not null) formsToMerge.Add(targetForm);
        formsToMerge.AddRange(formGroups.Select(group => ResolveDictionary(
            group[0].ImportedDocument!, group[0].ImportedTree!.Catalog[AcroFormName],
            "The source /AcroForm")));
        PdfName[] supportedFormKeys =
            [FieldsName, DefaultResourcesName, DefaultAppearanceName, NeedAppearancesName,
                SignatureFlagsName, CalculationOrderName, QuaddingName];
        if (formsToMerge.SelectMany(form => form.Keys).Any(key => !supportedFormKeys.Contains(key)))
            throw new NotSupportedException(
                "Merging AcroForms with XFA or unknown catalog-level extensions is not yet supported.");
        bool hasNeedAppearances = false;
        bool needAppearances = false;
        long signatureFlags = 0;
        var calculationOrder = new List<PdfObject>();
        foreach (PdfDictionary form in formsToMerge)
            if (form.TryGetValue(NeedAppearancesName, out PdfObject? value))
            {
                hasNeedAppearances = true;
                needAppearances |= (value as PdfBoolean)?.Value
                    ?? throw new InvalidOperationException("An /AcroForm /NeedAppearances value is not boolean.");
            }
        if (targetForm is not null)
        {
            signatureFlags |= ReadSignatureFlags(targetForm);
            if (targetForm.TryGetValue(CalculationOrderName, out PdfObject? order))
                calculationOrder.AddRange(ResolveArray(
                    _document, order, "The destination /AcroForm /CO value"));
        }
        var mergedFields = new List<PdfObject>();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        var resourceCategories = new Dictionary<PdfName, List<KeyValuePair<PdfName, PdfObject>>>();
        var usedResourceNames = new HashSet<PdfName>();
        int nextResource = 1;

        if (targetForm is not null)
        {
            AddFieldNames(_document, targetForm, fieldNames);
            mergedFields.AddRange(FormFields(_document, targetForm));
            AddResources(_document, targetForm, importer: null, renames: null);
        }

        var sourceForms = new List<(PdfDictionary Form, PdfObjectGraphImporter Importer,
            Dictionary<PdfName, PdfName> Renames)>();
        foreach (PageState[] group in formGroups)
        {
            PdfDocument source = group[0].ImportedDocument!;
            PdfDictionary form = ResolveDictionary(source,
                group[0].ImportedTree!.Catalog[AcroFormName], "The source /AcroForm");
            AddFieldNames(source, form, fieldNames);
            var renames = new Dictionary<PdfName, PdfName>();
            PdfObjectGraphImporter importer = importers[group[0]];
            PrepareResourceNames(source, form, renames);
            PdfString? defaultAppearance = form.TryGetValue(
                DefaultAppearanceName, out PdfObject? da) ? da as PdfString : null;
            PdfInteger? defaultQuadding = form.TryGetValue(
                QuaddingName, out PdfObject? q) ? q as PdfInteger : null;
            importer.AddDictionaryTransform((_, dictionary) =>
                TransformFormDictionary(dictionary, renames, defaultAppearance, defaultQuadding));
            AddResources(source, form, importer, renames);
            mergedFields.AddRange(FormFields(source, form).Select(importer.Import));
            signatureFlags |= ReadSignatureFlags(form);
            if (form.TryGetValue(CalculationOrderName, out PdfObject? order))
                calculationOrder.AddRange(ResolveArray(source, order,
                    "A source /AcroForm /CO value").Select(importer.Import));
            sourceForms.Add((form, importer, renames));
        }

        PdfDictionary baseForm = targetForm ?? sourceForms[0].Form;
        PdfObjectGraphImporter? baseImporter = targetForm is null ? sourceForms[0].Importer : null;
        Dictionary<PdfName, PdfName>? baseRenames = targetForm is null ? sourceForms[0].Renames : null;
        var formEntries = baseForm
            .Where(entry => !entry.Key.Equals(FieldsName)
                && !entry.Key.Equals(DefaultResourcesName)
                && !entry.Key.Equals(NeedAppearancesName)
                && !entry.Key.Equals(SignatureFlagsName)
                && !entry.Key.Equals(CalculationOrderName))
            .Select(entry => new KeyValuePair<PdfName, PdfObject>(entry.Key,
                entry.Key.Equals(DefaultAppearanceName) && entry.Value is PdfString appearance
                    ? RewriteDefaultAppearance(appearance, baseRenames)
                    : baseImporter?.Import(entry.Value) ?? entry.Value))
            .ToList();
        formEntries.Add(new KeyValuePair<PdfName, PdfObject>(
            FieldsName, new PdfArray(mergedFields)));
        if (hasNeedAppearances)
            formEntries.Add(new KeyValuePair<PdfName, PdfObject>(
                NeedAppearancesName, new PdfBoolean(needAppearances)));
        if (signatureFlags != 0)
            formEntries.Add(new KeyValuePair<PdfName, PdfObject>(
                SignatureFlagsName, new PdfInteger(signatureFlags)));
        if (calculationOrder.Count > 0)
            formEntries.Add(new KeyValuePair<PdfName, PdfObject>(
                CalculationOrderName, new PdfArray(calculationOrder)));
        if (resourceCategories.Count > 0)
            formEntries.Add(new KeyValuePair<PdfName, PdfObject>(DefaultResourcesName,
                new PdfDictionary(resourceCategories.Select(category =>
                    new KeyValuePair<PdfName, PdfObject>(
                        category.Key, new PdfDictionary(category.Value))))));
        catalogReplacements[AcroFormName] = new PdfDictionary(formEntries);

        void PrepareResourceNames(PdfDocument document, PdfDictionary form,
            IDictionary<PdfName, PdfName> renames)
        {
            if (!form.TryGetValue(DefaultResourcesName, out PdfObject? value)) return;
            PdfDictionary resources = ResolveDictionary(document, value, "An /AcroForm /DR value");
            foreach (PdfName resourceName in resources.SelectMany(category =>
                         ResolveDictionary(document, category.Value, "An /AcroForm resource category").Keys)
                .Distinct())
            {
                PdfName replacement;
                do replacement = Name($"KPF{nextResource++}");
                while (usedResourceNames.Contains(replacement));
                renames[resourceName] = replacement;
                usedResourceNames.Add(replacement);
            }
        }

        void AddResources(PdfDocument document, PdfDictionary form,
            PdfObjectGraphImporter? importer, IReadOnlyDictionary<PdfName, PdfName>? renames)
        {
            if (!form.TryGetValue(DefaultResourcesName, out PdfObject? value)) return;
            PdfDictionary resources = ResolveDictionary(document, value, "An /AcroForm /DR value");
            foreach (var category in resources)
            {
                PdfDictionary dictionary = ResolveDictionary(
                    document, category.Value, "An /AcroForm resource category");
                if (!resourceCategories.TryGetValue(category.Key, out var entries))
                    resourceCategories[category.Key] = entries = [];
                foreach (var entry in dictionary)
                {
                    PdfName name = renames is not null && renames.TryGetValue(entry.Key, out PdfName? renamed)
                        ? renamed : entry.Key;
                    usedResourceNames.Add(name);
                    entries.Add(new KeyValuePair<PdfName, PdfObject>(
                        name, importer?.Import(entry.Value) ?? entry.Value));
                }
            }
        }
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
            IReadOnlyList<PdfNameTreeEntry> sourceEntries = PdfNameTree.Read(
                group[0].ImportedDocument!, sourceDestinations);
            if (!IsCompleteImport(group, sourceTree)
                && !DestinationsStayWithinImportedPages(group[0].ImportedDocument!,
                    sourceEntries.Select(entry => entry.Value), group))
                throw new NotSupportedException(
                    "Named destinations outside the selected source pages cannot be preserved.");
            AddSourceEntries(sourceEntries, importers[group[0]]);
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

        void AddSourceEntries(
            IEnumerable<PdfNameTreeEntry> entries, PdfObjectGraphImporter importer)
        {
            var prepared = new List<PdfNameTreeEntry>();
            var renames = new Dictionary<string, PdfString>(StringComparer.Ordinal);
            foreach (PdfNameTreeEntry entry in entries)
            {
                string originalKey = Convert.ToBase64String(entry.Key.Bytes.Span);
                PdfString key = entry.Key;
                int suffix = 2;
                while (!keys.Add(Convert.ToBase64String(key.Bytes.Span)))
                    key = AppendDestinationSuffix(entry.Key, suffix++);
                if (!key.Bytes.Span.SequenceEqual(entry.Key.Bytes.Span))
                    renames[originalKey] = key;
                prepared.Add(new PdfNameTreeEntry(key, entry.Value));
            }
            if (renames.Count > 0)
                importer.AddDictionaryTransform((_, dictionary) =>
                    RewriteNamedDestinationReferences(dictionary, renames));
            foreach (PdfNameTreeEntry entry in prepared)
                combined.Add(new PdfNameTreeEntry(entry.Key, importer.Import(entry.Value)));
        }
    }

    private static PdfDictionary RewriteNamedDestinationReferences(
        PdfDictionary dictionary, IReadOnlyDictionary<string, PdfString> renames)
    {
        var replacements = new Dictionary<PdfName, PdfObject>();
        foreach (PdfName name in new[] { Name("Dest"), DestinationName })
            if (dictionary.TryGetValue(name, out PdfObject? value) && value is PdfString text
                && renames.TryGetValue(Convert.ToBase64String(text.Bytes.Span), out PdfString? renamed))
                replacements[name] = renamed;
        return replacements.Count == 0 ? dictionary : ReplaceMany(dictionary, replacements);
    }

    private static PdfString AppendDestinationSuffix(PdfString value, int suffix)
    {
        string addition = $" ({suffix})";
        if (value.Bytes.Length >= 2 && value.Bytes.Span[0] == 0xFE && value.Bytes.Span[1] == 0xFF)
        {
            string text = Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]) + addition;
            byte[] encoded = Encoding.BigEndianUnicode.GetBytes(text);
            byte[] result = new byte[encoded.Length + 2];
            result[0] = 0xFE;
            result[1] = 0xFF;
            encoded.CopyTo(result, 2);
            return new PdfString(result, value.Form);
        }
        byte[] suffixBytes = Encoding.ASCII.GetBytes($"~{suffix}");
        byte[] bytes = new byte[value.Bytes.Length + suffixBytes.Length];
        value.Bytes.Span.CopyTo(bytes);
        suffixBytes.CopyTo(bytes, value.Bytes.Length);
        return new PdfString(bytes, value.Form);
    }

    private static PdfArray FormFields(PdfDocument document, PdfDictionary form)
    {
        if (!form.TryGetValue(FieldsName, out PdfObject? value))
            throw new InvalidOperationException("An /AcroForm has no /Fields array.");
        return ResolveArray(document, value, "An /AcroForm /Fields value");
    }

    private static void AddFieldNames(
        PdfDocument document, PdfDictionary form, ISet<string> names)
    {
        var active = new HashSet<int>();
        foreach (PdfObject field in FormFields(document, form)) Visit(field, 0, []);

        void Visit(PdfObject value, int depth, IReadOnlyList<string> parentPath)
        {
            if (depth > 256)
                throw new InvalidOperationException("The AcroForm field tree is too deeply nested.");
            int? objectNumber = null;
            if (value is PdfIndirectReference reference)
            {
                objectNumber = reference.ObjectNumber;
                if (!active.Add(reference.ObjectNumber))
                    throw new InvalidOperationException("The AcroForm field tree contains a cycle.");
                value = document.Resolve(reference);
            }
            try
            {
                PdfDictionary field = value as PdfDictionary
                    ?? throw new InvalidOperationException("An AcroForm field is not a dictionary.");
                var path = new List<string>(parentPath);
                bool hasPartialName = false;
                if (field.TryGetValue(FieldName, out PdfObject? fieldName))
                {
                    PdfString name = fieldName as PdfString
                        ?? throw new InvalidOperationException("An AcroForm /T value is not a string.");
                    path.Add(Convert.ToBase64String(name.Bytes.Span));
                    hasPartialName = true;
                }
                bool hasKids = field.TryGetValue(KidsName, out PdfObject? kidsValue);
                if (hasPartialName && (field.ContainsKey(FieldTypeName) || !hasKids))
                {
                    string qualifiedName = string.Concat(path.Select(segment =>
                        $"{segment.Length}:{segment}"));
                    if (!names.Add(qualifiedName))
                        throw new NotSupportedException(
                            "Merged AcroForms must have unique field names.");
                }
                if (hasKids)
                    foreach (PdfObject kid in ResolveArray(document, kidsValue,
                                 "An AcroForm field /Kids value"))
                        Visit(kid, depth + 1, path);
            }
            finally
            {
                if (objectNumber.HasValue) active.Remove(objectNumber.Value);
            }
        }
    }

    private static PdfDictionary TransformFormDictionary(
        PdfDictionary dictionary,
        IReadOnlyDictionary<PdfName, PdfName> renames,
        PdfString? formDefaultAppearance,
        PdfInteger? formDefaultQuadding)
    {
        var replacements = new Dictionary<PdfName, PdfObject>();
        if (dictionary.TryGetValue(DefaultAppearanceName, out PdfObject? value))
        {
            PdfString appearance = value as PdfString
                ?? throw new InvalidOperationException("A form field /DA value is not a string.");
            replacements[DefaultAppearanceName] = RewriteDefaultAppearance(appearance, renames);
        }
        else if (formDefaultAppearance is not null
            && dictionary.TryGetValue(FieldTypeName, out PdfObject? fieldType)
            && fieldType is PdfName type && type.ValueAsLatin1() is "Tx" or "Ch")
        {
            replacements[DefaultAppearanceName] =
                RewriteDefaultAppearance(formDefaultAppearance, renames);
        }
        if (formDefaultQuadding is not null && !dictionary.ContainsKey(QuaddingName)
            && dictionary.TryGetValue(FieldTypeName, out PdfObject? quaddingFieldType)
            && quaddingFieldType is PdfName quaddingType
            && quaddingType.ValueAsLatin1() is "Tx" or "Ch")
            replacements[QuaddingName] = formDefaultQuadding;
        return replacements.Count == 0 ? dictionary : ReplaceMany(dictionary, replacements);
    }

    private static long ReadSignatureFlags(PdfDictionary form)
    {
        if (!form.TryGetValue(SignatureFlagsName, out PdfObject? value)) return 0;
        PdfInteger flags = value as PdfInteger
            ?? throw new InvalidOperationException("An /AcroForm /SigFlags value is not an integer.");
        if (flags.Value < 0)
            throw new InvalidOperationException("An /AcroForm /SigFlags value cannot be negative.");
        return flags.Value;
    }

    private static PdfString RewriteDefaultAppearance(
        PdfString appearance, IReadOnlyDictionary<PdfName, PdfName>? renames)
    {
        if (renames is null || renames.Count == 0) return appearance;
        ReadOnlySpan<byte> source = appearance.Bytes.Span;
        using var output = new MemoryStream(source.Length);
        int position = 0;
        while (position < source.Length)
        {
            if (source[position] != (byte)'/')
            {
                output.WriteByte(source[position++]);
                continue;
            }
            int start = position++;
            int valueStart = position;
            while (position < source.Length && !IsAppearanceNameBoundary(source[position]))
                position++;
            ReadOnlySpan<byte> raw = source[valueStart..position];
            byte[]? decoded = DecodeAppearanceName(raw);
            if (decoded is not null && renames.TryGetValue(new PdfName(decoded), out PdfName? replacement))
                output.Write(PdfObjectWriter.Write(replacement));
            else
                output.Write(source[start..position]);
        }
        return new PdfString(output.ToArray(), appearance.Form);
    }

    private static bool IsAppearanceNameBoundary(byte value) =>
        value is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20
            or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
            or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}'
            or (byte)'/' or (byte)'%';

    private static byte[]? DecodeAppearanceName(ReadOnlySpan<byte> raw)
    {
        var decoded = new List<byte>(raw.Length);
        for (int index = 0; index < raw.Length; index++)
        {
            if (raw[index] != (byte)'#')
            {
                decoded.Add(raw[index]);
                continue;
            }
            if (index + 2 >= raw.Length
                || !TryHexNibble(raw[index + 1], out int high)
                || !TryHexNibble(raw[index + 2], out int low))
                return null;
            decoded.Add((byte)((high << 4) | low));
            index += 2;
        }
        return decoded.ToArray();
    }

    private static bool TryHexNibble(byte value, out int nibble)
    {
        if (value is >= (byte)'0' and <= (byte)'9') nibble = value - (byte)'0';
        else if (value is >= (byte)'A' and <= (byte)'F') nibble = value - (byte)'A' + 10;
        else if (value is >= (byte)'a' and <= (byte)'f') nibble = value - (byte)'a' + 10;
        else { nibble = 0; return false; }
        return true;
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

    private void AddImportedLegacyDestinations(
        IEnumerable<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        var entries = new List<KeyValuePair<PdfName, PdfObject>>();
        var names = new HashSet<PdfName>();
        if (_tree.Catalog.TryGetValue(DestsName, out PdfObject? targetValue))
            AddEntries(ResolveDictionary(
                _document, targetValue, "The destination catalog /Dests value"), null);

        bool importedAny = false;
        foreach (PageState[] group in importedGroups)
        {
            PdfPageTree sourceTree = group[0].ImportedTree!;
            if (!sourceTree.Catalog.TryGetValue(DestsName, out PdfObject? sourceValue))
                continue;
            PdfDictionary sourceDestinations = ResolveDictionary(group[0].ImportedDocument!,
                sourceValue, "The source catalog /Dests value");
            if (!IsCompleteImport(group, sourceTree)
                && !DestinationsStayWithinImportedPages(group[0].ImportedDocument!,
                    sourceDestinations.Values, group))
                throw new NotSupportedException(
                    "Legacy named destinations outside the selected source pages cannot be preserved.");
            AddSourceEntries(sourceDestinations, importers[group[0]]);
            importedAny = true;
        }
        if (importedAny)
            catalogReplacements[DestsName] = new PdfDictionary(entries);

        void AddEntries(PdfDictionary dictionary, PdfObjectGraphImporter? importer)
        {
            foreach (var entry in dictionary)
            {
                if (!names.Add(entry.Key))
                    throw new NotSupportedException(
                        "Legacy named destinations from merged documents must have unique names.");
                entries.Add(new KeyValuePair<PdfName, PdfObject>(
                    entry.Key, importer?.Import(entry.Value) ?? entry.Value));
            }
        }

        void AddSourceEntries(PdfDictionary dictionary, PdfObjectGraphImporter importer)
        {
            var prepared = new List<KeyValuePair<PdfName, PdfObject>>();
            var renames = new Dictionary<PdfName, PdfName>();
            foreach (var entry in dictionary)
            {
                PdfName name = entry.Key;
                int suffix = 2;
                while (!names.Add(name)) name = AppendNameSuffix(entry.Key, suffix++);
                if (!name.Equals(entry.Key)) renames[entry.Key] = name;
                prepared.Add(new KeyValuePair<PdfName, PdfObject>(name, entry.Value));
            }
            if (renames.Count > 0)
                importer.AddDictionaryTransform((_, value) =>
                    RewriteLegacyDestinationReferences(value, renames));
            foreach (var entry in prepared)
                entries.Add(new KeyValuePair<PdfName, PdfObject>(
                    entry.Key, importer.Import(entry.Value)));
        }
    }

    private static PdfDictionary RewriteLegacyDestinationReferences(
        PdfDictionary dictionary, IReadOnlyDictionary<PdfName, PdfName> renames)
    {
        var replacements = new Dictionary<PdfName, PdfObject>();
        foreach (PdfName name in new[] { Name("Dest"), DestinationName })
            if (dictionary.TryGetValue(name, out PdfObject? value) && value is PdfName destination
                && renames.TryGetValue(destination, out PdfName? renamed))
                replacements[name] = renamed;
        return replacements.Count == 0 ? dictionary : ReplaceMany(dictionary, replacements);
    }

    private static PdfName AppendNameSuffix(PdfName value, int suffix)
    {
        byte[] suffixBytes = Encoding.ASCII.GetBytes($"~{suffix}");
        byte[] bytes = new byte[value.Bytes.Length + suffixBytes.Length];
        value.Bytes.Span.CopyTo(bytes);
        suffixBytes.CopyTo(bytes, value.Bytes.Length);
        return new PdfName(bytes);
    }

    private void AddImportedEmbeddedFiles(
        IEnumerable<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        PageState[][] groups = importedGroups.ToArray();
        bool hasImportedEmbeddedFiles = groups.Any(group => HasNameTreeCategory(
            group[0].ImportedDocument!, group[0].ImportedTree!.Catalog, EmbeddedFilesName));
        bool hasImportedAssociatedFiles = groups.Any(group =>
            group[0].ImportedTree!.Catalog.ContainsKey(AssociatedFilesName));
        if (!hasImportedEmbeddedFiles && !hasImportedAssociatedFiles) return;

        if (hasImportedEmbeddedFiles)
        {
            PdfDictionary currentNames = CurrentNamesDictionary(catalogReplacements);
            var nameEntries = currentNames
                .Where(entry => !entry.Key.Equals(EmbeddedFilesName)).ToList();
            var files = new List<PdfNameTreeEntry>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (currentNames.TryGetValue(EmbeddedFilesName, out PdfObject? targetFiles))
                AddFiles(PdfNameTree.Read(_document, targetFiles), null);
            foreach (PageState[] group in groups)
            {
                PdfDocument source = group[0].ImportedDocument!;
                PdfPageTree sourceTree = group[0].ImportedTree!;
                if (!TryGetNameTreeCategory(
                    source, sourceTree.Catalog, EmbeddedFilesName, out PdfObject? sourceFiles))
                    continue;
                RequireCompleteImport(group, sourceTree, "Embedded files");
                AddFiles(PdfNameTree.Read(source, sourceFiles!), importers[group[0]]);
            }
            files.Sort((left, right) =>
                left.Key.Bytes.Span.SequenceCompareTo(right.Key.Bytes.Span));
            var values = new List<PdfObject>(files.Count * 2);
            foreach (PdfNameTreeEntry file in files)
            {
                values.Add(file.Key);
                values.Add(file.Value);
            }
            nameEntries.Add(new KeyValuePair<PdfName, PdfObject>(
                EmbeddedFilesName, Dictionary(("Names", new PdfArray(values)))));
            catalogReplacements[NamesName] = new PdfDictionary(nameEntries);

            void AddFiles(
                IEnumerable<PdfNameTreeEntry> entries, PdfObjectGraphImporter? importer)
            {
                foreach (PdfNameTreeEntry entry in entries)
                {
                    if (!keys.Add(Convert.ToBase64String(entry.Key.Bytes.Span)))
                        throw new NotSupportedException(
                            "Embedded files from merged documents must have unique names.");
                    files.Add(new PdfNameTreeEntry(
                        entry.Key, importer?.Import(entry.Value) ?? entry.Value));
                }
            }
        }

        if (hasImportedAssociatedFiles)
        {
            var associated = new List<PdfObject>();
            if (_tree.Catalog.TryGetValue(AssociatedFilesName, out PdfObject? targetAssociated))
                associated.AddRange(ResolveArray(
                    _document, targetAssociated, "The destination catalog /AF value"));
            foreach (PageState[] group in groups)
            {
                PdfPageTree sourceTree = group[0].ImportedTree!;
                if (!sourceTree.Catalog.TryGetValue(
                    AssociatedFilesName, out PdfObject? sourceAssociated)) continue;
                RequireCompleteImport(group, sourceTree, "Associated files");
                associated.AddRange(ResolveArray(group[0].ImportedDocument!, sourceAssociated,
                    "The source catalog /AF value").Select(importers[group[0]].Import));
            }
            catalogReplacements[AssociatedFilesName] = new PdfArray(associated);
        }
    }

    private void AddImportedOutlines(
        PdfIncrementalUpdateBuilder update,
        IEnumerable<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        PageState[][] outlineGroups = importedGroups.Where(group =>
            group[0].ImportedTree!.Catalog.ContainsKey(OutlinesName)).ToArray();
        if (outlineGroups.Length == 0) return;
        foreach (PageState[] group in outlineGroups)
            RequireCompleteImport(group, group[0].ImportedTree!, "Bookmarks");

        PdfIndirectReference? targetRootReference = null;
        PdfDictionary? targetRoot = null;
        PdfIndirectReference? targetFirst = null;
        PdfIndirectReference? targetLast = null;
        long targetCount = 0;
        bool targetRootWasDirect = false;
        if (_tree.Catalog.TryGetValue(OutlinesName, out PdfObject? targetOutlineValue))
        {
            targetRootReference = targetOutlineValue as PdfIndirectReference;
            targetRootWasDirect = targetRootReference is null;
            targetRoot = ResolveDictionary(
                _document, targetOutlineValue, "The destination bookmark root");
            PdfIndirectReference[] targetTopLevel = ReadTopLevelOutlines(_document, targetRoot);
            if (targetTopLevel.Length > 0)
            {
                targetFirst = targetTopLevel[0];
                targetLast = targetTopLevel[^1];
            }
            targetCount = OutlineCount(targetRoot, targetTopLevel.Length);
        }

        var importedSegments = new List<ImportedOutlineSegment>();
        foreach (PageState[] group in outlineGroups)
        {
            PdfDocument source = group[0].ImportedDocument!;
            PdfDictionary root = ResolveDictionary(source,
                group[0].ImportedTree!.Catalog[OutlinesName], "A source bookmark root");
            PdfIndirectReference[] topLevel = ReadTopLevelOutlines(source, root);
            if (topLevel.Length == 0) continue;
            PdfObjectGraphImporter importer = importers[group[0]];
            var mapped = topLevel.ToDictionary(reference => OutlineKey(reference),
                importer.ReserveReference);
            importedSegments.Add(new ImportedOutlineSegment(
                source, root, importer, topLevel, mapped, OutlineCount(root, topLevel.Length)));
        }
        if (importedSegments.Count == 0) return;

        PdfIndirectReference mergedRoot = targetRootReference ?? update.ReserveObject();
        var segmentStarts = new List<PdfIndirectReference>();
        var segmentEnds = new List<PdfIndirectReference>();
        if (targetFirst is not null)
        {
            segmentStarts.Add(targetFirst);
            segmentEnds.Add(targetLast!);
        }
        segmentStarts.AddRange(importedSegments.Select(segment =>
            segment.Mapped[OutlineKey(segment.TopLevel[0])]));
        segmentEnds.AddRange(importedSegments.Select(segment =>
            segment.Mapped[OutlineKey(segment.TopLevel[^1])]));

        int offset = targetFirst is null ? 0 : 1;
        for (int segmentIndex = 0; segmentIndex < importedSegments.Count; segmentIndex++)
        {
            ImportedOutlineSegment segment = importedSegments[segmentIndex];
            int combinedIndex = segmentIndex + offset;
            PdfIndirectReference? previous = combinedIndex == 0 ? null : segmentEnds[combinedIndex - 1];
            PdfIndirectReference? next = combinedIndex + 1 == segmentStarts.Count
                ? null : segmentStarts[combinedIndex + 1];
            var topLevelKeys = segment.TopLevel.Select(OutlineKey).ToHashSet();
            string firstKey = OutlineKey(segment.TopLevel[0]);
            string lastKey = OutlineKey(segment.TopLevel[^1]);
            segment.Importer.AddDictionaryTransform((reference, dictionary) =>
            {
                if (reference is null || !topLevelKeys.Contains(OutlineKey(reference))) return dictionary;
                var replacements = new Dictionary<PdfName, PdfObject> { [ParentName] = mergedRoot };
                var removals = new List<PdfName>();
                string key = OutlineKey(reference);
                if (key == firstKey)
                {
                    if (previous is null) removals.Add(PrevName); else replacements[PrevName] = previous;
                }
                if (key == lastKey)
                {
                    if (next is null) removals.Add(NextName); else replacements[NextName] = next;
                }
                return ReplaceMany(dictionary, replacements, removals);
            });
            segment.Importer.Import(segment.TopLevel[0]);
        }

        if (targetLast is not null)
        {
            PdfDictionary last = ResolveDictionary(_document, targetLast, "The final destination bookmark");
            var replacements = new Dictionary<PdfName, PdfObject>
            {
                [NextName] = segmentStarts[offset]
            };
            if (targetRootWasDirect) replacements[ParentName] = mergedRoot;
            update.ReplaceObject(targetLast.ObjectNumber,
                ReplaceMany(last, replacements));
        }
        if (targetRootWasDirect)
        {
            foreach (PdfIndirectReference itemReference in ReadTopLevelOutlines(
                         _document, targetRoot!).Where(reference =>
                         targetLast is null || OutlineKey(reference) != OutlineKey(targetLast)))
            {
                PdfDictionary item = ResolveDictionary(
                    _document, itemReference, "A destination bookmark");
                update.ReplaceObject(itemReference.ObjectNumber,
                    ReplaceMany(item, new Dictionary<PdfName, PdfObject>
                    {
                        [ParentName] = mergedRoot
                    }));
            }
        }
        long mergedCount = checked(targetCount + importedSegments.Sum(segment => segment.Count));
        PdfDictionary rootValue = targetRoot is null
            ? Dictionary(("Type", Name("Outlines")))
            : targetRoot;
        PdfDictionary mergedRootValue = ReplaceMany(rootValue,
            new Dictionary<PdfName, PdfObject>
            {
                [FirstName] = segmentStarts[0],
                [LastName] = segmentEnds[^1],
                [CountName] = new PdfInteger(mergedCount)
            });
        if (targetRootReference is null)
        {
            update.SetObject(mergedRoot, mergedRootValue);
            catalogReplacements[OutlinesName] = mergedRoot;
        }
        else
            update.ReplaceObject(targetRootReference.ObjectNumber, mergedRootValue);

        PdfPageTree firstSourceTree = outlineGroups[0][0].ImportedTree!;
        if (!_tree.Catalog.ContainsKey(PageModeName)
            && firstSourceTree.Catalog.TryGetValue(PageModeName, out PdfObject? pageMode))
            catalogReplacements[PageModeName] = importers[outlineGroups[0][0]].Import(pageMode);
    }

    private static PdfIndirectReference[] ReadTopLevelOutlines(
        PdfDocument document, PdfDictionary root)
    {
        bool hasFirst = root.TryGetValue(FirstName, out PdfObject? firstValue);
        bool hasLast = root.TryGetValue(LastName, out PdfObject? lastValue);
        if (!hasFirst && !hasLast) return [];
        if (!hasFirst || !hasLast)
            throw new InvalidOperationException("A bookmark root must contain both /First and /Last.");
        PdfIndirectReference current = firstValue as PdfIndirectReference
            ?? throw new InvalidOperationException("A bookmark root /First value is not an indirect reference.");
        PdfIndirectReference last = lastValue as PdfIndirectReference
            ?? throw new InvalidOperationException("A bookmark root /Last value is not an indirect reference.");
        var result = new List<PdfIndirectReference>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (!visited.Add(OutlineKey(current)))
                throw new InvalidOperationException("The top-level bookmark list contains a cycle.");
            if (result.Count >= 1_000_000)
                throw new NotSupportedException("The bookmark root contains too many top-level items.");
            result.Add(current);
            if (OutlineKey(current) == OutlineKey(last)) break;
            PdfDictionary item = ResolveDictionary(document, current, "A bookmark item");
            current = item.TryGetValue(NextName, out PdfObject? next)
                ? next as PdfIndirectReference
                    ?? throw new InvalidOperationException("A bookmark /Next value is not an indirect reference.")
                : throw new InvalidOperationException("The bookmark list ends before its /Last item.");
        }
        return result.ToArray();
    }

    private static long OutlineCount(PdfDictionary root, int fallback = 0)
    {
        if (!root.TryGetValue(CountName, out PdfObject? value)) return fallback;
        PdfInteger count = value as PdfInteger
            ?? throw new InvalidOperationException("A bookmark root /Count value is not an integer.");
        if (count.Value < 0)
            throw new InvalidOperationException("A bookmark root /Count value cannot be negative.");
        return count.Value;
    }

    private static string OutlineKey(PdfIndirectReference reference) =>
        $"{reference.ObjectNumber}:{reference.Generation}";

    private PdfDictionary CurrentNamesDictionary(
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        if (catalogReplacements.TryGetValue(NamesName, out PdfObject? replacement))
            return replacement as PdfDictionary
                ?? throw new InvalidOperationException("The replacement catalog /Names value is not a dictionary.");
        return _tree.Catalog.TryGetValue(NamesName, out PdfObject? current)
            ? ResolveDictionary(_document, current, "The destination catalog /Names value")
            : new PdfDictionary([]);
    }

    private static bool HasNameTreeCategory(
        PdfDocument document, PdfDictionary catalog, PdfName category) =>
        TryGetNameTreeCategory(document, catalog, category, out _);

    private static bool TryGetNameTreeCategory(
        PdfDocument document, PdfDictionary catalog, PdfName category,
        out PdfObject? value)
    {
        value = null;
        if (!catalog.TryGetValue(NamesName, out PdfObject? namesValue)) return false;
        PdfDictionary names = ResolveDictionary(document, namesValue, "The catalog /Names value");
        return names.TryGetValue(category, out value);
    }

    private static void RequireCompleteImport(
        PageState[] group, PdfPageTree sourceTree, string feature)
    {
        if (group.Length != sourceTree.Pages.Count || group.Any(page => !page.ImportedWholeDocument))
            throw new NotSupportedException(
                $"{feature} can be preserved only when all pages from their source document are retained.");
    }

    private static bool IsCompleteImport(PageState[] group, PdfPageTree sourceTree) =>
        group.Length == sourceTree.Pages.Count && group.All(page => page.ImportedWholeDocument);

    private void AddImportedStructureTree(
        IReadOnlyList<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        PageState[][] taggedGroups = importedGroups.Where(group =>
        {
            PdfPageTree sourceTree = group[0].ImportedTree!;
            return sourceTree.Catalog.ContainsKey(StructTreeRootName)
                || group.Any(page => page.ImportedEntry!.Dictionary.ContainsKey(StructParentsName));
        }).ToArray();
        if (taggedGroups.Length == 0) return;

        PageState[] group = taggedGroups[0];
        PdfPageTree tree = group[0].ImportedTree!;
        bool isOnlyPageSet = taggedGroups.Length == 1
            && _tree.Pages.Count == 0
            && _pages.Count == group.Length
            && _pages.All(group.Contains);
        if (!isOnlyPageSet || !IsCompleteImport(group, tree))
            throw new NotSupportedException(
                "Tagged PDF structure can be preserved only when one complete source document is imported into an empty destination without adding or removing pages.");
        if (!tree.Catalog.ContainsKey(StructTreeRootName))
            throw new InvalidOperationException(
                "An imported page has /StructParents but its source catalog has no /StructTreeRoot.");

        PdfObjectGraphImporter importer = importers[group[0]];
        foreach (PdfName name in new[]
                 {
                     StructTreeRootName, MarkInfoName
                 })
        {
            if (tree.Catalog.TryGetValue(name, out PdfObject? value))
                catalogReplacements[name] = importer.Import(value);
        }
    }

    private void AddImportedDocumentProperties(
        IReadOnlyList<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        if (importedGroups.Count != 1 || _tree.Pages.Count != 0) return;
        PageState[] group = importedGroups[0];
        PdfPageTree tree = group[0].ImportedTree!;
        if (_pages.Count != group.Length || !_pages.All(group.Contains)
            || !IsCompleteImport(group, tree)) return;

        PdfObjectGraphImporter importer = importers[group[0]];
        foreach (PdfName name in new[]
                 {
                     MetadataName, LanguageName, ViewerPreferencesName, OutputIntentsName
                 })
        {
            if (tree.Catalog.TryGetValue(name, out PdfObject? value))
                catalogReplacements[name] = importer.Import(value);
        }
    }

    private void ValidateExistingStructureTreePageSet()
    {
        if (!_tree.Catalog.ContainsKey(StructTreeRootName)) return;
        bool retainsEveryOriginalPage = _pages.Count == _tree.Pages.Count
            && _pages.All(page => page.Entry is not null)
            && _pages.Select(page => page.Entry!.Reference.ObjectNumber).Order()
                .SequenceEqual(_tree.Pages.Select(page => page.Reference.ObjectNumber).Order());
        if (!retainsEveryOriginalPage)
            throw new NotSupportedException(
                "Adding or removing pages in an existing tagged PDF requires structure-tree editing, which is not yet supported. Reordering the complete existing page set is supported.");
    }

    private void AddImportedOptionalContent(
        IReadOnlyList<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        PageState[][] layeredGroups = importedGroups.Where(group =>
            group[0].ImportedTree!.Catalog.ContainsKey(OptionalContentPropertiesName)).ToArray();
        if (layeredGroups.Length == 0) return;

        PageState[] group = layeredGroups[0];
        PdfPageTree tree = group[0].ImportedTree!;
        bool isOnlyPageSet = layeredGroups.Length == 1
            && _tree.Pages.Count == 0
            && _pages.Count == group.Length
            && _pages.All(group.Contains);
        if (!isOnlyPageSet || !IsCompleteImport(group, tree))
            throw new NotSupportedException(
                "Optional-content layers can be preserved only when one complete source document is imported into an empty destination without adding or removing pages.");
        catalogReplacements[OptionalContentPropertiesName] =
            importers[group[0]].Import(tree.Catalog[OptionalContentPropertiesName]);
    }

    private void ValidateExistingOptionalContentPageSet()
    {
        if (!_tree.Catalog.ContainsKey(OptionalContentPropertiesName)) return;
        bool retainsEveryOriginalPage = _pages.Count == _tree.Pages.Count
            && _pages.All(page => page.Entry is not null)
            && _pages.Select(page => page.Entry!.Reference.ObjectNumber).Order()
                .SequenceEqual(_tree.Pages.Select(page => page.Reference.ObjectNumber).Order());
        if (!retainsEveryOriginalPage)
            throw new NotSupportedException(
                "Adding or removing pages in a PDF with optional-content layers requires layer-configuration editing, which is not yet supported. Reordering the complete existing page set is supported.");
    }

    private static bool DestinationsStayWithinImportedPages(
        PdfDocument document, IEnumerable<PdfObject> destinations, PageState[] group)
    {
        var retainedPages = group.Select(page => page.ImportedEntry!.Reference.ObjectNumber)
            .ToHashSet();
        return destinations.All(destination => TargetsRetainedPage(destination, 0));

        bool TargetsRetainedPage(PdfObject value, int depth)
        {
            if (depth > 32)
                throw new InvalidOperationException("A named destination is too deeply indirect.");
            if (value is PdfIndirectReference reference)
            {
                if (group[0].ImportedTree!.Pages.Any(page =>
                        page.Reference.ObjectNumber == reference.ObjectNumber))
                    return retainedPages.Contains(reference.ObjectNumber);
                value = document.Resolve(reference);
            }
            if (value is PdfArray array && array.Count > 0)
                return TargetsRetainedPage(array[0], depth + 1);
            if (value is PdfDictionary dictionary
                && dictionary.TryGetValue(DestinationName, out PdfObject? nested))
                return TargetsRetainedPage(nested, depth + 1);
            return false;
        }
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

    private static PdfArray ResolveArray(
        PdfDocument document, PdfObject value, string description)
    {
        PdfObject resolved = value is PdfIndirectReference reference
            ? document.Resolve(reference)
            : value;
        return resolved as PdfArray
            ?? throw new InvalidOperationException($"{description} is not an array.");
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
        PdfDocument source, PdfPageTreeEntry page,
        bool allowFormWidgets, bool allowTaggedPage)
    {
        if (!allowTaggedPage && page.Dictionary.ContainsKey(StructParentsName))
            throw new NotSupportedException(
                "Tagged pages must be imported as a complete document into an empty destination.");
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
    private sealed record ImportedOutlineSegment(
        PdfDocument Document,
        PdfDictionary Root,
        PdfObjectGraphImporter Importer,
        PdfIndirectReference[] TopLevel,
        Dictionary<string, PdfIndirectReference> Mapped,
        long Count);

    private sealed class PageState
    {
        internal PageState(PdfPageTreeEntry entry) => Entry = entry;
        internal PageState(PdfArray mediaBox) => MediaBox = mediaBox;
        internal PageState(
            PdfDocument importedDocument, PdfPageTree importedTree,
            PdfPageTreeEntry importedEntry, bool wholeDocument, int importBatchId)
        {
            ImportedDocument = importedDocument;
            ImportedTree = importedTree;
            ImportedEntry = importedEntry;
            ImportedWholeDocument = wholeDocument;
            ImportBatchId = importBatchId;
        }
        internal PdfPageTreeEntry? Entry { get; }
        internal PdfDocument? ImportedDocument { get; }
        internal PdfPageTree? ImportedTree { get; }
        internal PdfPageTreeEntry? ImportedEntry { get; }
        internal bool ImportedWholeDocument { get; }
        internal int ImportBatchId { get; }
        internal int? Rotation { get; set; }
        internal PdfArray? MediaBox { get; set; }
        internal PdfArray? CropBox { get; set; }
    }
}
