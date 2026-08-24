using System.Text;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Filters;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Security;
using KillerPdf.Engine.Signing;
using KillerPdf.Engine.Syntax;
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
    private static readonly PdfName ParentTreeName = Name("ParentTree");
    private static readonly PdfName ParentTreeNextKeyName = Name("ParentTreeNextKey");
    private static readonly PdfName StructureParentName = Name("StructParent");
    private static readonly PdfName NamespacesName = Name("Namespaces");
    private static readonly PdfName RoleMapName = Name("RoleMap");
    private static readonly PdfName ClassMapName = Name("ClassMap");
    private static readonly PdfName StructureTypeName = Name("S");
    private static readonly PdfName StructureClassName = Name("C");
    private static readonly PdfName StructureElementParentName = Name("P");
    private static readonly PdfName IdTreeName = Name("IDTree");
    private static readonly PdfName StructureIdName = Name("ID");
    private static readonly PdfName StructureAssociatedFilesName = Name("AF");
    private static readonly PdfName PronunciationLexiconName = Name("PronunciationLexicon");
    private static readonly PdfName StructureKidsName = Name("K");
    private static readonly PdfName PageName = Name("Pg");
    private static readonly PdfName TypeName = Name("Type");
    private static readonly PdfName StructureElementName = Name("StructElem");
    private static readonly PdfName MarkedContentReferenceName = Name("MCR");
    private static readonly PdfName ObjectReferenceName = Name("OBJR");
    private static readonly PdfName MarkInfoName = Name("MarkInfo");
    private static readonly PdfName MetadataName = Name("Metadata");
    private static readonly PdfName LanguageName = Name("Lang");
    private static readonly PdfName ViewerPreferencesName = Name("ViewerPreferences");
    private static readonly PdfName OptionalContentPropertiesName = Name("OCProperties");
    private static readonly PdfName OptionalContentName = Name("OC");
    private static readonly PdfName OptionalContentGroupName = Name("OCG");
    private static readonly PdfName OptionalContentMembershipName = Name("OCMD");
    private static readonly PdfName OutputIntentsName = Name("OutputIntents");
    private static readonly PdfName ExtensionsName = Name("Extensions");
    private static readonly PdfName PermissionsName = Name("Perms");
    private static readonly PdfName NeedsRenderingName = Name("NeedsRendering");
    private static readonly PdfName AcroFormName = Name("AcroForm");
    private static readonly PdfName FieldsName = Name("Fields");
    private static readonly PdfName DefaultResourcesName = Name("DR");
    private static readonly PdfName DefaultAppearanceName = Name("DA");
    private static readonly PdfName NeedAppearancesName = Name("NeedAppearances");
    private static readonly PdfName SignatureFlagsName = Name("SigFlags");
    private static readonly PdfName CalculationOrderName = Name("CO");
    private static readonly PdfName QuaddingName = Name("Q");
    private static readonly PdfName XfaName = Name("XFA");
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
        EnforceSourceCopyPermission(source);
        PdfPageTree sourceTree = PdfPageTree.Read(source);
        if (sourcePageIndex < 0 || sourcePageIndex >= sourceTree.Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(sourcePageIndex));
        PdfPageTreeEntry sourcePage = sourceTree.Pages[sourcePageIndex];
        ValidateImportablePage(source, sourcePage,
            allowFormWidgets: sourceTree.Catalog.ContainsKey(AcroFormName),
            allowTaggedPage: sourceTree.Catalog.ContainsKey(StructTreeRootName));
        int batchId = _nextImportBatchId++;
        _pages.Insert(pageIndex,
            new PageState(source, sourceTree, sourcePage, wholeDocument: false, batchId));
        _orderChanged = true;
        return this;
    }

    /// <summary>Copies a page from another document to the end of the current page order.</summary>
    public PdfIncrementalPageEditor AddImportedPage(PdfDocument source, int sourcePageIndex) =>
        InsertImportedPage(_pages.Count, source, sourcePageIndex);

    /// <summary>
    /// Copies a selected, ordered set of pages from one document. References among selected
    /// pages are remapped as one import batch; dependencies on omitted pages remain unsupported.
    /// </summary>
    public PdfIncrementalPageEditor InsertImportedPages(
        int pageIndex, PdfDocument source, IReadOnlyList<int> sourcePageIndices)
    {
        ValidateInsertionIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourcePageIndices);
        EnforceSourceCopyPermission(source);
        PdfPageTree sourceTree = PdfPageTree.Read(source);
        if (sourcePageIndices.Count > sourceTree.Pages.Count)
            throw new ArgumentException(
                "A selected page set cannot contain more entries than the source document.",
                nameof(sourcePageIndices));
        var seen = new HashSet<int>();
        var selected = new List<PdfPageTreeEntry>(sourcePageIndices.Count);
        foreach (int sourcePageIndex in sourcePageIndices)
        {
            if (sourcePageIndex < 0 || sourcePageIndex >= sourceTree.Pages.Count)
                throw new ArgumentOutOfRangeException(nameof(sourcePageIndices),
                    "A selected source page index is outside the document.");
            if (!seen.Add(sourcePageIndex))
                throw new ArgumentException(
                    "A selected source page index cannot appear more than once.",
                    nameof(sourcePageIndices));
            PdfPageTreeEntry sourcePage = sourceTree.Pages[sourcePageIndex];
            ValidateImportablePage(source, sourcePage,
                allowFormWidgets: sourceTree.Catalog.ContainsKey(AcroFormName),
                allowTaggedPage: sourceTree.Catalog.ContainsKey(StructTreeRootName));
            selected.Add(sourcePage);
        }
        if (selected.Count == 0) return this;
        int batchId = _nextImportBatchId++;
        _pages.InsertRange(pageIndex, selected.Select(page =>
            new PageState(source, sourceTree, page, wholeDocument: false, batchId)));
        _orderChanged = true;
        return this;
    }

    /// <summary>Copies a selected, ordered set of pages to the end of the document.</summary>
    public PdfIncrementalPageEditor AddImportedPages(
        PdfDocument source, IReadOnlyList<int> sourcePageIndices) =>
        InsertImportedPages(_pages.Count, source, sourcePageIndices);

    /// <summary>Copies every page from another document into the current page order.</summary>
    public PdfIncrementalPageEditor InsertImportedDocument(int pageIndex, PdfDocument source)
    {
        ValidateInsertionIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(source);
        EnforceSourceCopyPermission(source);
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

    public byte[] Build(PdfIncrementalUpdateWriteOptions? options = null)
    {
        if (!_orderChanged && !_rotationChanged && !_pageBoxesChanged)
            throw new InvalidOperationException("The incremental page update is empty.");
        EnforcePasswordPermissions();
        if (PdfSignatureReader.ReadCertificationPermission(_document).HasValue)
            throw new InvalidOperationException(
                "The document certification signature prohibits page-tree changes.");
        var update = new PdfIncrementalUpdateBuilder(_document);
        if (_orderChanged)
            BuildReorderedTree(update);
        else
            BuildPageChanges(update);
        return update.Build(options);
    }

    private void EnforcePasswordPermissions()
    {
        if (_document.PasswordAuthenticationRole != PdfPasswordAuthenticationRole.User)
            return;
        PdfDocumentPermissions permissions = _document.DeclaredPermissions
            ?? throw new InvalidOperationException(
                "The authenticated PDF has no declared permission state.");
        if ((_orderChanged || _rotationChanged) && !permissions.AllowDocumentAssembly)
            throw new InvalidOperationException(
                "The PDF user password does not permit document assembly or page rotation.");
        if (_pageBoxesChanged && !permissions.AllowDocumentModification)
            throw new InvalidOperationException(
                "The PDF user password does not permit page-box modification.");
    }

    private static void EnforceSourceCopyPermission(PdfDocument source)
    {
        if (source.PasswordAuthenticationRole == PdfPasswordAuthenticationRole.User
            && (source.DeclaredPermissions is not PdfDocumentPermissions permissions
                || !permissions.AllowContentCopying))
            throw new InvalidOperationException(
                "The source PDF user password does not permit copying page content.");
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
                first.ImportedTree!.Pages.Select(page => page.Reference));
            importer.SeedReference(
                first.ImportedTree.CatalogReference, _tree.CatalogReference);
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
        StructureRewriteState? structureRewrite = RewriteExistingStructureTree(
            update, catalogReplacements);
        AddImportedDocumentProperties(update, importedGroups, importers, catalogReplacements);
        AddImportedStructureTree(
            update, importedGroups, importers, catalogReplacements, structureRewrite);
        AddImportedCatalogExtensions(importedGroups, importers, catalogReplacements);
        AddImportedTaggedConformanceProperties(
            importedGroups, importers, catalogReplacements);
        AddImportedOptionalContent(importedGroups, importers, catalogReplacements);
        AddImportedAcroForm(importedGroups, importers, catalogReplacements);
        AddImportedNamedDestinations(importedGroups, importers, catalogReplacements);
        AddImportedEmbeddedFiles(importedGroups, importers, catalogReplacements);
        AddImportedNameTreeCategories(importedGroups, importers, catalogReplacements);
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
        PageState[][] sourceFormGroups = importedGroups.Where(group =>
            group[0].ImportedTree!.Catalog.ContainsKey(AcroFormName)).ToArray();
        var formGroupList = new List<PageState[]>();
        var pruningPlans = new Dictionary<PageState[], FormPruningPlan>();
        foreach (PageState[] group in sourceFormGroups)
        {
            PdfPageTree tree = group[0].ImportedTree!;
            if (IsCompleteImport(group, tree))
            {
                formGroupList.Add(group);
                continue;
            }
            PdfDocument source = group[0].ImportedDocument!;
            IReadOnlySet<(int ObjectNumber, int Generation)> selectedWidgets =
                SelectedWidgetReferences(source, group);
            if (selectedWidgets.Count == 0) continue;
            PdfDictionary form = ResolveDictionary(
                source, tree.Catalog[AcroFormName], "The source /AcroForm");
            FormPruningPlan plan = BuildFormPruningPlan(
                source, form, selectedWidgets);
            pruningPlans.Add(group, plan);
            importers[group[0]].AddSourceObjectOverrides(plan.RewrittenObjects);
            formGroupList.Add(group);
        }
        PageState[][] formGroups = formGroupList.ToArray();
        if (formGroups.Length == 0) return;
        if (!_tree.Catalog.ContainsKey(AcroFormName) && formGroups.Length == 1
            && !pruningPlans.ContainsKey(formGroups[0]))
        {
            PageState first = formGroups[0][0];
            catalogReplacements[AcroFormName] = importers[first].Import(
                first.ImportedTree!.Catalog[AcroFormName]);
            if (first.ImportedTree.Catalog.TryGetValue(
                    NeedsRenderingName, out PdfObject? needsRendering))
                catalogReplacements[NeedsRenderingName] = importers[first].Import(needsRendering);
            return;
        }

        PdfDictionary? targetForm = _tree.Catalog.TryGetValue(
            AcroFormName, out PdfObject? targetFormValue)
            ? ResolveDictionary(_document, targetFormValue, "The destination /AcroForm")
            : null;
        var formsToMerge = new List<PdfDictionary>();
        if (targetForm is not null) formsToMerge.Add(targetForm);
        formsToMerge.AddRange(formGroups.Select(group =>
            pruningPlans.TryGetValue(group, out FormPruningPlan? plan)
                ? plan.Form
                : ResolveDictionary(group[0].ImportedDocument!,
                    group[0].ImportedTree!.Catalog[AcroFormName],
                    "The source /AcroForm")));
        PdfName[] supportedFormKeys =
            [FieldsName, DefaultResourcesName, DefaultAppearanceName, NeedAppearancesName,
                SignatureFlagsName, CalculationOrderName, QuaddingName];
        if (formsToMerge.Any(form => form.ContainsKey(XfaName)))
            throw new NotSupportedException(
                "Merging AcroForms containing XFA packets is not supported because their templates and datasets cannot be combined safely.");
        var extensionEntries = new Dictionary<PdfName, PdfObject>();
        if (targetForm is not null)
            foreach (var entry in targetForm.Where(entry =>
                         !supportedFormKeys.Contains(entry.Key)))
                extensionEntries.Add(entry.Key, entry.Value);
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
        var procedureSets = new List<PdfObject>();
        var usedProcedureSets = new HashSet<PdfName>();
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
            bool isPartial = pruningPlans.TryGetValue(
                group, out FormPruningPlan? pruningPlan);
            PdfDictionary form = isPartial ? pruningPlan!.Form : ResolveDictionary(source,
                group[0].ImportedTree!.Catalog[AcroFormName], "The source /AcroForm");
            AddFieldNames(source, form, fieldNames,
                isPartial ? pruningPlan!.RewrittenObjects : null);
            var renames = new Dictionary<PdfName, PdfName>();
            PdfObjectGraphImporter importer = importers[group[0]];
            PrepareResourceNames(source, form, renames);
            PdfString? defaultAppearance = form.TryGetValue(
                DefaultAppearanceName, out PdfObject? da) ? da as PdfString : null;
            PdfInteger? defaultQuadding = form.TryGetValue(
                QuaddingName, out PdfObject? q) ? q as PdfInteger : null;
            importer.AddDictionaryTransform((_, dictionary) =>
                TransformFormDictionary(dictionary, renames, defaultAppearance, defaultQuadding));
            if (!isPartial)
                foreach (var entry in form.Where(entry =>
                             !supportedFormKeys.Contains(entry.Key)))
                {
                    if (extensionEntries.ContainsKey(entry.Key))
                        throw new NotSupportedException(
                            $"Multiple AcroForms define the catalog-level /{entry.Key.ValueAsLatin1()} extension and cannot be merged without extension-specific semantics.");
                    extensionEntries.Add(entry.Key, importer.Import(entry.Value));
                }
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
            .Where(entry => supportedFormKeys.Contains(entry.Key)
                && !entry.Key.Equals(FieldsName)
                && !entry.Key.Equals(DefaultResourcesName)
                && !entry.Key.Equals(NeedAppearancesName)
                && !entry.Key.Equals(SignatureFlagsName)
                && !entry.Key.Equals(CalculationOrderName))
            .Select(entry => new KeyValuePair<PdfName, PdfObject>(entry.Key,
                entry.Key.Equals(DefaultAppearanceName) && entry.Value is PdfString appearance
                    ? RewriteDefaultAppearance(appearance, baseRenames)
                    : baseImporter?.Import(entry.Value) ?? entry.Value))
            .ToList();
        formEntries.AddRange(extensionEntries.Select(entry =>
            new KeyValuePair<PdfName, PdfObject>(entry.Key, entry.Value)));
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
                        category.Key, new PdfDictionary(category.Value)))
                    .Concat(procedureSets.Count == 0 ? [] :
                    [new KeyValuePair<PdfName, PdfObject>(
                        Name("ProcSet"), new PdfArray(procedureSets))]))));
        else if (procedureSets.Count > 0)
            formEntries.Add(new KeyValuePair<PdfName, PdfObject>(DefaultResourcesName,
                new PdfDictionary([new(Name("ProcSet"), new PdfArray(procedureSets))])));
        catalogReplacements[AcroFormName] = new PdfDictionary(formEntries);

        void PrepareResourceNames(PdfDocument document, PdfDictionary form,
            IDictionary<PdfName, PdfName> renames)
        {
            if (!form.TryGetValue(DefaultResourcesName, out PdfObject? value)) return;
            PdfDictionary resources = ResolveDictionary(document, value, "An /AcroForm /DR value");
            foreach (PdfName resourceName in resources.Where(category =>
                         !category.Key.Equals(Name("ProcSet"))).SelectMany(category =>
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
                PdfObject categoryValue = category.Value is PdfIndirectReference reference
                    ? document.Resolve(reference) : category.Value;
                if (category.Key.Equals(Name("ProcSet")))
                {
                    PdfArray array = categoryValue as PdfArray
                        ?? throw new InvalidOperationException(
                            "An /AcroForm /DR /ProcSet value is not an array.");
                    foreach (PdfObject item in array)
                    {
                        PdfName name = item as PdfName
                            ?? throw new InvalidOperationException(
                                "An /AcroForm /DR /ProcSet entry is not a name.");
                        if (usedProcedureSets.Add(name)) procedureSets.Add(name);
                    }
                    continue;
                }
                PdfDictionary dictionary = categoryValue as PdfDictionary
                    ?? throw new InvalidOperationException(
                        "An /AcroForm resource category is not a dictionary.");
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
            if (!IsCompleteImport(group, sourceTree))
            {
                DestinationReferences references = ReferencedNamedDestinations(
                    group[0].ImportedDocument!, group);
                var byName = sourceEntries.ToDictionary(entry =>
                    Convert.ToBase64String(entry.Key.Bytes.Span), StringComparer.Ordinal);
                foreach (string reference in references.StringNames)
                    if (!byName.TryGetValue(reference, out PdfNameTreeEntry? destination)
                        || !DestinationStaysWithinImportedPages(
                            group[0].ImportedDocument!, destination.Value, group))
                        throw new NotSupportedException(
                            "A selected source page uses a named destination outside the selected page set.");
                sourceEntries = sourceEntries.Where(entry => DestinationStaysWithinImportedPages(
                    group[0].ImportedDocument!, entry.Value, group)).ToArray();
            }
            if (sourceEntries.Count == 0) continue;
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
            string text = PdfUnicodeEncoding.DecodeBigEndian(
                value.Bytes.Span[2..], "A named destination") + addition;
            byte[] encoded = PdfUnicodeEncoding.EncodeBigEndian(text);
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

    private static IReadOnlySet<(int ObjectNumber, int Generation)> SelectedWidgetReferences(
        PdfDocument document, IEnumerable<PageState> pages)
    {
        var result = new HashSet<(int ObjectNumber, int Generation)>();
        foreach (PageState page in pages)
        {
            if (!page.ImportedEntry!.Dictionary.TryGetValue(
                    AnnotsName, out PdfObject? annotationsValue)) continue;
            foreach (PdfObject item in ResolveArray(
                         document, annotationsValue, "A selected page /Annots value"))
            {
                PdfDictionary annotation = item is PdfIndirectReference reference
                    ? ResolveDictionary(document, reference, "A selected page annotation")
                    : item as PdfDictionary
                        ?? throw new InvalidOperationException(
                            "A selected page annotation is not a dictionary.");
                if (!annotation.TryGetValue(SubtypeName, out PdfObject? subtype)
                    || subtype is not PdfName name || !name.Equals(WidgetName)) continue;
                if (item is not PdfIndirectReference widgetReference)
                    throw new NotSupportedException(
                        "A direct form-widget annotation cannot be matched safely to a partial AcroForm field tree.");
                result.Add((widgetReference.ObjectNumber, widgetReference.Generation));
            }
        }
        return result;
    }

    private static FormPruningPlan BuildFormPruningPlan(
        PdfDocument document, PdfDictionary form,
        IReadOnlySet<(int ObjectNumber, int Generation)> selectedWidgets)
    {
        if (form.ContainsKey(XfaName))
            throw new NotSupportedException(
                "A selected widget from an XFA form requires complete-document import.");
        var active = new HashSet<(int ObjectNumber, int Generation)>();
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        var retained = new HashSet<(int ObjectNumber, int Generation)>();
        var overrides = new Dictionary<(int ObjectNumber, int Generation), PdfDictionary>();
        var fields = new List<PdfObject>();
        foreach (PdfObject field in FormFields(document, form))
        {
            PdfObject? pruned = Prune(field, 0);
            if (pruned is not null) fields.Add(pruned);
        }
        if (!selectedWidgets.IsSubsetOf(retained))
            throw new NotSupportedException(
                "A selected widget is not reachable from the source AcroForm /Fields tree.");
        var replacements = new Dictionary<PdfName, PdfObject>
        {
            [FieldsName] = new PdfArray(fields)
        };
        if (form.TryGetValue(CalculationOrderName, out PdfObject? calculationOrder))
        {
            PdfObject[] retainedOrder = ResolveArray(
                    document, calculationOrder, "The source /AcroForm /CO value")
                .Where(item => item is PdfIndirectReference reference
                    && retained.Contains((reference.ObjectNumber, reference.Generation)))
                .ToArray();
            if (retainedOrder.Length > 0)
                replacements[CalculationOrderName] = new PdfArray(retainedOrder);
        }
        PdfDictionary effectiveForm = ReplaceMany(form, replacements,
            replacements.ContainsKey(CalculationOrderName)
                ? null : [CalculationOrderName]);
        return new FormPruningPlan(effectiveForm, overrides, retained);

        PdfObject? Prune(PdfObject value, int depth)
        {
            if (depth > 256)
                throw new InvalidOperationException("The AcroForm field tree is too deeply nested.");
            PdfIndirectReference? reference = value as PdfIndirectReference;
            PdfObject resolved = reference is null ? value : document.Resolve(reference);
            PdfDictionary field = resolved as PdfDictionary
                ?? throw new InvalidOperationException("An AcroForm field is not a dictionary.");
            if (reference is not null)
            {
                var key = (reference.ObjectNumber, reference.Generation);
                if (!active.Add(key))
                    throw new InvalidOperationException("The AcroForm field tree contains a cycle.");
                if (!visited.Add(key))
                {
                    active.Remove(key);
                    throw new InvalidOperationException(
                        "The AcroForm field tree references the same field more than once.");
                }
            }
            try
            {
                var keptKids = new List<PdfObject>();
                bool hadKids = field.TryGetValue(KidsName, out PdfObject? kidsValue);
                if (hadKids)
                    foreach (PdfObject kid in ResolveArray(
                                 document, kidsValue!, "An AcroForm field /Kids value"))
                    {
                        PdfObject? kept = Prune(kid, depth + 1);
                        if (kept is not null) keptKids.Add(kept);
                    }
                bool selected = reference is not null
                    && selectedWidgets.Contains(
                        (reference.ObjectNumber, reference.Generation));
                if (!selected && keptKids.Count == 0) return null;
                if (reference is not null)
                    retained.Add((reference.ObjectNumber, reference.Generation));
                PdfDictionary rewritten = field;
                if (hadKids)
                    rewritten = keptKids.Count == 0
                        ? ReplaceMany(field, new Dictionary<PdfName, PdfObject>(), [KidsName])
                        : ReplaceMany(field, new Dictionary<PdfName, PdfObject>
                        {
                            [KidsName] = new PdfArray(keptKids)
                        });
                if (reference is null) return rewritten;
                if (!ReferenceEquals(rewritten, field))
                    overrides[(reference.ObjectNumber, reference.Generation)] = rewritten;
                return reference;
            }
            finally
            {
                if (reference is not null)
                    active.Remove((reference.ObjectNumber, reference.Generation));
            }
        }
    }

    private static void AddFieldNames(
        PdfDocument document, PdfDictionary form, ISet<string> names,
        IReadOnlyDictionary<(int ObjectNumber, int Generation), PdfDictionary>? overrides = null)
    {
        var active = new HashSet<(int ObjectNumber, int Generation)>();
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        foreach (PdfObject field in FormFields(document, form)) Visit(field, 0, []);

        void Visit(PdfObject value, int depth, IReadOnlyList<string> parentPath)
        {
            if (depth > 256)
                throw new InvalidOperationException("The AcroForm field tree is too deeply nested.");
            (int ObjectNumber, int Generation)? referenceKey = null;
            if (value is PdfIndirectReference reference)
            {
                var key = (reference.ObjectNumber, reference.Generation);
                referenceKey = key;
                if (!active.Add(key))
                    throw new InvalidOperationException("The AcroForm field tree contains a cycle.");
                if (!visited.Add(key))
                {
                    active.Remove(key);
                    throw new InvalidOperationException(
                        "The AcroForm field tree references the same field more than once.");
                }
                value = overrides is not null
                    && overrides.TryGetValue(
                        (reference.ObjectNumber, reference.Generation),
                        out PdfDictionary? replacement)
                        ? replacement : document.Resolve(reference);
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
                if (referenceKey.HasValue) active.Remove(referenceKey.Value);
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
            if (!IsCompleteImport(group, sourceTree))
            {
                DestinationReferences references = ReferencedNamedDestinations(
                    group[0].ImportedDocument!, group);
                foreach (PdfName reference in references.LegacyNames)
                    if (!sourceDestinations.TryGetValue(reference, out PdfObject? destination)
                        || !DestinationStaysWithinImportedPages(
                            group[0].ImportedDocument!, destination, group))
                        throw new NotSupportedException(
                            "A selected source page uses a legacy named destination outside the selected page set.");
                sourceDestinations = new PdfDictionary(sourceDestinations.Where(entry =>
                    DestinationStaysWithinImportedPages(
                        group[0].ImportedDocument!, entry.Value, group)));
            }
            if (sourceDestinations.Count == 0) continue;
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
        PageState[][] completeGroups = groups.Where(group =>
            IsCompleteImport(group, group[0].ImportedTree!)).ToArray();
        bool hasImportedEmbeddedFiles = completeGroups.Any(group => HasNameTreeCategory(
            group[0].ImportedDocument!, group[0].ImportedTree!.Catalog, EmbeddedFilesName));
        bool hasImportedAssociatedFiles = completeGroups.Any(group =>
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
            foreach (PageState[] group in completeGroups)
            {
                PdfDocument source = group[0].ImportedDocument!;
                PdfPageTree sourceTree = group[0].ImportedTree!;
                if (!TryGetNameTreeCategory(
                    source, sourceTree.Catalog, EmbeddedFilesName, out PdfObject? sourceFiles))
                    continue;
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
                    PdfString key = entry.Key;
                    if (importer is null
                        && !keys.Add(Convert.ToBase64String(key.Bytes.Span)))
                        throw new NotSupportedException(
                            "The destination embedded-files name tree contains duplicate names.");
                    if (importer is not null)
                    {
                        int suffix = 2;
                        while (!keys.Add(Convert.ToBase64String(key.Bytes.Span)))
                            key = AppendDestinationSuffix(entry.Key, suffix++);
                    }
                    files.Add(new PdfNameTreeEntry(
                        key, importer?.Import(entry.Value) ?? entry.Value));
                }
            }
        }

        if (hasImportedAssociatedFiles)
        {
            var associated = new List<PdfObject>();
            if (_tree.Catalog.TryGetValue(AssociatedFilesName, out PdfObject? targetAssociated))
                associated.AddRange(ResolveArray(
                    _document, targetAssociated, "The destination catalog /AF value"));
            foreach (PageState[] group in completeGroups)
            {
                PdfPageTree sourceTree = group[0].ImportedTree!;
                if (!sourceTree.Catalog.TryGetValue(
                    AssociatedFilesName, out PdfObject? sourceAssociated)) continue;
                associated.AddRange(ResolveArray(group[0].ImportedDocument!, sourceAssociated,
                    "The source catalog /AF value").Select(importers[group[0]].Import));
            }
            catalogReplacements[AssociatedFilesName] = new PdfArray(associated);
        }
    }

    private void AddImportedNameTreeCategories(
        IEnumerable<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        PageState[][] groups = importedGroups.Where(group =>
            IsCompleteImport(group, group[0].ImportedTree!)).ToArray();
        var sourceCategories = new HashSet<PdfName>();
        foreach (PageState[] group in groups)
        {
            PdfDocument source = group[0].ImportedDocument!;
            PdfPageTree tree = group[0].ImportedTree!;
            if (!tree.Catalog.TryGetValue(NamesName, out PdfObject? namesValue)) continue;
            PdfDictionary names = ResolveDictionary(
                source, namesValue, "A source catalog /Names value");
            foreach (PdfName category in names.Keys.Where(name =>
                         !name.Equals(DestsName) && !name.Equals(EmbeddedFilesName)))
                sourceCategories.Add(category);
        }
        if (sourceCategories.Count == 0) return;

        PdfDictionary currentNames = CurrentNamesDictionary(catalogReplacements);
        var mergedCategories = currentNames.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (PdfName category in sourceCategories)
        {
            var entries = new List<PdfNameTreeEntry>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (currentNames.TryGetValue(category, out PdfObject? targetValue))
                Add(PdfNameTree.Read(_document, targetValue), null);
            foreach (PageState[] group in groups)
            {
                PdfDocument source = group[0].ImportedDocument!;
                PdfPageTree tree = group[0].ImportedTree!;
                if (!tree.Catalog.TryGetValue(NamesName, out PdfObject? namesValue)) continue;
                PdfDictionary names = ResolveDictionary(
                    source, namesValue, "A source catalog /Names value");
                if (!names.TryGetValue(category, out PdfObject? sourceValue)) continue;
                Add(PdfNameTree.Read(source, sourceValue), importers[group[0]]);
            }
            entries.Sort((left, right) =>
                left.Key.Bytes.Span.SequenceCompareTo(right.Key.Bytes.Span));
            var values = new List<PdfObject>(entries.Count * 2);
            foreach (PdfNameTreeEntry entry in entries)
            {
                values.Add(entry.Key);
                values.Add(entry.Value);
            }
            mergedCategories[category] = Dictionary(("Names", new PdfArray(values)));

            void Add(IEnumerable<PdfNameTreeEntry> additions, PdfObjectGraphImporter? importer)
            {
                foreach (PdfNameTreeEntry entry in additions)
                {
                    if (!keys.Add(Convert.ToBase64String(entry.Key.Bytes.Span)))
                        throw new NotSupportedException(
                            $"The /Names /{category.ValueAsLatin1()} name tree contains a duplicate key across merged documents.");
                    entries.Add(new PdfNameTreeEntry(
                        entry.Key, importer?.Import(entry.Value) ?? entry.Value));
                }
            }
        }
        catalogReplacements[NamesName] = new PdfDictionary(mergedCategories);
    }

    private void AddImportedOutlines(
        PdfIncrementalUpdateBuilder update,
        IEnumerable<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        PageState[][] outlineGroups = importedGroups.Where(group =>
            IsCompleteImport(group, group[0].ImportedTree!) &&
            group[0].ImportedTree!.Catalog.ContainsKey(OutlinesName)).ToArray();
        if (outlineGroups.Length == 0) return;

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

    private static bool IsCompleteImport(PageState[] group, PdfPageTree sourceTree) =>
        group.Length == sourceTree.Pages.Count && group.All(page => page.ImportedWholeDocument);

    private void AddImportedStructureTree(
        PdfIncrementalUpdateBuilder update,
        IReadOnlyList<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements,
        StructureRewriteState? rewriteState)
    {
        PageState[][] sourceTaggedGroups = importedGroups.Where(group =>
        {
            PdfPageTree sourceTree = group[0].ImportedTree!;
            return sourceTree.Catalog.ContainsKey(StructTreeRootName)
                || group.Any(page => page.ImportedEntry!.Dictionary.ContainsKey(StructParentsName));
        }).ToArray();
        PageState[][] taggedGroups = sourceTaggedGroups.Where(group =>
            IsCompleteImport(group, group[0].ImportedTree!)
            || group.Any(page => page.ImportedEntry!.Dictionary.ContainsKey(StructParentsName)))
            .ToArray();
        if (taggedGroups.Length == 0) return;
        var pruningPlans = new Dictionary<PageState[], StructurePruningPlan>();
        foreach (PageState[] taggedGroup in taggedGroups)
        {
            PdfPageTree taggedTree = taggedGroup[0].ImportedTree!;
            if (!taggedTree.Catalog.ContainsKey(StructTreeRootName))
                throw new InvalidOperationException(
                    "An imported page has /StructParents but its source catalog has no /StructTreeRoot.");
            if (IsCompleteImport(taggedGroup, taggedTree)) continue;
            var retained = taggedGroup.Select(page =>
                (page.ImportedEntry!.Reference.ObjectNumber,
                    page.ImportedEntry.Reference.Generation)).ToHashSet();
            var removed = taggedTree.Pages.Select(page =>
                    (page.Reference.ObjectNumber, page.Reference.Generation))
                .Where(reference => !retained.Contains(reference)).ToHashSet();
            StructurePruningPlan plan = BuildStructurePruningPlan(
                taggedGroup[0].ImportedDocument!,
                taggedTree.Catalog[StructTreeRootName], taggedTree.Pages, removed);
            pruningPlans.Add(taggedGroup, plan);
            importers[taggedGroup[0]].AddSourceObjectOverrides(plan.RewrittenObjects);
        }

        bool destinationIsEmpty = _tree.Pages.Count == 0;
        if (!_tree.Catalog.ContainsKey(StructTreeRootName))
        {
            PageState[] group = taggedGroups[0];
            PdfPageTree tree = group[0].ImportedTree!;
            bool isOnlyPageSet = taggedGroups.Length == 1 && destinationIsEmpty
                && _pages.Count == group.Length && _pages.All(group.Contains)
                && IsCompleteImport(group, tree);
            if (isOnlyPageSet)
            {
                PdfObjectGraphImporter importer = importers[group[0]];
                foreach (PdfName name in new[] { StructTreeRootName, MarkInfoName })
                    if (tree.Catalog.TryGetValue(name, out PdfObject? value))
                        catalogReplacements[name] = importer.Import(value);
                return;
            }
            bool everyResultPageIsTagged = _pages.All(page =>
                taggedGroups.Any(group => group.Contains(page)));
            if (!destinationIsEmpty || !everyResultPageIsTagged)
                throw new NotSupportedException(
                    "Tagged content cannot be introduced alongside existing or newly added untagged pages.");
        }
        bool targetHadStructure = _tree.Catalog.TryGetValue(
            StructTreeRootName, out PdfObject? targetRootValue);
        PdfDictionary targetRoot = targetHadStructure
            ? rewriteState?.Root ?? ResolveDictionary(
                _document, targetRootValue!, "The destination /StructTreeRoot")
            : Dictionary(("Type", Name("StructTreeRoot")));
        PdfIndirectReference targetRootReference;
        bool targetRootIsNew = false;
        if (targetRootValue is PdfIndirectReference existingReference)
            targetRootReference = existingReference;
        else if (!targetHadStructure)
        {
            targetRootReference = update.ReserveObject();
            targetRootIsNew = true;
            catalogReplacements[StructTreeRootName] = targetRootReference;
        }
        else
        {
            targetRootReference = FindStructureRootParentReference(_document, targetRoot)
                ?? update.ReserveObject();
            targetRootIsNew = _document.Resolve(targetRootReference) is PdfNull;
            catalogReplacements[StructTreeRootName] = targetRootReference;
        }

        var parentEntries = rewriteState is not null
            ? rewriteState.ParentEntries.ToList()
            : targetRoot.TryGetValue(ParentTreeName, out PdfObject? targetParentTree)
                ? PdfNumberTree.Read(_document, targetParentTree).ToList()
                : [];
        if (parentEntries.Any(entry => entry.Key < 0))
            throw new InvalidOperationException(
                "The destination structure-tree ParentTree contains a negative key.");
        long nextKey = parentEntries.Count == 0
            ? 0 : checked(parentEntries.Max(entry => entry.Key) + 1);
        if (targetRoot.TryGetValue(ParentTreeNextKeyName, out PdfObject? nextValue))
        {
            long declared = (nextValue as PdfInteger)?.Value
                ?? throw new InvalidOperationException("The /ParentTreeNextKey value is not an integer.");
            if (declared < 0)
                throw new InvalidOperationException("The /ParentTreeNextKey value cannot be negative.");
            nextKey = Math.Max(nextKey, declared);
        }
        var structureKids = new List<PdfObject>();
        if (targetRoot.TryGetValue(StructureKidsName, out PdfObject? targetKids))
            structureKids.AddRange(StructureKids(
                _document, targetKids, "The destination structure-root kids"));
        PdfIndirectReference? targetDocumentReference = null;
        PdfDictionary? targetDocument = null;
        bool targetDocumentIsNew = false;
        var targetDocumentKids = new List<PdfObject>();
        if (structureKids.Count == 1)
        {
            PdfDictionary candidate = ResolveEffectiveDictionary(
                structureKids[0], "The destination top-level structure element");
            if (IsDocumentElement(candidate))
            {
                if (structureKids[0] is PdfIndirectReference documentReference)
                    targetDocumentReference = documentReference;
                else
                {
                    targetDocumentReference = FindStructureElementParentReference(
                        _document, candidate);
                    if (targetDocumentReference is null)
                    {
                        if (HasStructureElementParentReference(_document, candidate))
                            throw new NotSupportedException(
                                "A direct destination Document structure element has ambiguous child parent references.");
                        targetDocumentReference = update.ReserveObject();
                        targetDocumentIsNew = true;
                    }
                    structureKids[0] = targetDocumentReference;
                    candidate = ReplaceMany(candidate,
                        new Dictionary<PdfName, PdfObject>
                        {
                            [StructureElementParentName] = targetRootReference
                        });
                }
                targetDocument = candidate;
                if (candidate.TryGetValue(StructureKidsName, out PdfObject? documentKids))
                    targetDocumentKids.AddRange(StructureKids(
                        _document, documentKids, "The destination Document-element kids"));
            }
        }
        var namespaces = new List<PdfObject>();
        if (targetRoot.TryGetValue(NamespacesName, out PdfObject? targetNamespaces))
            namespaces.AddRange(ResolveArray(
                _document, targetNamespaces, "The destination /StructTreeRoot /Namespaces"));
        var roleEntries = targetRoot.TryGetValue(RoleMapName, out PdfObject? targetRoleMap)
            ? ResolveDictionary(_document, targetRoleMap,
                "The destination /StructTreeRoot /RoleMap").ToList()
            : [];
        var classEntries = targetRoot.TryGetValue(ClassMapName, out PdfObject? targetClassMap)
            ? ResolveDictionary(_document, targetClassMap,
                "The destination /StructTreeRoot /ClassMap").ToList()
            : [];
        var usedRoleNames = roleEntries.Select(entry => entry.Key).ToHashSet();
        var usedClassNames = classEntries.Select(entry => entry.Key).ToHashSet();
        var idEntries = targetRoot.TryGetValue(IdTreeName, out PdfObject? targetIdTree)
            ? PdfNameTree.Read(_document, targetIdTree).ToList()
            : [];
        var usedIds = idEntries.Select(entry => Convert.ToBase64String(entry.Key.Bytes.Span))
            .ToHashSet(StringComparer.Ordinal);
        var structureAssociatedFiles = ReadOptionalArray(
            _document, targetRoot, StructureAssociatedFilesName,
            "The destination /StructTreeRoot /AF");
        var pronunciationLexicons = ReadOptionalArray(
            _document, targetRoot, PronunciationLexiconName,
            "The destination /StructTreeRoot /PronunciationLexicon");
        var importedRootExtensions = new Dictionary<PdfName, PdfObject>();
        int nextRoleName = 1;
        int nextClassName = 1;
        int nextId = 1;

        foreach (PageState[] group in taggedGroups)
        {
            PdfDocument source = group[0].ImportedDocument!;
            PdfPageTree tree = group[0].ImportedTree!;
            PdfObject sourceRootValue = tree.Catalog[StructTreeRootName];
            bool isPartial = pruningPlans.TryGetValue(
                group, out StructurePruningPlan? pruningPlan);
            PdfDictionary sourceRoot = isPartial
                ? pruningPlan!.EffectiveRoot
                : ResolveDictionary(source, sourceRootValue, "The source /StructTreeRoot");
            PdfName[] supportedSourceRootKeys =
            [TypeName, StructureKidsName, ParentTreeName, ParentTreeNextKeyName,
                NamespacesName, RoleMapName, ClassMapName, IdTreeName,
                StructureAssociatedFilesName, PronunciationLexiconName];
            PdfObjectGraphImporter importer = importers[group[0]];
            if (sourceRootValue is PdfIndirectReference sourceRootReference)
                importer.SeedReference(sourceRootReference, targetRootReference);
            else
            {
                PdfIndirectReference sourceParent = FindStructureRootParentReference(
                    source, sourceRoot)
                    ?? throw new InvalidOperationException(
                        "A direct source structure-tree root has no identifiable top-level parent reference.");
                importer.SeedReference(sourceParent, targetRootReference);
            }
            PdfIndirectReference? sourceDocumentReferenceForMerge = null;
            PdfDictionary? sourceDocumentForMerge = null;
            if (targetDocumentReference is not null
                && sourceRoot.TryGetValue(StructureKidsName, out PdfObject? sourceTopLevel)
                && StructureKids(source, sourceTopLevel,
                    "A source structure-root kids value") is { Count: 1 } sourceTopLevelKids)
            {
                PdfObject sourceDocumentValue = sourceTopLevelKids[0];
                sourceDocumentReferenceForMerge = sourceDocumentValue as PdfIndirectReference;
                sourceDocumentForMerge = sourceDocumentReferenceForMerge is not null
                    && isPartial && pruningPlan!.RewrittenObjects.TryGetValue(
                        (sourceDocumentReferenceForMerge.ObjectNumber,
                            sourceDocumentReferenceForMerge.Generation),
                        out PdfDictionary? rewrittenDocument)
                            ? rewrittenDocument
                            : ResolveDictionary(source, sourceDocumentValue,
                                "The source top-level structure element");
                if (IsDocumentElement(sourceDocumentForMerge))
                {
                    sourceDocumentReferenceForMerge ??= FindStructureElementParentReference(
                        source, sourceDocumentForMerge)
                        ?? throw new NotSupportedException(
                            "A direct source Document structure element has no unambiguous indirect self-reference.");
                    importer.SeedReference(
                        sourceDocumentReferenceForMerge, targetDocumentReference);
                }
                else
                {
                    sourceDocumentReferenceForMerge = null;
                    sourceDocumentForMerge = null;
                }
            }
            foreach (PdfName extensionKey in sourceRoot.Keys
                         .Where(key => !supportedSourceRootKeys.Contains(key)))
            {
                if (isPartial)
                    throw new NotSupportedException(
                        $"Selecting pages from a tagged structure root containing /{extensionKey.ValueAsLatin1()} is not supported because its page dependencies are unknown.");
                if (targetRoot.ContainsKey(extensionKey)
                    || importedRootExtensions.ContainsKey(extensionKey))
                    throw new NotSupportedException(
                        $"Tagged structure roots contain conflicting /{extensionKey.ValueAsLatin1()} extension entries.");
                importedRootExtensions[extensionKey] = importer.Import(sourceRoot[extensionKey]);
            }

            var keyMap = new Dictionary<long, long>();
            IReadOnlyList<PdfNumberTreeEntry> sourceEntries = isPartial
                ? pruningPlan!.ParentEntries
                : sourceRoot.TryGetValue(ParentTreeName, out PdfObject? sourceParentTree)
                    ? PdfNumberTree.Read(source, sourceParentTree) : [];
            if (sourceEntries.Any(entry => entry.Key < 0))
                throw new InvalidOperationException(
                    "A source structure-tree ParentTree contains a negative key.");
            var sourceParentKeys = sourceEntries.Select(entry => entry.Key).ToHashSet();
            foreach (PageState page in group)
            {
                if (!page.ImportedEntry!.Dictionary.TryGetValue(
                        StructParentsName, out PdfObject? sourceKey)) continue;
                long key = (sourceKey as PdfInteger)?.Value
                    ?? throw new NotSupportedException(
                        "A selected tagged page has a non-integer /StructParents key.");
                if (!sourceParentKeys.Contains(key))
                    throw new NotSupportedException(
                        $"Selected tagged page structure-parent key {key} is missing from the source ParentTree.");
            }
            foreach (PdfNumberTreeEntry entry in sourceEntries.OrderBy(entry => entry.Key))
            {
                keyMap[entry.Key] = nextKey;
                nextKey = checked(nextKey + 1);
            }
            var roleRenames = new Dictionary<PdfName, PdfName>();
            var classRenames = new Dictionary<PdfName, PdfName>();
            var idRenames = new Dictionary<string, PdfString>(StringComparer.Ordinal);
            PdfDictionary? sourceRoleMap = sourceRoot.TryGetValue(
                RoleMapName, out PdfObject? sourceRoles)
                ? ResolveDictionary(source, sourceRoles, "A source /StructTreeRoot /RoleMap") : null;
            PdfDictionary? sourceClassMap = sourceRoot.TryGetValue(
                ClassMapName, out PdfObject? sourceClasses)
                ? ResolveDictionary(source, sourceClasses, "A source /StructTreeRoot /ClassMap") : null;
            PrepareNames(sourceRoleMap, usedRoleNames, roleRenames, "KPRole", ref nextRoleName);
            PrepareNames(sourceClassMap, usedClassNames, classRenames, "KPClass", ref nextClassName);
            IReadOnlyList<PdfNameTreeEntry> sourceIds = [];
            if (sourceRoot.TryGetValue(IdTreeName, out PdfObject? sourceIdTree))
            {
                try
                {
                    sourceIds = PdfNameTree.Read(source, sourceIdTree);
                }
                catch (InvalidOperationException) when (isPartial)
                {
                    sourceIds = [];
                }
            }
            if (isPartial)
                sourceIds = sourceIds.Where(entry => entry.Value is PdfIndirectReference reference
                    && pruningPlan!.RetainedStructureObjects.Contains(
                        (reference.ObjectNumber, reference.Generation)))
                    .ToArray();
            foreach (PdfNameTreeEntry entry in sourceIds)
            {
                string sourceKey = Convert.ToBase64String(entry.Key.Bytes.Span);
                PdfString destinationId = entry.Key;
                if (usedIds.Contains(sourceKey))
                {
                    byte[] suffix;
                    string candidate;
                    do
                    {
                        suffix = Encoding.ASCII.GetBytes($"-KP{nextId++}");
                        destinationId = new PdfString(
                            [.. entry.Key.Bytes.Span, .. suffix], PdfStringForm.Hexadecimal);
                        candidate = Convert.ToBase64String(destinationId.Bytes.Span);
                    }
                    while (usedIds.Contains(candidate));
                    idRenames[sourceKey] = destinationId;
                    sourceKey = candidate;
                }
                usedIds.Add(sourceKey);
            }
            importer.AddDictionaryTransform((_, dictionary) =>
                RemapStructureDictionary(
                    dictionary, keyMap, roleRenames, classRenames, idRenames));
            if (sourceRoleMap is not null)
                roleEntries.AddRange(sourceRoleMap.Select(entry =>
                    new KeyValuePair<PdfName, PdfObject>(
                        roleRenames.GetValueOrDefault(entry.Key, entry.Key),
                        importer.Import(entry.Value))));
            if (sourceClassMap is not null)
                classEntries.AddRange(sourceClassMap.Select(entry =>
                    new KeyValuePair<PdfName, PdfObject>(
                        classRenames.GetValueOrDefault(entry.Key, entry.Key),
                        importer.Import(entry.Value))));
            bool mergedIntoDocument = false;
            if (targetDocumentReference is not null
                && sourceDocumentReferenceForMerge is not null
                && sourceDocumentForMerge is not null)
            {
                if (sourceDocumentForMerge.TryGetValue(
                        StructureKidsName, out PdfObject? sourceDocumentKids))
                    targetDocumentKids.AddRange(StructureKids(
                        source, sourceDocumentKids, "A source Document-element kids value")
                        .Select(importer.Import));
                mergedIntoDocument = true;
            }
            if (!mergedIntoDocument
                && sourceRoot.TryGetValue(StructureKidsName, out PdfObject? sourceKids))
                structureKids.AddRange(StructureKids(
                    source, sourceKids, "A source structure-root kids value")
                    .Select(importer.Import));
            foreach (PdfNameTreeEntry entry in sourceIds)
            {
                string sourceKey = Convert.ToBase64String(entry.Key.Bytes.Span);
                PdfString key = idRenames.GetValueOrDefault(sourceKey, entry.Key);
                idEntries.Add(new PdfNameTreeEntry(key, importer.Import(entry.Value)));
            }
            foreach (PdfNumberTreeEntry entry in sourceEntries)
                parentEntries.Add(new PdfNumberTreeEntry(
                    keyMap[entry.Key], importer.Import(entry.Value)));
            if (sourceRoot.TryGetValue(NamespacesName, out PdfObject? sourceNamespaces))
                namespaces.AddRange(ResolveArray(source, sourceNamespaces,
                    "A source /StructTreeRoot /Namespaces").Select(importer.Import));
            if (!isPartial)
            {
                AddImportedArray(StructureAssociatedFilesName, structureAssociatedFiles,
                    "A source /StructTreeRoot /AF");
                AddImportedArray(PronunciationLexiconName, pronunciationLexicons,
                    "A source /StructTreeRoot /PronunciationLexicon");
            }

            void AddImportedArray(PdfName name, ICollection<PdfObject> destination, string description)
            {
                if (sourceRoot.TryGetValue(name, out PdfObject? value))
                    foreach (PdfObject item in ResolveArray(source, value, description))
                        destination.Add(importer.Import(item));
            }
        }

        var parentNumbers = new List<PdfObject>(parentEntries.Count * 2);
        foreach (PdfNumberTreeEntry entry in parentEntries.OrderBy(entry => entry.Key))
        {
            parentNumbers.Add(new PdfInteger(entry.Key));
            parentNumbers.Add(entry.Value);
        }
        PdfDictionary rebuiltParentTree = Dictionary(("Nums", new PdfArray(parentNumbers)));
        PdfObject parentTreeForRoot = rebuiltParentTree;
        if (targetRoot.TryGetValue(ParentTreeName, out targetParentTree)
            && targetParentTree is PdfIndirectReference parentReference)
        {
            update.ReplaceObject(parentReference.ObjectNumber, rebuiltParentTree);
            parentTreeForRoot = parentReference;
        }
        var replacements = new Dictionary<PdfName, PdfObject>
        {
            [StructureKidsName] = new PdfArray(structureKids),
            [ParentTreeName] = parentTreeForRoot,
            [ParentTreeNextKeyName] = new PdfInteger(nextKey)
        };
        if (namespaces.Count > 0) replacements[NamespacesName] = new PdfArray(namespaces);
        if (roleEntries.Count > 0) replacements[RoleMapName] = new PdfDictionary(roleEntries);
        if (classEntries.Count > 0) replacements[ClassMapName] = new PdfDictionary(classEntries);
        if (idEntries.Count > 0)
        {
            var names = new List<PdfObject>(idEntries.Count * 2);
            foreach (PdfNameTreeEntry entry in idEntries.OrderBy(
                         entry => Convert.ToBase64String(entry.Key.Bytes.Span),
                         StringComparer.Ordinal))
            {
                names.Add(entry.Key);
                names.Add(entry.Value);
            }
            replacements[IdTreeName] = Dictionary(("Names", new PdfArray(names)));
        }
        if (structureAssociatedFiles.Count > 0)
            replacements[StructureAssociatedFilesName] = new PdfArray(structureAssociatedFiles);
        if (pronunciationLexicons.Count > 0)
            replacements[PronunciationLexiconName] = new PdfArray(pronunciationLexicons);
        foreach (var extension in importedRootExtensions)
            replacements[extension.Key] = extension.Value;
        if (targetDocumentReference is not null && targetDocument is not null)
        {
            PdfDictionary mergedDocument = ReplaceMany(
                targetDocument, new Dictionary<PdfName, PdfObject>
                {
                    [StructureKidsName] = new PdfArray(targetDocumentKids)
                });
            if (targetDocumentIsNew)
                update.SetObject(targetDocumentReference, mergedDocument);
            else
                update.ReplaceObject(targetDocumentReference.ObjectNumber, mergedDocument);
        }
        PdfDictionary mergedRoot = ReplaceMany(targetRoot, replacements);
        if (targetRootIsNew) update.SetObject(targetRootReference, mergedRoot);
        else update.ReplaceObject(targetRootReference.ObjectNumber, mergedRoot);
        if (_tree.Catalog.TryGetValue(MarkInfoName, out PdfObject? markInfo))
            catalogReplacements[MarkInfoName] = markInfo;
        else
        {
            PageState firstTaggedPage = taggedGroups[0][0];
            if (firstTaggedPage.ImportedTree!.Catalog.TryGetValue(
                    MarkInfoName, out PdfObject? sourceMarkInfo))
                catalogReplacements[MarkInfoName] =
                    importers[firstTaggedPage].Import(sourceMarkInfo);
        }

        PdfDictionary ResolveEffectiveDictionary(PdfObject value, string description)
        {
            if (value is PdfIndirectReference reference
                && rewriteState is not null
                && rewriteState.RewrittenObjects.TryGetValue(
                    (reference.ObjectNumber, reference.Generation),
                    out PdfDictionary? rewritten))
                return rewritten;
            return ResolveDictionary(_document, value, description);
        }

        static IReadOnlyList<PdfObject> StructureKids(
            PdfDocument document, PdfObject value, string description)
        {
            PdfObject resolved = value is PdfIndirectReference reference
                ? document.Resolve(reference) : value;
            if (resolved is PdfArray array) return array.ToArray();
            if (resolved is PdfNull)
                throw new InvalidOperationException($"{description} resolves to null.");
            return [value];
        }

        static PdfIndirectReference? FindStructureElementParentReference(
            PdfDocument document, PdfDictionary element)
        {
            if (!element.TryGetValue(StructureKidsName, out PdfObject? kidsValue)) return null;
            PdfIndirectReference? result = null;
            foreach (PdfObject kid in StructureKids(
                         document, kidsValue, "A direct structure-element kids value"))
            {
                PdfObject resolved = kid is PdfIndirectReference reference
                    ? document.Resolve(reference) : kid;
                if (resolved is not PdfDictionary child) continue;
                if (!child.TryGetValue(StructureElementParentName, out PdfObject? parent)
                    || parent is not PdfIndirectReference parentReference)
                    return null;
                if (result is not null
                    && (result.ObjectNumber != parentReference.ObjectNumber
                        || result.Generation != parentReference.Generation))
                    return null;
                result = parentReference;
            }
            return result;
        }

        static bool HasStructureElementParentReference(
            PdfDocument document, PdfDictionary element)
        {
            if (!element.TryGetValue(StructureKidsName, out PdfObject? kidsValue)) return false;
            foreach (PdfObject kid in StructureKids(
                         document, kidsValue, "A direct structure-element kids value"))
            {
                PdfObject resolved = kid is PdfIndirectReference reference
                    ? document.Resolve(reference) : kid;
                if (resolved is PdfDictionary child
                    && child.TryGetValue(StructureElementParentName, out PdfObject? parent)
                    && parent is PdfIndirectReference)
                    return true;
            }
            return false;
        }

        static bool IsDocumentElement(PdfDictionary dictionary) =>
            dictionary.TryGetValue(Name("S"), out PdfObject? value)
            && value is PdfName name && name.ValueAsLatin1() == "Document";

        static void PrepareNames(
            PdfDictionary? sourceMap, ISet<PdfName> used,
            IDictionary<PdfName, PdfName> renames, string prefix, ref int next)
        {
            if (sourceMap is null) return;
            foreach (PdfName sourceName in sourceMap.Keys)
            {
                PdfName destinationName = sourceName;
                if (used.Contains(destinationName))
                {
                    do destinationName = Name($"{prefix}{next++}");
                    while (used.Contains(destinationName));
                    renames[sourceName] = destinationName;
                }
                used.Add(destinationName);
            }
        }

        static List<PdfObject> ReadOptionalArray(
            PdfDocument document, PdfDictionary dictionary, PdfName name, string description) =>
            dictionary.TryGetValue(name, out PdfObject? value)
                ? ResolveArray(document, value, description).ToList() : [];
    }

    private static PdfIndirectReference? FindStructureRootParentReference(
        PdfDocument document, PdfDictionary root)
    {
        if (!root.TryGetValue(StructureKidsName, out PdfObject? kids)) return null;
        IEnumerable<PdfObject> values = kids is PdfArray array ? array : [kids];
        PdfIndirectReference? result = null;
        foreach (PdfObject value in values)
        {
            PdfDictionary child = ResolveDictionary(
                document, value, "A top-level structure element");
            if (!child.TryGetValue(StructureElementParentName, out PdfObject? parent)
                || parent is not PdfIndirectReference parentReference)
                return null;
            if (result is not null && (result.ObjectNumber != parentReference.ObjectNumber
                || result.Generation != parentReference.Generation)) return null;
            result = parentReference;
        }
        return result;
    }

    private static PdfDictionary RemapStructureDictionary(
        PdfDictionary dictionary, IReadOnlyDictionary<long, long> keyMap,
        IReadOnlyDictionary<PdfName, PdfName> roleRenames,
        IReadOnlyDictionary<PdfName, PdfName> classRenames,
        IReadOnlyDictionary<string, PdfString> idRenames)
    {
        var replacements = new Dictionary<PdfName, PdfObject>();
        foreach (PdfName name in new[] { StructParentsName, StructureParentName })
            if (dictionary.TryGetValue(name, out PdfObject? value))
            {
                long key = (value as PdfInteger)?.Value
                    ?? throw new InvalidOperationException($"/{name.ValueAsLatin1()} is not an integer.");
                if (!keyMap.TryGetValue(key, out long replacement))
                    throw new InvalidOperationException(
                        $"Structure-parent key {key} has no ParentTree entry.");
                replacements[name] = new PdfInteger(replacement);
            }
        bool isStructureElement = dictionary.TryGetValue(TypeName, out PdfObject? type)
            && type is PdfName typeName && typeName.Equals(StructureElementName);
        if (isStructureElement
            && dictionary.TryGetValue(StructureTypeName, out PdfObject? structureType)
            && structureType is PdfName role
            && roleRenames.TryGetValue(role, out PdfName? renamedRole))
            replacements[StructureTypeName] = renamedRole;
        if (isStructureElement
            && dictionary.TryGetValue(StructureClassName, out PdfObject? structureClass))
        {
            PdfObject renamedClass = structureClass switch
            {
                PdfName name when classRenames.TryGetValue(name, out PdfName? replacement) => replacement,
                PdfArray array => new PdfArray(array.Select(item => item is PdfName name
                    && classRenames.TryGetValue(name, out PdfName? replacement)
                        ? replacement : item)),
                _ => structureClass
            };
            replacements[StructureClassName] = renamedClass;
        }
        if (isStructureElement
            && dictionary.TryGetValue(StructureIdName, out PdfObject? structureId)
            && structureId is PdfString id
            && idRenames.TryGetValue(Convert.ToBase64String(id.Bytes.Span), out PdfString? renamedId))
            replacements[StructureIdName] = renamedId;
        return replacements.Count == 0 ? dictionary : ReplaceMany(dictionary, replacements);
    }

    private void AddImportedTaggedConformanceProperties(
        IReadOnlyList<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        if (_tree.Pages.Count != 0 || importedGroups.Count != 1) return;
        PageState[] group = importedGroups[0];
        if (_pages.Count != group.Length || !_pages.All(group.Contains)) return;
        PdfPageTree tree = group[0].ImportedTree!;
        if (!tree.Catalog.ContainsKey(StructTreeRootName)
            || IsCompleteImport(group, tree)) return;
        PdfObjectGraphImporter importer = importers[group[0]];
        foreach (PdfName name in new[]
                 {
                     MetadataName, LanguageName, ViewerPreferencesName,
                     OutputIntentsName, Name("Version")
                 })
            if (tree.Catalog.TryGetValue(name, out PdfObject? value))
                catalogReplacements[name] = importer.Import(value);
    }

    private void AddImportedCatalogExtensions(
        IEnumerable<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        var entries = _tree.Catalog.TryGetValue(ExtensionsName, out PdfObject? targetValue)
            ? ResolveDictionary(_document, targetValue,
                "The destination catalog /Extensions value").ToList()
            : [];
        var names = entries.Select(entry => entry.Key).ToHashSet();
        bool importedAny = false;
        foreach (PageState[] group in importedGroups)
        {
            PdfPageTree tree = group[0].ImportedTree!;
            if (!IsCompleteImport(group, tree)) continue;
            if (!tree.Catalog.TryGetValue(ExtensionsName, out PdfObject? sourceValue)) continue;
            PdfDocument source = group[0].ImportedDocument!;
            PdfDictionary extensions = ResolveDictionary(
                source, sourceValue, "A source catalog /Extensions value");
            foreach (var entry in extensions)
            {
                if (!names.Add(entry.Key))
                    throw new NotSupportedException(
                        $"Multiple documents define the /Extensions /{entry.Key.ValueAsLatin1()} namespace and cannot be merged safely.");
                entries.Add(new KeyValuePair<PdfName, PdfObject>(
                    entry.Key, importers[group[0]].Import(entry.Value)));
            }
            importedAny = true;
        }
        if (importedAny)
            catalogReplacements[ExtensionsName] = new PdfDictionary(entries);
    }

    private void AddImportedDocumentProperties(
        PdfIncrementalUpdateBuilder update,
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
        if (group[0].ImportedDocument!.CrossReferences.TryGetTrailerValue(
                Name("Info"), out PdfObject? information))
            update.SetDocumentInformation(importer.Import(information));
        else
            update.SetDocumentInformation(null);
        foreach (var entry in tree.Catalog.Where(entry =>
                     !entry.Key.Equals(TypeName)
                     && !entry.Key.Equals(PagesName)
                     && !entry.Key.Equals(PermissionsName)))
            catalogReplacements[entry.Key] = importer.Import(entry.Value);
    }

    private void ValidateExistingStructureTreePageSet()
    {
        if (!_tree.Catalog.ContainsKey(StructTreeRootName)) return;
        bool additionsAreSupported = _pages.Where(page => page.Entry is null)
            .All(page => page.ImportedDocument is null
                || page.ImportedTree!.Catalog.ContainsKey(StructTreeRootName));
        if (!additionsAreSupported)
            throw new NotSupportedException(
                "Untagged content cannot be imported into an existing tagged PDF. Reordering, removing, adding blank pages, and merging complete tagged documents are supported.");
    }

    private StructureRewriteState? RewriteExistingStructureTree(
        PdfIncrementalUpdateBuilder update,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        if (!_tree.Catalog.TryGetValue(StructTreeRootName, out PdfObject? rootValue)) return null;
        var retainedPages = _pages.Where(page => page.Entry is not null)
            .Select(page => (page.Entry!.Reference.ObjectNumber,
                page.Entry.Reference.Generation)).ToHashSet();
        var removedPages = _tree.Pages.Select(page =>
                (page.Reference.ObjectNumber, page.Reference.Generation))
            .Where(reference => !retainedPages.Contains(reference)).ToHashSet();
        if (removedPages.Count == 0) return null;

        StructurePruningPlan plan = BuildStructurePruningPlan(
            _document, rootValue, _tree.Pages, removedPages);
        foreach (var replacement in plan.RewrittenObjects)
            update.ReplaceObject(replacement.Key.ObjectNumber, replacement.Value);
        if (rootValue is not PdfIndirectReference)
            catalogReplacements[StructTreeRootName] = plan.Root;
        return new StructureRewriteState(
            plan.EffectiveRoot, plan.RewrittenObjects, plan.ParentEntries);
    }

    private static StructurePruningPlan BuildStructurePruningPlan(
        PdfDocument document, PdfObject rootValue,
        IReadOnlyList<PdfPageTreeEntry> pages,
        IReadOnlySet<(int ObjectNumber, int Generation)> removedPages)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rootValue);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(removedPages);

        var active = new HashSet<(int ObjectNumber, int Generation)>();
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        var retainedStructureObjects =
            new HashSet<(int ObjectNumber, int Generation)>();
        var rewrittenObjects =
            new Dictionary<(int ObjectNumber, int Generation), PdfDictionary>();
        IReadOnlyList<PdfNumberTreeEntry> retainedParentEntries = [];
        PdfObject? rewrittenRootValue = Rewrite(rootValue, inheritedPage: null, isRoot: true);
        if (rewrittenRootValue is null)
            throw new InvalidOperationException("Removing pages cannot remove the structure-tree root.");

        PdfDictionary root = ResolveDictionary(document, rootValue, "The /StructTreeRoot");
        if (root.TryGetValue(ParentTreeName, out PdfObject? parentTreeValue))
        {
            var removedKeys = pages
                .Where(page => removedPages.Contains(
                    (page.Reference.ObjectNumber, page.Reference.Generation)))
                .Select(page => page.Dictionary.TryGetValue(StructParentsName, out PdfObject? value)
                    ? (value as PdfInteger)?.Value : null)
                .Where(value => value.HasValue).Select(value => value!.Value).ToHashSet();
            retainedParentEntries = PdfNumberTree.Read(
                document, parentTreeValue).Where(entry => !removedKeys.Contains(entry.Key)).ToArray();
            var numbers = new List<PdfObject>(retainedParentEntries.Count * 2);
            foreach (PdfNumberTreeEntry entry in retainedParentEntries.OrderBy(entry => entry.Key))
            {
                numbers.Add(new PdfInteger(entry.Key));
                numbers.Add(entry.Value);
            }
            var rebuilt = Dictionary(("Nums", new PdfArray(numbers)));
            if (parentTreeValue is PdfIndirectReference parentTreeReference)
                rewrittenObjects[(parentTreeReference.ObjectNumber,
                    parentTreeReference.Generation)] = rebuilt;
            else
            {
                PdfDictionary currentRoot = EffectiveDictionary(
                    rewrittenRootValue, "The rewritten /StructTreeRoot");
                PdfDictionary replacedRoot = ReplaceMany(currentRoot,
                    new Dictionary<PdfName, PdfObject> { [ParentTreeName] = rebuilt });
                if (rootValue is PdfIndirectReference rootReference)
                    rewrittenObjects[(rootReference.ObjectNumber,
                        rootReference.Generation)] = replacedRoot;
                else
                    rewrittenRootValue = replacedRoot;
            }
        }

        PdfDictionary effectiveRoot = rewrittenRootValue as PdfDictionary
            ?? (rootValue is PdfIndirectReference rootReferenceValue
                && rewrittenObjects.TryGetValue(
                    (rootReferenceValue.ObjectNumber, rootReferenceValue.Generation),
                    out PdfDictionary? recordedRoot)
                    ? recordedRoot
                    : root);

        PdfObject? Rewrite(PdfObject value,
            (int ObjectNumber, int Generation)? inheritedPage, bool isRoot = false)
        {
            if (value is PdfArray array)
            {
                var children = new List<PdfObject>();
                foreach (PdfObject child in array)
                {
                    PdfObject? rewritten = Rewrite(child, inheritedPage);
                    if (rewritten is not null) children.Add(rewritten);
                }
                return children.Count == 0 ? null : new PdfArray(children);
            }
            if (value is PdfInteger)
                return inheritedPage.HasValue && removedPages.Contains(inheritedPage.Value)
                    ? null : value;

            PdfIndirectReference? reference = value as PdfIndirectReference;
            PdfObject resolved = reference is null ? value : document.Resolve(reference);
            if (resolved is PdfArray indirectArray)
            {
                if (reference is null) return Rewrite(indirectArray, inheritedPage);
                var identity = (reference.ObjectNumber, reference.Generation);
                if (!active.Add(identity))
                    throw new InvalidOperationException("The structure tree contains a cycle.");
                if (!visited.Add(identity))
                {
                    active.Remove(identity);
                    throw new InvalidOperationException(
                        "The structure tree references the same kids array more than once.");
                }
                try
                {
                    return Rewrite(indirectArray, inheritedPage);
                }
                finally
                {
                    active.Remove(identity);
                }
            }
            if (resolved is not PdfDictionary dictionary) return value;
            var referenceIdentity = reference is null ? default
                : (reference.ObjectNumber, reference.Generation);
            if (reference is not null && !active.Add(referenceIdentity))
                throw new InvalidOperationException("The structure tree contains a cycle.");
            if (reference is not null && !visited.Add(referenceIdentity))
            {
                active.Remove(referenceIdentity);
                throw new InvalidOperationException(
                    "The structure tree references the same element more than once.");
            }
            try
            {
                (int ObjectNumber, int Generation)? explicitPage = PageReference(dictionary);
                (int ObjectNumber, int Generation)? effectivePage = explicitPage ?? inheritedPage;
                string? type = dictionary.TryGetValue(TypeName, out PdfObject? typeValue)
                    && typeValue is PdfName typeName ? typeName.ValueAsLatin1() : null;
                if (type is "MCR" or "OBJR")
                {
                    if (effectivePage.HasValue && removedPages.Contains(effectivePage.Value))
                        return null;
                    if (reference is not null)
                        retainedStructureObjects.Add(referenceIdentity);
                    return value;
                }

                bool hadKids = dictionary.TryGetValue(StructureKidsName, out PdfObject? kids);
                PdfObject? rewrittenKids = hadKids ? Rewrite(kids!, effectivePage) : null;
                bool pageWasRemoved = explicitPage.HasValue && removedPages.Contains(explicitPage.Value);
                if (!isRoot && pageWasRemoved && rewrittenKids is null) return null;
                if (reference is not null)
                    retainedStructureObjects.Add(referenceIdentity);
                var replacements = new Dictionary<PdfName, PdfObject>();
                var removals = new List<PdfName>();
                if (hadKids)
                {
                    if (rewrittenKids is null) removals.Add(StructureKidsName);
                    else replacements[StructureKidsName] = rewrittenKids;
                }
                if (pageWasRemoved && rewrittenKids is not null) removals.Add(PageName);
                if (replacements.Count == 0 && removals.Count == 0) return value;
                PdfDictionary rewritten = ReplaceMany(dictionary, replacements, removals);
                if (reference is not null)
                {
                    rewrittenObjects[(reference.ObjectNumber, reference.Generation)] = rewritten;
                    return reference;
                }
                return rewritten;
            }
            finally
            {
                if (reference is not null) active.Remove(referenceIdentity);
            }
        }

        (int ObjectNumber, int Generation)? PageReference(PdfDictionary dictionary) =>
            dictionary.TryGetValue(PageName, out PdfObject? page)
                && page is PdfIndirectReference reference
                    ? (reference.ObjectNumber, reference.Generation) : null;

        PdfDictionary EffectiveDictionary(PdfObject value, string description) =>
            value is PdfIndirectReference reference
                && rewrittenObjects.TryGetValue(
                    (reference.ObjectNumber, reference.Generation),
                    out PdfDictionary? rewritten)
                    ? rewritten
                    : ResolveDictionary(document, value, description);

        return new StructurePruningPlan(
            rewrittenRootValue, effectiveRoot, rewrittenObjects,
            retainedParentEntries, retainedStructureObjects);
    }

    private void AddImportedOptionalContent(
        IReadOnlyList<PageState[]> importedGroups,
        IReadOnlyDictionary<PageState, PdfObjectGraphImporter> importers,
        IDictionary<PdfName, PdfObject> catalogReplacements)
    {
        PageState[][] sourceLayeredGroups = importedGroups.Where(group =>
            group[0].ImportedTree!.Catalog.ContainsKey(OptionalContentPropertiesName)).ToArray();
        var layeredGroupList = new List<PageState[]>();
        var selectedGroups = new Dictionary<PageState[],
            IReadOnlySet<(int ObjectNumber, int Generation)>>();
        foreach (PageState[] group in sourceLayeredGroups)
        {
            if (IsCompleteImport(group, group[0].ImportedTree!))
            {
                layeredGroupList.Add(group);
                continue;
            }
            PdfDocument source = group[0].ImportedDocument!;
            PageState[] layeredPages = group.Where(page =>
                PageUsesOptionalContent(source, page.ImportedEntry!)).ToArray();
            if (layeredPages.Length == 0) continue;
            HashSet<(int ObjectNumber, int Generation)> references = layeredPages.SelectMany(page =>
                    OptionalContentGroupReferences(source, page.ImportedEntry!))
                .ToHashSet();
            if (references.Count == 0)
                throw new NotSupportedException(
                    "A selected page uses optional content whose OCG dependencies cannot be resolved from its page graph.");
            selectedGroups.Add(group, references);
            layeredGroupList.Add(group);
        }
        PageState[][] layeredGroups = layeredGroupList.ToArray();
        if (layeredGroups.Length == 0) return;

        bool targetHasProperties = _tree.Catalog.TryGetValue(
            OptionalContentPropertiesName, out PdfObject? targetPropertiesValue);
        if (!targetHasProperties && layeredGroups.Length == 1
            && !selectedGroups.ContainsKey(layeredGroups[0]))
        {
            PageState[] group = layeredGroups[0];
            catalogReplacements[OptionalContentPropertiesName] =
                importers[group[0]].Import(
                    group[0].ImportedTree!.Catalog[OptionalContentPropertiesName]);
            return;
        }

        PdfDictionary targetProperties = targetHasProperties
            ? ResolveDictionary(_document, targetPropertiesValue!, "The destination /OCProperties")
            : new PdfDictionary([]);
        var groups = targetHasProperties
            ? ResolveArray(_document, targetProperties[Name("OCGs")],
                "The destination /OCProperties /OCGs").ToList()
            : [];
        if (groups.Select(item => ReferenceIdentity(AssertReference(
                    item, "A destination /OCProperties /OCGs entry")))
                .Distinct().Count() != groups.Count)
            throw new InvalidOperationException(
                "The destination /OCProperties /OCGs array contains a duplicate group reference.");
        PdfDictionary targetDefault = targetHasProperties
            ? ResolveDictionary(_document, targetProperties[Name("D")],
                "The destination optional-content default configuration")
            : Dictionary(("Name", new PdfString("Default"u8, PdfStringForm.Literal)),
                ("BaseState", Name("ON")));
        var defaultArrays = new Dictionary<PdfName, List<PdfObject>>();
        foreach (string key in new[] { "Order", "ON", "OFF", "Locked", "RBGroups", "AS" })
        {
            PdfName name = Name(key);
            defaultArrays[name] = targetDefault.TryGetValue(name, out PdfObject? value)
                ? ResolveArray(_document, value, $"The destination optional-content /{key} array").ToList()
                : [];
        }
        var configurations = targetProperties.TryGetValue(Name("Configs"), out PdfObject? targetConfigs)
            ? ResolveArray(_document, targetConfigs, "The destination /OCProperties /Configs").ToList()
            : [];
        string targetBaseState = OptionalContentBaseState(targetDefault);

        foreach (PageState[] group in layeredGroups)
        {
            PdfDocument source = group[0].ImportedDocument!;
            PdfObjectGraphImporter importer = importers[group[0]];
            PdfDictionary sourceProperties = ResolveDictionary(source,
                group[0].ImportedTree!.Catalog[OptionalContentPropertiesName],
                "A source /OCProperties");
            PdfArray allSourceGroups = ResolveArray(source, sourceProperties[Name("OCGs")],
                "A source /OCProperties /OCGs");
            (int ObjectNumber, int Generation)[] sourceGroupIdentities =
                allSourceGroups.Select(item =>
                    item as PdfIndirectReference
                    ?? throw new InvalidOperationException(
                        "An /OCProperties /OCGs entry is not an indirect reference."))
                .Select(reference => (reference.ObjectNumber, reference.Generation)).ToArray();
            if (sourceGroupIdentities.Distinct().Count() != sourceGroupIdentities.Length)
                throw new InvalidOperationException(
                    "A source /OCProperties /OCGs array contains a duplicate group reference.");
            HashSet<(int ObjectNumber, int Generation)> allSourceGroupReferences =
                sourceGroupIdentities.ToHashSet();
            IReadOnlySet<(int ObjectNumber, int Generation)> retainedGroupReferences =
                selectedGroups.TryGetValue(group,
                    out IReadOnlySet<(int ObjectNumber, int Generation)>? selected)
                    ? selected : allSourceGroupReferences;
            if (!retainedGroupReferences.IsSubsetOf(allSourceGroupReferences))
                throw new NotSupportedException(
                    "A selected page references an optional-content group absent from the source /OCProperties /OCGs array.");
            var sourceGroups = new PdfArray(allSourceGroups.Where(item =>
                retainedGroupReferences.Contains(ReferenceIdentity(
                    AssertReference(item, "An /OCProperties /OCGs entry")))));
            groups.AddRange(sourceGroups.Select(importer.Import));
            PdfDictionary sourceDefault = ResolveDictionary(source, sourceProperties[Name("D")],
                "A source optional-content default configuration");
            string sourceBaseState = OptionalContentBaseState(sourceDefault);
            var sourceOn = OptionalContentReferenceSet(source, sourceDefault, "ON");
            var sourceOff = OptionalContentReferenceSet(source, sourceDefault, "OFF");
            foreach (PdfObject sourceGroup in sourceGroups)
            {
                if (sourceGroup is not PdfIndirectReference sourceReference)
                    throw new InvalidOperationException("An /OCProperties /OCGs entry is not an indirect reference.");
                bool explicitlyOn = sourceOn.Contains(ReferenceIdentity(sourceReference));
                bool explicitlyOff = sourceOff.Contains(ReferenceIdentity(sourceReference));
                if (explicitlyOn && explicitlyOff)
                    throw new InvalidOperationException(
                        "An optional-content group appears in both /ON and /OFF.");
                if (sourceBaseState == "Unchanged" && !explicitlyOn && !explicitlyOff)
                    throw new NotSupportedException(
                        "A source optional-content configuration with /BaseState /Unchanged must explicitly list every imported group in /ON or /OFF.");
                bool visible = explicitlyOn || !explicitlyOff && sourceBaseState == "ON";
                bool targetDefaultVisible = targetBaseState == "ON";
                if (targetBaseState == "Unchanged" || visible != targetDefaultVisible)
                    defaultArrays[Name(visible ? "ON" : "OFF")].Add(importer.Import(sourceGroup));
            }
            foreach (string key in new[] { "Order", "Locked", "RBGroups", "AS" })
            {
                PdfName name = Name(key);
                if (sourceDefault.TryGetValue(name, out PdfObject? value))
                {
                    PdfObject? pruned = PruneConfigurationValue(value);
                    if (pruned is PdfArray prunedArray)
                        defaultArrays[name].AddRange(prunedArray.Select(importer.Import));
                }
            }
            if (sourceProperties.TryGetValue(Name("Configs"), out PdfObject? sourceConfigs))
                foreach (PdfObject configuration in ResolveArray(
                             source, sourceConfigs, "A source /OCProperties /Configs"))
                {
                    PdfObject? pruned = PruneConfigurationValue(configuration);
                    if (pruned is not null) configurations.Add(importer.Import(pruned));
                }

            PdfObject? PruneConfigurationValue(PdfObject value, int depth = 0)
            {
                if (depth > 256)
                    throw new NotSupportedException(
                        "An optional-content configuration is too deeply nested.");
                if (value is PdfIndirectReference reference)
                {
                    var identity = ReferenceIdentity(reference);
                    if (allSourceGroupReferences.Contains(identity))
                        return retainedGroupReferences.Contains(identity)
                            ? reference : null;
                    PdfObject resolved = source.Resolve(reference);
                    return resolved is PdfNull
                        ? null : PruneConfigurationValue(resolved, depth + 1);
                }
                if (value is PdfArray array)
                {
                    var items = array.Select(item => PruneConfigurationValue(item, depth + 1))
                        .Where(item => item is not null).Cast<PdfObject>().ToArray();
                    return items.Length == 0 ? null : new PdfArray(items);
                }
                if (value is PdfDictionary dictionary)
                {
                    var entries = new List<KeyValuePair<PdfName, PdfObject>>();
                    foreach (var entry in dictionary)
                    {
                        PdfObject? pruned = PruneConfigurationValue(entry.Value, depth + 1);
                        if (pruned is not null)
                            entries.Add(new KeyValuePair<PdfName, PdfObject>(entry.Key, pruned));
                    }
                    if (dictionary.ContainsKey(Name("OCGs"))
                        && entries.All(entry => !entry.Key.Equals(Name("OCGs")))) return null;
                    return new PdfDictionary(entries);
                }
                return value;
            }
        }

        var defaultReplacements = defaultArrays
            .Where(entry => entry.Value.Count > 0)
            .ToDictionary(entry => entry.Key, entry => (PdfObject)new PdfArray(entry.Value));
        PdfDictionary mergedDefault = ReplaceMany(targetDefault, defaultReplacements,
            defaultArrays.Where(entry => entry.Value.Count == 0)
                .Select(entry => entry.Key).ToArray());
        var propertyReplacements = new Dictionary<PdfName, PdfObject>
        {
            [Name("OCGs")] = new PdfArray(groups), [Name("D")] = mergedDefault
        };
        if (configurations.Count > 0)
            propertyReplacements[Name("Configs")] = new PdfArray(configurations);
        catalogReplacements[OptionalContentPropertiesName] =
            ReplaceMany(targetProperties, propertyReplacements);

        static string OptionalContentBaseState(PdfDictionary configuration)
        {
            string state = configuration.TryGetValue(Name("BaseState"), out PdfObject? value)
                ? (value as PdfName)?.ValueAsLatin1()
                    ?? throw new InvalidOperationException("An optional-content /BaseState is not a name.")
                : "ON";
            return state is "ON" or "OFF" or "Unchanged" ? state
                : throw new InvalidOperationException(
                    $"Optional-content /BaseState /{state} is not defined.");
        }

        static HashSet<(int ObjectNumber, int Generation)> OptionalContentReferenceSet(
            PdfDocument document, PdfDictionary configuration, string key)
        {
            PdfName name = Name(key);
            if (!configuration.TryGetValue(name, out PdfObject? value)) return [];
            return ResolveArray(document, value, $"An optional-content /{key} array")
                .Select(item => item as PdfIndirectReference
                    ?? throw new InvalidOperationException(
                        $"An optional-content /{key} entry is not an indirect reference."))
                .Select(ReferenceIdentity).ToHashSet();
        }

        static (int ObjectNumber, int Generation) ReferenceIdentity(
            PdfIndirectReference reference) => (reference.ObjectNumber, reference.Generation);

        static PdfIndirectReference AssertReference(PdfObject value, string description) =>
            value as PdfIndirectReference
            ?? throw new InvalidOperationException($"{description} is not an indirect reference.");
    }

    private static bool DestinationsStayWithinImportedPages(
        PdfDocument document, IEnumerable<PdfObject> destinations, PageState[] group)
    {
        return destinations.All(destination =>
            DestinationStaysWithinImportedPages(document, destination, group));
    }

    private static bool DestinationStaysWithinImportedPages(
        PdfDocument document, PdfObject destination, PageState[] group)
    {
        var retainedPages = group.Select(page =>
            (page.ImportedEntry!.Reference.ObjectNumber,
                page.ImportedEntry.Reference.Generation)).ToHashSet();
        var sourcePages = group[0].ImportedTree!.Pages.Select(page =>
            (page.Reference.ObjectNumber, page.Reference.Generation)).ToHashSet();
        return TargetsRetainedPage(destination, 0);

        bool TargetsRetainedPage(PdfObject value, int depth)
        {
            if (depth > 32)
                throw new InvalidOperationException("A named destination is too deeply indirect.");
            if (value is PdfIndirectReference reference)
            {
                var identity = (reference.ObjectNumber, reference.Generation);
                if (sourcePages.Contains(identity))
                    return retainedPages.Contains(identity);
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

    private static DestinationReferences ReferencedNamedDestinations(
        PdfDocument document, PageState[] group)
    {
        var strings = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<PdfName>();
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        var sourcePages = group[0].ImportedTree!.Pages.Select(page =>
            (page.Reference.ObjectNumber, page.Reference.Generation)).ToHashSet();
        int visitedObjects = 0;
        foreach (PageState page in group)
            Visit(page.ImportedEntry!.Dictionary, 0);
        return new DestinationReferences(strings, names);

        void Visit(PdfObject value, int depth)
        {
            if (depth > 64)
                throw new InvalidOperationException(
                    "A selected page dependency graph is too deeply nested.");
            if (value is PdfIndirectReference reference)
            {
                var identity = (reference.ObjectNumber, reference.Generation);
                if (sourcePages.Contains(identity) || !visited.Add(identity))
                    return;
                if (++visitedObjects > 100_000)
                    throw new NotSupportedException(
                        "A selected page dependency graph contains too many objects.");
                Visit(document.Resolve(reference), depth + 1);
                return;
            }
            if (value is PdfArray array)
            {
                foreach (PdfObject item in array) Visit(item, depth + 1);
                return;
            }
            PdfDictionary? dictionary = value switch
            {
                PdfDictionary candidate => candidate,
                PdfStream stream => stream.Dictionary,
                _ => null
            };
            if (dictionary is null) return;
            foreach (PdfName key in new[] { Name("Dest"), DestinationName })
                if (dictionary.TryGetValue(key, out PdfObject? destination))
                {
                    if (destination is PdfString text)
                        strings.Add(Convert.ToBase64String(text.Bytes.Span));
                    else if (destination is PdfName name)
                        names.Add(name);
                }
            foreach ((PdfName key, PdfObject item) in dictionary)
                if (!key.Equals(ParentName)) Visit(item, depth + 1);
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
        update.SetObject(destinationReference,
            importer.ApplyDictionaryTransform(new PdfDictionary(entries)));
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

    private static bool PageUsesOptionalContent(
        PdfDocument source, PdfPageTreeEntry page)
    {
        const int maximumObjects = 1_000_000;
        const int maximumDepth = 256;
        const int maximumContentBytes = 64 * 1024 * 1024;
        var visited = new HashSet<(int ObjectNumber, int Generation)>
        { (page.Reference.ObjectNumber, page.Reference.Generation) };
        int objectCount = 0;

        if (page.InheritedValues.TryGetValue(Name("Resources"), out PdfObject? resources)
            && UsesOptionalContent(resources, 0, scanContent: false))
            return true;
        foreach (var entry in page.Dictionary)
        {
            if (entry.Key.Equals(ParentName) || entry.Key.Equals(Name("Resources"))) continue;
            if (UsesOptionalContent(entry.Value, 0, entry.Key.Equals(Name("Contents"))))
                return true;
        }
        return false;

        bool UsesOptionalContent(PdfObject value, int depth, bool scanContent)
        {
            if (depth >= maximumDepth)
                throw new NotSupportedException(
                    "The selected page graph is too deeply nested to prove optional-content independence.");
            if (value is PdfIndirectReference reference)
            {
                var key = (reference.ObjectNumber, reference.Generation);
                if (!visited.Add(key)) return false;
                if (++objectCount > maximumObjects)
                    throw new NotSupportedException(
                        "The selected page graph is too large to prove optional-content independence.");
                return UsesOptionalContent(source.Resolve(reference), depth + 1, scanContent);
            }
            if (value is PdfArray array)
                return array.Any(item => UsesOptionalContent(item, depth + 1, scanContent));
            if (value is PdfStream stream)
            {
                if (UsesDictionary(stream.Dictionary, depth + 1)) return true;
                bool isForm = stream.Dictionary.TryGetValue(SubtypeName, out PdfObject? subtype)
                    && subtype is PdfName subtypeName && subtypeName.ValueAsLatin1() == "Form";
                bool isPattern = stream.Dictionary.TryGetValue(TypeName, out PdfObject? type)
                    && type is PdfName typeName && typeName.ValueAsLatin1() == "Pattern";
                return (scanContent || isForm || isPattern) && ContentUsesOptionalContent(stream);
            }
            return value is PdfDictionary dictionary && UsesDictionary(dictionary, depth + 1);
        }

        bool UsesDictionary(PdfDictionary dictionary, int depth)
        {
            if (dictionary.TryGetValue(TypeName, out PdfObject? type)
                && type is PdfName typeName
                && (typeName.Equals(OptionalContentGroupName)
                    || typeName.Equals(OptionalContentMembershipName)))
                return true;
            if (dictionary.ContainsKey(OptionalContentName)) return true;
            return dictionary.Any(entry => !entry.Key.Equals(ParentName)
                && UsesOptionalContent(entry.Value, depth + 1, scanContent: false));
        }

        static bool ContentUsesOptionalContent(PdfStream stream)
        {
            byte[] decoded = PdfStreamDecoder.Decode(stream, maximumContentBytes);
            var tokenizer = new PdfTokenizer(decoded);
            while (true)
            {
                PdfToken token = tokenizer.Read();
                if (token.Kind == PdfTokenKind.EndOfInput) return false;
                if (token.Kind == PdfTokenKind.Name
                    && token.Value.Span.SequenceEqual("OC"u8)) return true;
                if (token.Kind == PdfTokenKind.Keyword
                    && token.Value.Span.SequenceEqual("BI"u8))
                    tokenizer = SkipInlineImage(decoded, tokenizer);
            }

            static PdfTokenizer SkipInlineImage(byte[] content, PdfTokenizer tokenizer)
            {
                while (true)
                {
                    PdfToken token = tokenizer.Read();
                    if (token.Kind == PdfTokenKind.EndOfInput)
                        throw new InvalidOperationException(
                            "An inline image dictionary has no ID operator.");
                    if (token.Kind != PdfTokenKind.Keyword
                        || !token.Value.Span.SequenceEqual("ID"u8)) continue;
                    int dataStart = tokenizer.Position;
                    if (dataStart >= content.Length || !IsContentWhitespace(content[dataStart]))
                        throw new InvalidOperationException(
                            "An inline image ID operator is not followed by whitespace.");
                    for (int index = dataStart + 1; index + 1 < content.Length; index++)
                    {
                        if (content[index] != (byte)'E' || content[index + 1] != (byte)'I') continue;
                        if (index == 0 || !IsContentWhitespace(content[index - 1])) continue;
                        int after = index + 2;
                        if (after < content.Length
                            && !IsContentWhitespace(content[after])
                            && !IsContentDelimiter(content[after])) continue;
                        return new PdfTokenizer(content, after);
                    }
                    throw new InvalidOperationException("An inline image has no EI operator.");
                }
            }

            static bool IsContentWhitespace(byte value) =>
                value is 0 or 9 or 10 or 12 or 13 or 32;

            static bool IsContentDelimiter(byte value) => value is
                (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or
                (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or
                (byte)'/' or (byte)'%';
        }
    }

    private static IReadOnlySet<(int ObjectNumber, int Generation)> OptionalContentGroupReferences(
        PdfDocument source, PdfPageTreeEntry page)
    {
        const int maximumObjects = 1_000_000;
        const int maximumDepth = 256;
        var result = new HashSet<(int ObjectNumber, int Generation)>();
        var visited = new HashSet<(int ObjectNumber, int Generation)>
        { (page.Reference.ObjectNumber, page.Reference.Generation) };
        int objectCount = 0;
        if (page.InheritedValues.TryGetValue(Name("Resources"), out PdfObject? resources))
            Visit(resources, 0);
        foreach (var entry in page.Dictionary)
            if (!entry.Key.Equals(ParentName) && !entry.Key.Equals(Name("Resources")))
                Visit(entry.Value, 0);
        return result;

        void Visit(PdfObject value, int depth)
        {
            if (depth >= maximumDepth)
                throw new NotSupportedException(
                    "The selected page graph is too deeply nested to collect optional-content dependencies.");
            if (value is PdfIndirectReference reference)
            {
                var key = (reference.ObjectNumber, reference.Generation);
                if (!visited.Add(key)) return;
                if (++objectCount > maximumObjects)
                    throw new NotSupportedException(
                        "The selected page graph is too large to collect optional-content dependencies.");
                PdfObject resolved = source.Resolve(reference);
                if (resolved is PdfDictionary dictionary
                    && dictionary.TryGetValue(TypeName, out PdfObject? type)
                    && type is PdfName typeName && typeName.Equals(OptionalContentGroupName))
                    result.Add(key);
                Visit(resolved, depth + 1);
                return;
            }
            if (value is PdfArray array)
            {
                foreach (PdfObject item in array) Visit(item, depth + 1);
                return;
            }
            PdfDictionary? dictionaryValue = value switch
            {
                PdfDictionary dictionary => dictionary,
                PdfStream stream => stream.Dictionary,
                _ => null
            };
            if (dictionaryValue is null) return;
            foreach (var entry in dictionaryValue)
                if (!entry.Key.Equals(ParentName)) Visit(entry.Value, depth + 1);
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
    private sealed record DestinationReferences(
        IReadOnlySet<string> StringNames, IReadOnlySet<PdfName> LegacyNames);
    private sealed record ImportedOutlineSegment(
        PdfDocument Document,
        PdfDictionary Root,
        PdfObjectGraphImporter Importer,
        PdfIndirectReference[] TopLevel,
        Dictionary<string, PdfIndirectReference> Mapped,
        long Count);
    private sealed record StructureRewriteState(
        PdfDictionary Root,
        IReadOnlyDictionary<(int ObjectNumber, int Generation), PdfDictionary> RewrittenObjects,
        IReadOnlyList<PdfNumberTreeEntry> ParentEntries);
    private sealed record StructurePruningPlan(
        PdfObject Root,
        PdfDictionary EffectiveRoot,
        IReadOnlyDictionary<(int ObjectNumber, int Generation), PdfDictionary> RewrittenObjects,
        IReadOnlyList<PdfNumberTreeEntry> ParentEntries,
        IReadOnlySet<(int ObjectNumber, int Generation)> RetainedStructureObjects);
    private sealed record FormPruningPlan(
        PdfDictionary Form,
        IReadOnlyDictionary<(int ObjectNumber, int Generation), PdfDictionary> RewrittenObjects,
        IReadOnlySet<(int ObjectNumber, int Generation)> RetainedFields);

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
