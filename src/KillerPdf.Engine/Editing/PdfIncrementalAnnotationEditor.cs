using System.Globalization;
using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Security;
using KillerPdf.Engine.Signing;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Editing;

/// <summary>
/// Adds annotations to existing pages through a byte-preserving incremental revision.
/// Original page contents and every source byte remain untouched.
/// </summary>
public sealed class PdfIncrementalAnnotationEditor
{
    private static readonly PdfName AnnotsName = new("Annots"u8);
    private static readonly PdfName StructTreeRootName = new("StructTreeRoot"u8);
    private static readonly PdfName StructureKidsName = new("K"u8);
    private static readonly PdfName ParentTreeName = new("ParentTree"u8);
    private static readonly PdfName ParentTreeNextKeyName = new("ParentTreeNextKey"u8);
    private static readonly PdfName NamespacesName = new("Namespaces"u8);

    private readonly PdfDocument _document;
    private readonly PdfPageTree _tree;
    private readonly IReadOnlyList<PdfPageTreeEntry> _pages;
    private readonly List<PendingAnnotation> _annotations = [];

    public PdfIncrementalAnnotationEditor(PdfDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _tree = PdfPageTree.Read(document);
        _pages = _tree.Pages;
    }

    public int PageCount => _pages.Count;

    public PdfIncrementalAnnotationEditor AddTextNote(
        int pageIndex, double x, double y, string contents,
        PdfRgbColor? color = null, bool open = false, double size = 24)
    {
        ValidatePage(pageIndex);
        ArgumentNullException.ThrowIfNull(contents);
        ValidateCoordinate(x, nameof(x));
        ValidateCoordinate(y, nameof(y));
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        _annotations.Add(new PendingTextNote(
            pageIndex, x, y, size, contents, color ?? PdfRgbColor.NoteYellow, open));
        return this;
    }

    public PdfIncrementalAnnotationEditor AddHighlight(
        int pageIndex, double x, double y, double width, double height,
        string? contents = null, PdfRgbColor? color = null, double opacity = 0.35)
        => AddTextMarkup(PdfTextMarkupType.Highlight, pageIndex, x, y, width, height,
            contents, color ?? PdfRgbColor.Yellow, opacity);

    public PdfIncrementalAnnotationEditor AddUnderline(
        int pageIndex, double x, double y, double width, double height,
        string? contents = null, PdfRgbColor? color = null, double opacity = 1)
        => AddTextMarkup(PdfTextMarkupType.Underline, pageIndex, x, y, width, height,
            contents, color ?? new PdfRgbColor(0, 0.35, 0.9), opacity);

    public PdfIncrementalAnnotationEditor AddStrikeOut(
        int pageIndex, double x, double y, double width, double height,
        string? contents = null, PdfRgbColor? color = null, double opacity = 1)
        => AddTextMarkup(PdfTextMarkupType.StrikeOut, pageIndex, x, y, width, height,
            contents, color ?? new PdfRgbColor(0.9, 0.1, 0.1), opacity);

    public PdfIncrementalAnnotationEditor AddSquiggly(
        int pageIndex, double x, double y, double width, double height,
        string? contents = null, PdfRgbColor? color = null, double opacity = 1)
        => AddTextMarkup(PdfTextMarkupType.Squiggly, pageIndex, x, y, width, height,
            contents, color ?? new PdfRgbColor(0.9, 0.1, 0.1), opacity);

    public PdfIncrementalAnnotationEditor AddFreeText(
        int pageIndex, double x, double y, double width, double height,
        string contents, TrueTypeFont font, double fontSize = 12,
        PdfRgbColor? textColor = null, PdfRgbColor? fillColor = null,
        PdfRgbColor? borderColor = null, double borderWidth = 1, double opacity = 1)
    {
        ValidatePage(pageIndex);
        ValidateRectangle(x, y, width, height);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(font);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        ValidateStroke(borderWidth, opacity);
        ValidateDrawableText(font, contents, nameof(contents));
        _annotations.Add(new PendingFreeText(
            pageIndex, x, y, width, height, contents, font, fontSize,
            textColor ?? new PdfRgbColor(0, 0, 0), fillColor,
            borderColor ?? new PdfRgbColor(0, 0, 0), borderWidth, opacity));
        return this;
    }

    public PdfIncrementalAnnotationEditor AddLine(
        int pageIndex, PdfPoint start, PdfPoint end, PdfRgbColor? color = null,
        double lineWidth = 1, double opacity = 1, string? contents = null)
    {
        ValidatePage(pageIndex);
        ValidateStroke(lineWidth, opacity);
        if (start == end) throw new ArgumentException("A line must have two distinct endpoints.", nameof(end));
        _annotations.Add(new PendingLine(
            pageIndex, start, end, color ?? new PdfRgbColor(0, 0, 0), lineWidth, opacity, contents));
        return this;
    }

    public PdfIncrementalAnnotationEditor AddRectangle(
        int pageIndex, double x, double y, double width, double height,
        PdfRgbColor? strokeColor = null, PdfRgbColor? fillColor = null,
        double lineWidth = 1, double opacity = 1, string? contents = null)
        => AddShape(PendingShapeType.Square, pageIndex, x, y, width, height,
            strokeColor, fillColor, lineWidth, opacity, contents);

    public PdfIncrementalAnnotationEditor AddEllipse(
        int pageIndex, double x, double y, double width, double height,
        PdfRgbColor? strokeColor = null, PdfRgbColor? fillColor = null,
        double lineWidth = 1, double opacity = 1, string? contents = null)
        => AddShape(PendingShapeType.Circle, pageIndex, x, y, width, height,
            strokeColor, fillColor, lineWidth, opacity, contents);

    public PdfIncrementalAnnotationEditor AddInk(
        int pageIndex, IReadOnlyList<PdfPoint> points, PdfRgbColor? color = null,
        double lineWidth = 2, double opacity = 1, string? contents = null)
        => AddInk(pageIndex, [points], color, lineWidth, opacity, contents);

    public PdfIncrementalAnnotationEditor AddInk(
        int pageIndex, IReadOnlyList<IReadOnlyList<PdfPoint>> strokes, PdfRgbColor? color = null,
        double lineWidth = 2, double opacity = 1, string? contents = null)
    {
        ValidatePage(pageIndex);
        ArgumentNullException.ThrowIfNull(strokes);
        ValidateStroke(lineWidth, opacity);
        if (strokes.Count == 0 || strokes.Any(stroke => stroke is null || stroke.Count < 2))
            throw new ArgumentException("Ink requires at least one stroke containing two points.", nameof(strokes));
        _annotations.Add(new PendingInk(
            pageIndex, strokes.Select(stroke => stroke.ToArray()).ToArray(),
            color ?? new PdfRgbColor(0, 0, 0), lineWidth, opacity, contents));
        return this;
    }

    public PdfIncrementalAnnotationEditor AddImageStamp(
        int pageIndex, double x, double y, double width, double height,
        PdfImage image, string? contents = null)
    {
        ValidatePage(pageIndex);
        ValidateRectangle(x, y, width, height);
        ArgumentNullException.ThrowIfNull(image);
        _annotations.Add(new PendingImageStamp(
            pageIndex, x, y, width, height, image, contents));
        return this;
    }

    private PdfIncrementalAnnotationEditor AddShape(
        PendingShapeType type, int pageIndex, double x, double y, double width, double height,
        PdfRgbColor? strokeColor, PdfRgbColor? fillColor,
        double lineWidth, double opacity, string? contents)
    {
        ValidatePage(pageIndex);
        ValidateRectangle(x, y, width, height);
        ValidateStroke(lineWidth, opacity);
        _annotations.Add(new PendingShape(type, pageIndex, x, y, width, height,
            strokeColor ?? new PdfRgbColor(0, 0, 0), fillColor, lineWidth, opacity, contents));
        return this;
    }

    private PdfIncrementalAnnotationEditor AddTextMarkup(
        PdfTextMarkupType type, int pageIndex, double x, double y, double width, double height,
        string? contents, PdfRgbColor color, double opacity)
    {
        ValidatePage(pageIndex);
        ValidateRectangle(x, y, width, height);
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        _annotations.Add(new PendingTextMarkup(
            type, pageIndex, x, y, width, height, contents, color, opacity));
        return this;
    }

    public byte[] Build(PdfIncrementalUpdateWriteOptions? options = null)
    {
        if (_annotations.Count == 0)
            throw new InvalidOperationException("The incremental annotation update is empty.");
        if (_document.PasswordAuthenticationRole == PdfPasswordAuthenticationRole.User
            && (_document.DeclaredPermissions is not PdfDocumentPermissions permissions
                || !permissions.AllowAnnotationModification))
            throw new InvalidOperationException(
                "The PDF user password does not permit annotation modification.");
        PdfSignatureCertificationPermission? certification =
            PdfSignatureReader.ReadCertificationPermission(_document);
        if (certification.HasValue
            && certification != PdfSignatureCertificationPermission.FormFillingSignaturesAndAnnotations)
            throw new InvalidOperationException(
                "The document certification signature prohibits annotation changes.");
        var update = new PdfIncrementalUpdateBuilder(_document);
        var allocated = _annotations.Select(annotation => new AllocatedAnnotation(
            annotation, update.ReserveObject(), update.ReserveObject())).ToArray();
        IReadOnlyDictionary<int, long> structureParentKeys =
            PrepareTaggedAnnotationStructure(update, allocated);
        Dictionary<TrueTypeFont, EditorFontBinding> fonts = AllocateFonts(update);
        Dictionary<PdfImage, PdfIndirectReference> images = AllocateImages(update);

        foreach (AllocatedAnnotation item in allocated)
        {
            PdfPageTreeEntry page = _pages[item.Definition.PageIndex];
            switch (item.Definition)
            {
                case PendingTextNote note:
                    update.SetObject(item.AnnotationReference,
                        WithStructureParent(TextNoteDictionary(note, page.Reference,
                            item.AnnotationReference, item.AppearanceReference), item,
                            structureParentKeys));
                    update.SetObject(item.AppearanceReference, TextNoteAppearance(note));
                    break;
                case PendingTextMarkup markup:
                    update.SetObject(item.AnnotationReference,
                        WithStructureParent(TextMarkupDictionary(markup, page.Reference,
                            item.AnnotationReference, item.AppearanceReference), item,
                            structureParentKeys));
                    update.SetObject(item.AppearanceReference, TextMarkupAppearance(markup));
                    break;
                case PendingFreeText freeText:
                    EditorFontBinding binding = fonts[freeText.Font];
                    update.SetObject(item.AnnotationReference,
                        WithStructureParent(FreeTextDictionary(freeText, page.Reference,
                            item.AnnotationReference, item.AppearanceReference, binding.Resource),
                            item, structureParentKeys));
                    update.SetObject(item.AppearanceReference,
                        FreeTextAppearance(freeText, binding.Resource, binding.Type0Reference,
                            binding.Usage));
                    break;
                case PendingLine line:
                    update.SetObject(item.AnnotationReference,
                        WithStructureParent(LineDictionary(line, page.Reference,
                            item.AnnotationReference, item.AppearanceReference), item,
                            structureParentKeys));
                    update.SetObject(item.AppearanceReference, LineAppearance(line));
                    break;
                case PendingShape shape:
                    update.SetObject(item.AnnotationReference,
                        WithStructureParent(ShapeDictionary(shape, page.Reference,
                            item.AnnotationReference, item.AppearanceReference), item,
                            structureParentKeys));
                    update.SetObject(item.AppearanceReference, ShapeAppearance(shape));
                    break;
                case PendingInk ink:
                    update.SetObject(item.AnnotationReference,
                        WithStructureParent(InkDictionary(ink, page.Reference,
                            item.AnnotationReference, item.AppearanceReference), item,
                            structureParentKeys));
                    update.SetObject(item.AppearanceReference, InkAppearance(ink));
                    break;
                case PendingImageStamp stamp:
                    update.SetObject(item.AnnotationReference,
                        WithStructureParent(ImageStampDictionary(stamp, page.Reference,
                            item.AnnotationReference, item.AppearanceReference), item,
                            structureParentKeys));
                    update.SetObject(item.AppearanceReference,
                        ImageStampAppearance(stamp, images[stamp.Image]));
                    break;
                default:
                    throw new InvalidOperationException("Unknown annotation definition.");
            }
        }

        foreach (IGrouping<int, AllocatedAnnotation> group in allocated.GroupBy(item => item.Definition.PageIndex))
            AppendPageAnnotations(update, _pages[group.Key], group.Select(item =>
                (item.AnnotationReference, AllocatedAnnotationName(item))));
        return update.Build(options);
    }

    private static string AllocatedAnnotationName(AllocatedAnnotation item)
    {
        string kind = item.Definition switch
        {
            PendingTextNote => "Note",
            PendingTextMarkup markup => markup.Type.ToString(),
            PendingFreeText => "FreeText",
            PendingLine => "Line",
            PendingShape shape => shape.Type.ToString(),
            PendingInk => "Ink",
            PendingImageStamp => "Image",
            _ => throw new InvalidOperationException("Unknown annotation definition.")
        };
        return $"KillerPDF-{kind}-{item.AnnotationReference.ObjectNumber}";
    }

    private IReadOnlyDictionary<int, long> PrepareTaggedAnnotationStructure(
        PdfIncrementalUpdateBuilder update, IReadOnlyList<AllocatedAnnotation> annotations)
    {
        if (!_tree.Catalog.TryGetValue(StructTreeRootName, out PdfObject? rootValue))
            return new Dictionary<int, long>();
        PdfDictionary root = ResolveDictionary(rootValue,
            "The document structure-tree root is not a dictionary.");
        if (!root.TryGetValue(Name("Type"), out PdfObject? rootType))
            throw new InvalidOperationException(
                "The document structure-tree root has no /Type /StructTreeRoot value.");
        PdfObject resolvedRootType = ResolveValue(rootType,
            "The document structure-tree root /Type value");
        if (resolvedRootType is not PdfName rootTypeName
            || rootTypeName.ValueAsLatin1() != "StructTreeRoot")
            throw new InvalidOperationException(
                "The document structure-tree root has an invalid /Type value.");
        PdfIndirectReference rootReference;
        if (rootValue is PdfIndirectReference indirectRoot)
            rootReference = indirectRoot;
        else
        {
            rootReference = FindStructureRootParentReference(root)
                ?? throw new NotSupportedException(
                    "A direct structure-tree root has no unambiguous indirect parent reference.");
            var catalogEntries = _tree.Catalog.ToDictionary(
                entry => entry.Key, entry => entry.Value);
            catalogEntries[StructTreeRootName] = rootReference;
            update.ReplaceObject(_tree.CatalogReference.ObjectNumber,
                new PdfDictionary(catalogEntries));
        }
        var documentTarget = FindDocumentStructureElement(root, rootReference, update);
        PdfIndirectReference documentElementReference = documentTarget.Reference;
        PdfDictionary documentElement = documentTarget.Dictionary;
        root = documentTarget.Root;
        bool documentElementIsNew = documentTarget.IsNew;
        PdfIndirectReference? namespaceReference = FindStructureNamespace(root);

        IReadOnlyList<PdfNumberTreeEntry> existingEntries =
            root.TryGetValue(ParentTreeName, out PdfObject? parentTreeValue)
                ? PdfNumberTree.Read(_document, parentTreeValue)
                : [];
        if (existingEntries.Any(entry => entry.Key < 0))
            throw new InvalidOperationException(
                "The structure-tree ParentTree contains a negative key.");
        foreach (PdfNumberTreeEntry entry in existingEntries)
            ValidateParentTreeValue(entry.Value,
                $"The structure-tree ParentTree value for key {entry.Key}");
        long nextKey = root.TryGetValue(ParentTreeNextKeyName, out PdfObject? nextValue)
            ? (ResolveValue(nextValue,
                    "The structure-tree /ParentTreeNextKey value") as PdfInteger)?.Value
                ?? throw new InvalidOperationException(
                    "The structure-tree /ParentTreeNextKey is not an integer.")
            : existingEntries.Count == 0 ? 0 : checked(existingEntries.Max(entry => entry.Key) + 1);
        if (nextKey < 0)
            throw new InvalidOperationException(
                "The structure-tree /ParentTreeNextKey cannot be negative.");
        if (existingEntries.Count > 0)
            nextKey = Math.Max(nextKey, checked(existingEntries.Max(entry => entry.Key) + 1));

        var keys = new Dictionary<int, long>();
        var newStructureReferences = new List<PdfIndirectReference>();
        var parentNumbers = new List<PdfObject>();
        foreach (PdfNumberTreeEntry entry in existingEntries.OrderBy(entry => entry.Key))
        {
            parentNumbers.Add(new PdfInteger(entry.Key));
            parentNumbers.Add(entry.Value);
        }
        foreach (AllocatedAnnotation annotation in annotations)
        {
            string description = AnnotationDescription(annotation.Definition);
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidOperationException(
                    "Annotations added to a tagged PDF require descriptive contents.");
            PdfIndirectReference structureReference = update.ReserveObject();
            long key = nextKey;
            nextKey = checked(nextKey + 1);
            keys.Add(annotation.AnnotationReference.ObjectNumber, key);
            newStructureReferences.Add(structureReference);
            PdfIndirectReference pageReference = _pages[annotation.Definition.PageIndex].Reference;
            var entries = new List<(string Name, PdfObject Value)>
            {
                ("Type", Name("StructElem")),
                ("S", Name("Annot")),
                ("P", documentElementReference),
                ("Pg", pageReference),
                ("Alt", UnicodeString(description)),
                ("K", Dictionary(
                    ("Type", Name("OBJR")),
                    ("Pg", pageReference),
                    ("Obj", annotation.AnnotationReference)))
            };
            if (namespaceReference is not null)
                entries.Add(("NS", namespaceReference));
            update.SetObject(structureReference, Dictionary(entries.ToArray()));
            parentNumbers.Add(new PdfInteger(key));
            parentNumbers.Add(structureReference);
        }

        PdfIndirectReference rebuiltParentTree = update.AddObject(
            Dictionary(("Nums", new PdfArray(parentNumbers))));
        var rootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
        rootEntries[ParentTreeName] = rebuiltParentTree;
        rootEntries[ParentTreeNextKeyName] = new PdfInteger(nextKey);
        update.ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(rootEntries));

        var documentEntries = documentElement.ToDictionary(entry => entry.Key, entry => entry.Value);
        var kids = new List<PdfObject>();
        if (documentEntries.TryGetValue(StructureKidsName, out PdfObject? existingKids))
        {
            PdfObject resolvedKids = ResolveValue(existingKids,
                "The document structure element /K value");
            if (resolvedKids is PdfNull)
                throw new InvalidOperationException(
                    "The document structure element /K value contains a stale indirect reference.");
            if (resolvedKids is PdfArray array)
            {
                foreach (PdfObject kid in array)
                {
                    ValidateDocumentKid(kid);
                    kids.Add(kid);
                }
            }
            else
            {
                ValidateDocumentKid(existingKids);
                kids.Add(existingKids);
            }
        }
        kids.AddRange(newStructureReferences);
        documentEntries[StructureKidsName] = kids.Count == 1
            ? kids[0] : new PdfArray(kids);
        if (documentElementIsNew)
            update.SetObject(documentElementReference, new PdfDictionary(documentEntries));
        else
            update.ReplaceObject(documentElementReference.ObjectNumber,
                new PdfDictionary(documentEntries));
        return keys;

        void ValidateDocumentKid(PdfObject kid)
        {
            PdfObject resolved = ResolveValue(kid,
                "The document structure element /K value");
            if (resolved is PdfInteger mcid && mcid.Value >= 0) return;
            if (resolved is PdfDictionary dictionary)
            {
                if (dictionary.TryGetValue(Name("S"), out PdfObject? role))
                {
                    PdfObject resolvedRole = ResolveValue(role,
                        "A document structure child /S value");
                    if (resolvedRole is PdfName) return;
                }
                if (dictionary.TryGetValue(Name("MCID"), out PdfObject? markedContent))
                {
                    PdfObject resolvedMcid = ResolveValue(markedContent,
                        "A document structure child /MCID value");
                    if (resolvedMcid is PdfInteger dictionaryMcid
                        && dictionaryMcid.Value >= 0) return;
                }
                if (dictionary.TryGetValue(Name("Obj"), out PdfObject? referencedObject)
                    && referencedObject is PdfIndirectReference objectReference
                    && ResolveValue(objectReference,
                        "A document structure OBJR /Obj value") is PdfDictionary)
                    return;
            }
            throw new InvalidOperationException(
                "The document structure element /K value contains an invalid child.");
        }

        void ValidateParentTreeValue(PdfObject value, string description)
        {
            PdfObject resolved = ResolveValue(value, description);
            if (resolved is PdfArray array)
            {
                foreach (PdfObject item in array)
                {
                    if (item is PdfNull) continue;
                    if (item is not PdfIndirectReference itemReference
                        || ResolveValue(itemReference,
                            $"{description} array entry") is not PdfDictionary element)
                        throw new InvalidOperationException(
                            $"{description} array contains a non-structure-element entry.");
                    ValidateStructureElement(element, description);
                }
                return;
            }
            if (resolved is not PdfDictionary dictionary)
                throw new InvalidOperationException(
                    $"{description} is not a structure element or array.");
            ValidateStructureElement(dictionary, description);
        }

        void ValidateStructureElement(PdfDictionary element, string description)
        {
            if (!element.TryGetValue(Name("S"), out PdfObject? role))
                throw new InvalidOperationException(
                    $"{description} structure element has no /S role name.");
            PdfObject resolvedRole = ResolveValue(
                role, $"{description} structure element /S value");
            if (resolvedRole is not PdfName)
                throw new InvalidOperationException(
                    $"{description} structure element /S value is not a name.");
        }
    }

    private PdfIndirectReference? FindStructureRootParentReference(PdfDictionary root)
    {
        if (!root.TryGetValue(StructureKidsName, out PdfObject? kidsValue)) return null;
        PdfObject resolvedKids = ResolveValue(
            kidsValue, "The structure-tree root /K value");
        IEnumerable<PdfObject> kids = resolvedKids is PdfArray array ? array : [resolvedKids];
        PdfIndirectReference? result = null;
        foreach (PdfObject kid in kids)
        {
            PdfDictionary child = ResolveDictionary(kid,
                "A top-level structure element is not a dictionary.");
            if (!child.TryGetValue(Name("P"), out PdfObject? parent)
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

    private (PdfIndirectReference Reference, PdfDictionary Dictionary,
        PdfDictionary Root, bool IsNew) FindDocumentStructureElement(
            PdfDictionary root, PdfIndirectReference rootReference,
            PdfIncrementalUpdateBuilder update)
    {
        if (!root.TryGetValue(StructureKidsName, out PdfObject? kidsValue))
            throw new InvalidOperationException("The structure-tree root has no children.");
        PdfObject resolvedKids = ResolveValue(
            kidsValue, "The structure-tree root /K value");
        PdfObject[] kids = resolvedKids is PdfArray array ? array.ToArray() : [resolvedKids];
        PdfIndirectReference? fallback = null;
        for (int index = 0; index < kids.Length; index++)
        {
            PdfObject kid = kids[index];
            PdfDictionary dictionary = ResolveDictionary(kid,
                "A top-level structure element is not a dictionary.");
            if (kid is PdfIndirectReference reference)
            {
                fallback ??= reference;
                if (!dictionary.TryGetValue(Name("P"), out PdfObject? parent)
                    || parent is not PdfIndirectReference parentReference
                    || parentReference.ObjectNumber != rootReference.ObjectNumber
                    || parentReference.Generation != rootReference.Generation)
                    throw new InvalidOperationException(
                        "A top-level structure element has no reciprocal /P link to the structure-tree root.");
            }
            if (!dictionary.TryGetValue(Name("S"), out PdfObject? type))
                throw new InvalidOperationException(
                    "A top-level structure element has no /S role name.");
            PdfObject resolvedType = ResolveValue(
                type, "A top-level structure element /S value");
            PdfName name = resolvedType as PdfName
                ?? throw new InvalidOperationException(
                    "A top-level structure element /S value is not a name.");
            if (name.ValueAsLatin1() == "Document")
                return Target(kid, dictionary, index);
        }
        if (fallback is not null)
            return (fallback, ResolveDictionary(fallback,
                "The top-level structure element is not a dictionary."), root, false);
        if (kids.Length == 0)
            throw new InvalidOperationException("The structure-tree root has no children.");
        return Target(kids[0], ResolveDictionary(kids[0],
            "The top-level structure element is not a dictionary."), 0);

        (PdfIndirectReference, PdfDictionary, PdfDictionary, bool) Target(
            PdfObject value, PdfDictionary dictionary, int index)
        {
            if (value is PdfIndirectReference reference)
                return (reference, dictionary, root, false);
            PdfIndirectReference? documentReference =
                FindStructureElementParentReference(dictionary);
            bool isNew = false;
            if (documentReference is null)
            {
                if (HasStructureElementParentReference(dictionary))
                    throw new NotSupportedException(
                        "A direct top-level structure element has ambiguous child parent references.");
                documentReference = update.ReserveObject();
                isNew = true;
            }
            var entries = dictionary.ToDictionary(entry => entry.Key, entry => entry.Value);
            entries[Name("P")] = rootReference;
            kids[index] = documentReference;
            var rootEntries = root.ToDictionary(entry => entry.Key, entry => entry.Value);
            rootEntries[StructureKidsName] = kids.Length == 1
                ? kids[0] : new PdfArray(kids);
            return (documentReference, new PdfDictionary(entries),
                new PdfDictionary(rootEntries), isNew);
        }
    }

    private PdfIndirectReference? FindStructureElementParentReference(PdfDictionary element)
    {
        if (!element.TryGetValue(StructureKidsName, out PdfObject? kidsValue)) return null;
        PdfIndirectReference? result = null;
        foreach (PdfObject kid in StructureKids(kidsValue))
        {
            PdfObject resolved = ResolveValue(
                kid, "A direct structure-element child");
            if (resolved is not PdfDictionary child) continue;
            if (!child.TryGetValue(Name("P"), out PdfObject? parent)
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

    private bool HasStructureElementParentReference(PdfDictionary element)
    {
        if (!element.TryGetValue(StructureKidsName, out PdfObject? kidsValue)) return false;
        return StructureKids(kidsValue).Any(kid =>
        {
            PdfObject resolved = ResolveValue(
                kid, "A direct structure-element child");
            return resolved is PdfDictionary child
                && child.TryGetValue(Name("P"), out PdfObject? parent)
                && parent is PdfIndirectReference;
        });
    }

    private IReadOnlyList<PdfObject> StructureKids(PdfObject value)
    {
        PdfObject resolved = ResolveValue(value,
            "A structure-element /K value");
        return resolved is PdfArray array ? array.ToArray() : [value];
    }

    private PdfIndirectReference? FindStructureNamespace(PdfDictionary root)
    {
        if (!root.TryGetValue(NamespacesName, out PdfObject? value)) return null;
        PdfObject resolved = ResolveValue(value,
            "The structure-tree /Namespaces value");
        PdfArray namespaces = resolved as PdfArray
            ?? throw new InvalidOperationException("The structure-tree /Namespaces value is not an array.");
        PdfIndirectReference? result = null;
        foreach (PdfObject namespaceValue in namespaces)
        {
            PdfIndirectReference reference = namespaceValue as PdfIndirectReference
                ?? throw new InvalidOperationException(
                    "A structure namespace is not an indirect reference.");
            PdfDictionary definition = ResolveDictionary(reference,
                "A structure namespace is not a dictionary.");
            if (!definition.TryGetValue(Name("Type"), out PdfObject? type))
                throw new InvalidOperationException(
                    "A structure namespace has no /Type /Namespace value.");
            PdfObject resolvedType = ResolveValue(
                type, "A structure namespace /Type value");
            if (resolvedType is not PdfName typeName
                || typeName.ValueAsLatin1() != "Namespace")
                throw new InvalidOperationException(
                    "A structure namespace has an invalid /Type value.");
            if (!definition.TryGetValue(Name("NS"), out PdfObject? uri))
                throw new InvalidOperationException(
                    "A structure namespace has no /NS string.");
            PdfObject resolvedUri = ResolveValue(
                uri, "A structure namespace /NS value");
            PdfString text = resolvedUri as PdfString
                ?? throw new InvalidOperationException(
                    "A structure namespace /NS value is not a string.");
            if (definition.TryGetValue(Name("Schema"), out PdfObject? schema)
                && ResolveValue(schema,
                    "A structure namespace /Schema value") is not PdfDictionary)
                throw new InvalidOperationException(
                    "A structure namespace /Schema value is not a dictionary.");
            if (DecodePdfString(text) != "http://iso.org/pdf2/ssn") continue;
            if (result is not null)
                throw new InvalidOperationException(
                    "The structure-tree /Namespaces array contains duplicate PDF 2.0 namespaces.");
            result = reference;
        }
        return result;
    }

    private static string DecodePdfString(PdfString value)
        => PdfUnicodeEncoding.DecodeTextString(
            value.Bytes.Span, "A structure namespace URI");

    private PdfDictionary ResolveDictionary(PdfObject value, string message) =>
        ResolveValue(value, message) as PdfDictionary
            ?? throw new InvalidOperationException(message);

    private PdfObject ResolveValue(PdfObject value, string description)
    {
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        for (int depth = 0; value is PdfIndirectReference reference; depth++)
        {
            if (depth > 32)
                throw new InvalidOperationException(
                    $"{description} is too deeply indirect.");
            if (!visited.Add((reference.ObjectNumber, reference.Generation)))
                throw new InvalidOperationException(
                    $"{description} contains an indirect-reference cycle.");
            value = _document.Resolve(reference);
        }
        return value;
    }

    private static PdfDictionary WithStructureParent(
        PdfDictionary dictionary, AllocatedAnnotation annotation,
        IReadOnlyDictionary<int, long> keys)
    {
        if (!keys.TryGetValue(annotation.AnnotationReference.ObjectNumber, out long key))
            return dictionary;
        var entries = dictionary.ToDictionary(entry => entry.Key, entry => entry.Value);
        entries[Name("StructParent")] = new PdfInteger(key);
        return new PdfDictionary(entries);
    }

    private static string AnnotationDescription(PendingAnnotation annotation) => annotation switch
    {
        PendingTextNote value => value.Contents,
        PendingTextMarkup value => value.Contents ?? string.Empty,
        PendingFreeText value => value.Contents,
        PendingLine value => value.Contents ?? string.Empty,
        PendingShape value => value.Contents ?? string.Empty,
        PendingInk value => value.Contents ?? string.Empty,
        PendingImageStamp value => value.Contents ?? string.Empty,
        _ => string.Empty
    };

    private Dictionary<TrueTypeFont, EditorFontBinding> AllocateFonts(PdfIncrementalUpdateBuilder update)
    {
        var result = new Dictionary<TrueTypeFont, EditorFontBinding>();
        int sequence = 0;
        foreach (IGrouping<TrueTypeFont, PendingFreeText> group in
            _annotations.OfType<PendingFreeText>().GroupBy(value => value.Font))
        {
            var usage = new EmbeddedFontUsage(group.Key, new PdfName(
                Encoding.ASCII.GetBytes($"KpF{sequence + 1}")));
            foreach (FontGlyphMapping mapping in group.SelectMany(
                         value => group.Key.MapText(value.Contents)))
            {
                if (mapping.UnicodeSequence is "\r" or "\n") continue;
                usage.AddMapping(mapping.Glyph, mapping.UnicodeSequence);
            }
            PdfIndirectReference type0 = update.ReserveObject();
            PdfIndirectReference cidFont = update.ReserveObject();
            PdfIndirectReference descriptor = update.ReserveObject();
            PdfIndirectReference fontFile = update.ReserveObject();
            PdfIndirectReference toUnicode = update.ReserveObject();
            PdfIndirectReference encoding = update.ReserveObject();
            EmbeddedTrueTypeFontObjects values = PdfEmbeddedTrueTypeFontFactory.Create(
                group.Key, usage.Mappings, type0, cidFont, descriptor, fontFile, toUnicode,
                encoding);
            update.SetObject(type0, values.Type0).SetObject(cidFont, values.CidFont)
                .SetObject(descriptor, values.Descriptor).SetObject(fontFile, values.FontFile)
                .SetObject(toUnicode, values.ToUnicode).SetObject(encoding, values.Encoding);
            result.Add(group.Key, new EditorFontBinding(
                new PdfName(Encoding.ASCII.GetBytes($"KpF{++sequence}")), type0, usage));
        }
        return result;
    }

    private Dictionary<PdfImage, PdfIndirectReference> AllocateImages(
        PdfIncrementalUpdateBuilder update)
    {
        var result = new Dictionary<PdfImage, PdfIndirectReference>();
        foreach (PdfImage image in _annotations.OfType<PendingImageStamp>()
            .Select(value => value.Image).Distinct())
            Add(image);
        return result;

        PdfIndirectReference Add(PdfImage image)
        {
            if (result.TryGetValue(image, out PdfIndirectReference? existing)) return existing;
            PdfIndirectReference reference = update.ReserveObject();
            result.Add(image, reference);
            PdfIndirectReference? softMask = image.SoftMask is null ? null : Add(image.SoftMask);
            update.SetObject(reference, PdfImageXObjectFactory.Create(image, softMask));
            return reference;
        }
    }

    private void AppendPageAnnotations(
        PdfIncrementalUpdateBuilder update, PdfPageTreeEntry page,
        IEnumerable<(PdfIndirectReference Reference, string Name)> additions)
    {
        var pending = additions.ToArray();
        var values = new List<PdfObject>();
        var annotationIdentities = new HashSet<(int ObjectNumber, int Generation)>();
        var annotationNames = new HashSet<string>(StringComparer.Ordinal);
        if (page.Dictionary.TryGetValue(AnnotsName, out PdfObject existing))
        {
            PdfArray array;
            if (existing is PdfIndirectReference reference)
            {
                array = ResolveValue(reference,
                    $"Page {page.Index + 1} /Annots value") as PdfArray
                    ?? throw new InvalidOperationException($"Page {page.Index + 1} /Annots reference is not an array.");
                PrepareExistingAnnotations(array);
                foreach (PdfObject annotation in array)
                    ValidateExistingAnnotation(annotation);
                values.AddRange(array);
                AddPendingAnnotations();
                PdfIndirectReference replacementArray = update.AddObject(
                    new PdfArray(values));
                PdfDictionary replacementPage = new(page.Dictionary
                    .Where(entry => !entry.Key.Equals(AnnotsName))
                    .Append(new KeyValuePair<PdfName, PdfObject>(
                        AnnotsName, replacementArray)));
                update.ReplaceObject(page.Reference.ObjectNumber, replacementPage);
                return;
            }
            array = existing as PdfArray
                ?? throw new InvalidOperationException($"Page {page.Index + 1} /Annots value is not an array.");
            PrepareExistingAnnotations(array);
            foreach (PdfObject annotation in array)
                ValidateExistingAnnotation(annotation);
            values.AddRange(array);
        }
        AddPendingAnnotations();
        var replacement = new PdfDictionary(page.Dictionary
            .Where(entry => !entry.Key.Equals(AnnotsName))
            .Append(new KeyValuePair<PdfName, PdfObject>(AnnotsName, new PdfArray(values))));
        update.ReplaceObject(page.Reference.ObjectNumber, replacement);

        void AddPendingAnnotations()
        {
            foreach ((PdfIndirectReference reference, string name) in pending)
            {
                if (!annotationNames.Add(name))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} already contains annotation /NM value '{name}'.");
                values.Add(reference);
            }
        }

        void PrepareExistingAnnotations(PdfArray annotations)
        {
            foreach (PdfObject annotation in annotations)
            {
                if (annotation is not PdfIndirectReference reference)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} /Annots contains a direct annotation entry.");
                if (!annotationIdentities.Add((reference.ObjectNumber, reference.Generation)))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} /Annots contains a duplicate annotation reference.");
            }
        }

        void ValidateExistingAnnotation(PdfObject value)
        {
            PdfIndirectReference reference = (PdfIndirectReference)value;
            PdfObject resolved = ResolveValue(reference,
                $"Page {page.Index + 1} annotation value");
            PdfDictionary annotation = resolved as PdfDictionary
                ?? throw new InvalidOperationException(
                    $"Page {page.Index + 1} /Annots contains a stale or non-dictionary entry.");
            PdfObject Resolve(PdfObject item) => ResolveValue(item,
                $"Page {page.Index + 1} annotation value");
            if (annotation.TryGetValue(Name("Type"), out PdfObject? type)
                && (Resolve(type) is not PdfName typeName
                    || typeName.ValueAsLatin1() != "Annot"))
                throw new InvalidOperationException(
                    $"Page {page.Index + 1} /Annots contains an entry with an invalid /Type value.");
            if (!annotation.TryGetValue(Name("Subtype"), out PdfObject? subtype)
                || Resolve(subtype) is not PdfName)
                throw new InvalidOperationException(
                    $"Page {page.Index + 1} /Annots contains an entry without a /Subtype name.");
            if (!annotation.TryGetValue(Name("Rect"), out PdfObject? rectangle)
                || Resolve(rectangle) is not PdfArray coordinates
                || coordinates.Count != 4
                || coordinates.Any(item => Resolve(item) is not (PdfInteger or PdfReal)))
                throw new InvalidOperationException(
                    $"Page {page.Index + 1} /Annots contains an entry without a four-number /Rect array.");
            if (annotation.TryGetValue(Name("P"), out PdfObject? owner)
                && (owner is not PdfIndirectReference ownerReference
                    || ownerReference.ObjectNumber != page.Reference.ObjectNumber
                    || ownerReference.Generation != page.Reference.Generation))
                throw new InvalidOperationException(
                    $"Page {page.Index + 1} /Annots contains an entry whose /P value identifies another page.");
            if (annotation.TryGetValue(Name("NM"), out PdfObject? nameValue))
            {
                PdfString name = Resolve(nameValue) as PdfString
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} /Annots contains an entry whose /NM value is not a string.");
                string decoded = PdfUnicodeEncoding.DecodeTextString(name.Bytes.Span,
                    $"Page {page.Index + 1} annotation /NM value");
                if (!annotationNames.Add(decoded))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} /Annots contains duplicate /NM values.");
            }
            foreach (string key in new[] { "Contents", "T", "Subj" })
            {
                if (!annotation.TryGetValue(Name(key), out PdfObject? textValue)) continue;
                PdfString text = Resolve(textValue) as PdfString
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /{key} value is not a string.");
                PdfUnicodeEncoding.DecodeTextString(text.Bytes.Span,
                    $"Page {page.Index + 1} annotation /{key} value");
            }
            if (annotation.TryGetValue(Name("Lang"), out PdfObject? languageValue))
            {
                PdfString language = Resolve(languageValue) as PdfString
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /Lang value is not a string.");
                string tag = PdfUnicodeEncoding.DecodeTextString(language.Bytes.Span,
                    $"Page {page.Index + 1} annotation /Lang value");
                if (!PdfLanguageTag.IsValid(tag))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /Lang value is not a valid BCP 47 language tag.");
            }
            if (annotation.TryGetValue(Name("RC"), out PdfObject? richText)
                && Resolve(richText) is not (PdfString or PdfStream))
                throw new InvalidOperationException(
                    $"Page {page.Index + 1} annotation /RC value is not a string or stream.");
            if (annotation.TryGetValue(Name("IT"), out PdfObject? intent)
                && Resolve(intent) is not PdfName)
                throw new InvalidOperationException(
                    $"Page {page.Index + 1} annotation /IT value is not a name.");
            if (annotation.TryGetValue(Name("IRT"), out PdfObject? replyValue))
            {
                if (replyValue is not PdfIndirectReference replyReference
                    || Resolve(replyValue) is not PdfDictionary reply
                    || !reply.TryGetValue(Name("Subtype"), out PdfObject? replySubtype)
                    || Resolve(replySubtype) is not PdfName)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /IRT value is not an indirect typed annotation dictionary.");
                if (!annotationIdentities.Contains((replyReference.ObjectNumber,
                        replyReference.Generation)))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /IRT target is not registered on the page.");
            }
            if (annotation.TryGetValue(Name("RT"), out PdfObject? replyType))
            {
                string replyName = (Resolve(replyType) as PdfName)?.ValueAsLatin1()
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /RT value is not a name.");
                if (replyName is not ("R" or "Group"))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /RT value /{replyName} is not defined.");
            }
            bool hasState = annotation.TryGetValue(
                Name("State"), out PdfObject? annotationStateValue);
            bool hasStateModel = annotation.TryGetValue(
                Name("StateModel"), out PdfObject? stateModelValue);
            if (hasState || hasStateModel)
            {
                if (((PdfName)Resolve(subtype)).ValueAsLatin1() != "Text")
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation state is only defined for text annotations.");
                if (!hasState || Resolve(annotationStateValue!) is not PdfString stateString
                    || !hasStateModel || Resolve(stateModelValue!) is not PdfString modelString)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /State and /StateModel must both be strings.");
                string stateModel = PdfUnicodeEncoding.DecodeTextString(modelString.Bytes.Span,
                    $"Page {page.Index + 1} annotation /StateModel value");
                string state = PdfUnicodeEncoding.DecodeTextString(stateString.Bytes.Span,
                    $"Page {page.Index + 1} annotation /State value");
                bool validState = stateModel switch
                {
                    "Marked" => state is "Marked" or "Unmarked",
                    "Review" => state is "Accepted" or "Rejected" or "Cancelled"
                        or "Completed" or "None",
                    _ => false
                };
                if (!validState)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /State /{state} is not defined for /StateModel /{stateModel}.");
            }
            if (annotation.TryGetValue(Name("Popup"), out PdfObject? popupValue))
            {
                if (popupValue is not PdfIndirectReference popupReference
                    || Resolve(popupValue) is not PdfDictionary popup
                    || !popup.TryGetValue(Name("Subtype"), out PdfObject? popupSubtype)
                    || Resolve(popupSubtype) is not PdfName popupSubtypeName
                    || popupSubtypeName.ValueAsLatin1() != "Popup")
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /Popup value is not an indirect popup annotation.");
                if (!annotationIdentities.Contains((popupReference.ObjectNumber,
                        popupReference.Generation)))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /Popup target is not registered on the page.");
                if (!popup.TryGetValue(Name("Parent"), out PdfObject? popupParent)
                    || popupParent is not PdfIndirectReference popupParentReference
                    || popupParentReference.ObjectNumber != reference.ObjectNumber
                    || popupParentReference.Generation != reference.Generation)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /Popup target does not link back through /Parent.");
            }
            string retainedSubtype = ((PdfName)Resolve(subtype)).ValueAsLatin1();
            if (retainedSubtype == "Popup")
            {
                if (!annotation.TryGetValue(Name("Parent"), out PdfObject? parentValue)
                    || parentValue is not PdfIndirectReference parentReference
                    || Resolve(parentValue) is not PdfDictionary parent)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} popup annotation has no indirect /Parent dictionary.");
                if (!annotationIdentities.Contains((parentReference.ObjectNumber,
                        parentReference.Generation)))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} popup /Parent is not registered on the page.");
                if (!parent.TryGetValue(Name("Popup"), out PdfObject? parentPopup)
                    || parentPopup is not PdfIndirectReference parentPopupReference
                    || parentPopupReference.ObjectNumber != reference.ObjectNumber
                    || parentPopupReference.Generation != reference.Generation)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} popup /Parent does not link back through /Popup.");
            }
            foreach (string key in new[] { "M", "CreationDate" })
            {
                if (!annotation.TryGetValue(Name(key), out PdfObject? dateValue)) continue;
                if (Resolve(dateValue) is not PdfString date
                    || !PdfDateStringValidator.IsValid(date))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /{key} value is not a valid PDF date.");
            }
            if (annotation.TryGetValue(Name("F"), out PdfObject? flags)
                && (Resolve(flags) is not PdfInteger flagValue || flagValue.Value < 0))
                throw new InvalidOperationException(
                    $"Page {page.Index + 1} annotation /F value is not a nonnegative integer.");
            if (annotation.TryGetValue(Name("CA"), out PdfObject? opacity))
            {
                double opacityValue = Resolve(opacity) switch
                {
                    PdfInteger integer => integer.Value,
                    PdfReal real => real.Value,
                    _ => double.NaN
                };
                if (!double.IsFinite(opacityValue) || opacityValue is < 0 or > 1)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /CA value is not a number from 0 through 1.");
            }
            if (annotation.TryGetValue(Name("C"), out PdfObject? colorValue))
            {
                PdfArray color = Resolve(colorValue) as PdfArray
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /C value is not an array.");
                if (color.Count is not (0 or 1 or 3 or 4)
                    || color.Any(item => !TryFiniteNumber(Resolve(item), out double component)
                        || component is < 0 or > 1))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /C value is not a valid color array.");
            }
            if (annotation.TryGetValue(Name("Border"), out PdfObject? borderValue))
            {
                PdfArray border = Resolve(borderValue) as PdfArray
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /Border value is not an array.");
                if (border.Count is not (3 or 4)
                    || Enumerable.Range(0, 3).Any(index =>
                        !TryFiniteNumber(Resolve(border[index]), out double number)
                        || number < 0))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /Border value has invalid radii or width.");
                if (border.Count == 4)
                {
                    PdfArray dash = Resolve(border[3]) as PdfArray
                        ?? throw new InvalidOperationException(
                            $"Page {page.Index + 1} annotation /Border dash value is not an array.");
                    if (dash.Any(item => !TryFiniteNumber(Resolve(item), out double number)
                            || number < 0)
                        || dash.Count > 0 && dash.All(item =>
                            TryFiniteNumber(Resolve(item), out double number) && number == 0))
                        throw new InvalidOperationException(
                            $"Page {page.Index + 1} annotation /Border dash array is invalid.");
                }
            }
            if (annotation.TryGetValue(Name("BS"), out PdfObject? borderStyleValue))
            {
                PdfDictionary borderStyle = Resolve(borderStyleValue) as PdfDictionary
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /BS value is not a dictionary.");
                if (borderStyle.TryGetValue(Name("W"), out PdfObject? width)
                    && (!TryFiniteNumber(Resolve(width), out double widthValue)
                        || widthValue < 0))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /BS /W value is not nonnegative.");
                if (borderStyle.TryGetValue(Name("S"), out PdfObject? style))
                {
                    string styleName = (Resolve(style) as PdfName)?.ValueAsLatin1()
                        ?? throw new InvalidOperationException(
                            $"Page {page.Index + 1} annotation /BS /S value is not a name.");
                    if (styleName is not ("S" or "D" or "B" or "I" or "U"))
                        throw new InvalidOperationException(
                            $"Page {page.Index + 1} annotation /BS /S value /{styleName} is not defined.");
                }
                if (borderStyle.TryGetValue(Name("D"), out PdfObject? dashValue))
                {
                    PdfArray dash = Resolve(dashValue) as PdfArray
                        ?? throw new InvalidOperationException(
                            $"Page {page.Index + 1} annotation /BS /D value is not an array.");
                    if (dash.Any(item => !TryFiniteNumber(Resolve(item), out double number)
                            || number < 0)
                        || dash.Count > 0 && dash.All(item =>
                            TryFiniteNumber(Resolve(item), out double number) && number == 0))
                        throw new InvalidOperationException(
                            $"Page {page.Index + 1} annotation /BS /D dash array is invalid.");
                }
            }
            if (annotation.TryGetValue(Name("QuadPoints"), out PdfObject? quadrilateralValue))
            {
                PdfArray points = Resolve(quadrilateralValue) as PdfArray
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /QuadPoints value is not an array.");
                if (points.Count == 0 || points.Count % 8 != 0
                    || points.Any(item => !TryFiniteNumber(Resolve(item), out _)))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /QuadPoints value is not a nonempty sequence of numeric quadrilaterals.");
            }
            if (annotation.TryGetValue(Name("StructParent"), out PdfObject? structureParent)
                && (Resolve(structureParent) is not PdfInteger parentKey
                    || parentKey.Value < 0))
                throw new InvalidOperationException(
                    $"Page {page.Index + 1} annotation /StructParent value is not a nonnegative integer.");
            PdfName? appearanceState = null;
            if (annotation.TryGetValue(Name("AS"), out PdfObject? stateValue))
                appearanceState = Resolve(stateValue) as PdfName
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /AS value is not a name.");
            if (annotation.TryGetValue(Name("AP"), out PdfObject? appearanceValue))
            {
                PdfDictionary appearances = Resolve(appearanceValue) as PdfDictionary
                    ?? throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /AP value is not a dictionary.");
                if (!appearances.TryGetValue(Name("N"), out PdfObject? normalValue))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /AP dictionary has no normal /N appearance.");
                ValidateAppearance(normalValue, "normal /N");
                foreach (string key in new[] { "R", "D" })
                    if (appearances.TryGetValue(Name(key), out PdfObject? optionalAppearance))
                        ValidateAppearance(optionalAppearance, $"/{key}");
                if (appearanceState is not null
                    && Resolve(normalValue) is PdfDictionary normalStates
                    && !normalStates.ContainsKey(appearanceState))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation /AS value has no matching normal appearance state.");
            }

            void ValidateAppearance(PdfObject appearance, string description)
            {
                PdfObject resolvedAppearance = Resolve(appearance);
                if (resolvedAppearance is PdfStream stream)
                {
                    ValidateAppearanceStream(stream, description);
                    return;
                }
                if (resolvedAppearance is not PdfDictionary states || states.Count == 0
                    || states.Any(entry => Resolve(entry.Value) is not PdfStream))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation {description} appearance is not a stream or nonempty state dictionary of streams.");
                foreach (var entry in states)
                    ValidateAppearanceStream((PdfStream)Resolve(entry.Value),
                        $"{description} /{entry.Key.ValueAsLatin1()}");
            }

            void ValidateAppearanceStream(PdfStream stream, string description)
            {
                PdfDictionary dictionary = stream.Dictionary;
                if (dictionary.TryGetValue(Name("Type"), out PdfObject? appearanceType)
                    && (Resolve(appearanceType) is not PdfName appearanceTypeName
                        || appearanceTypeName.ValueAsLatin1() != "XObject"))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation {description} appearance has an invalid /Type value.");
                if (!dictionary.TryGetValue(Name("Subtype"), out PdfObject? appearanceSubtype)
                    || Resolve(appearanceSubtype) is not PdfName appearanceSubtypeName
                    || appearanceSubtypeName.ValueAsLatin1() != "Form")
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation {description} appearance has no /Subtype /Form value.");
                if (!dictionary.TryGetValue(Name("BBox"), out PdfObject? boundsValue)
                    || Resolve(boundsValue) is not PdfArray bounds || bounds.Count != 4
                    || bounds.Any(item => !TryFiniteNumber(Resolve(item), out _)))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation {description} appearance has no finite four-number /BBox.");
                if (dictionary.TryGetValue(Name("Matrix"), out PdfObject? matrixValue)
                    && (Resolve(matrixValue) is not PdfArray matrix || matrix.Count != 6
                        || matrix.Any(item => !TryFiniteNumber(Resolve(item), out _))))
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation {description} appearance has no finite six-number /Matrix.");
                if (dictionary.TryGetValue(Name("Resources"), out PdfObject? resources)
                    && Resolve(resources) is not PdfDictionary)
                    throw new InvalidOperationException(
                        $"Page {page.Index + 1} annotation {description} appearance /Resources value is not a dictionary.");
            }

            static bool TryFiniteNumber(PdfObject item, out double number)
            {
                number = item switch
                {
                    PdfInteger integer => integer.Value,
                    PdfReal real => real.Value,
                    _ => double.NaN
                };
                return double.IsFinite(number);
            }
        }
    }

    private static PdfDictionary TextNoteDictionary(
        PendingTextNote note, PdfIndirectReference page, PdfIndirectReference annotation,
        PdfIndirectReference appearance) =>
        Dictionary(
            ("Type", Name("Annot")), ("Subtype", Name("Text")),
            ("Rect", Rectangle(note.X, note.Y, note.Size, note.Size)),
            ("P", page), ("F", new PdfInteger(4)),
            ("Contents", UnicodeString(note.Contents)),
            ("NM", Latin1String($"KillerPDF-Note-{annotation.ObjectNumber}")),
            ("Name", Name("Note")), ("Open", new PdfBoolean(note.Open)),
            ("C", ColorArray(note.Color)),
            ("AP", Dictionary(("N", appearance))));

    private static PdfStream TextNoteAppearance(PendingTextNote note)
    {
        using var output = new MemoryStream();
        WriteAscii(output,
            $"q\n{ColorOperands(note.Color)} rg\n0 0 {Format(note.Size)} {Format(note.Size)} re\nf\n" +
            $"0 G\n1 w\n0.5 0.5 {Format(Math.Max(0, note.Size - 1))} {Format(Math.Max(0, note.Size - 1))} re\nS\n");
        double fold = note.Size * 0.3;
        WriteAscii(output,
            $"{Format(note.Size - fold)} {Format(note.Size)} m\n" +
            $"{Format(note.Size - fold)} {Format(note.Size - fold)} l\n" +
            $"{Format(note.Size)} {Format(note.Size - fold)} l\nS\n" +
            $"{Format(note.Size * 0.22)} {Format(note.Size * 0.58)} m\n" +
            $"{Format(note.Size * 0.7)} {Format(note.Size * 0.58)} l\n" +
            $"{Format(note.Size * 0.22)} {Format(note.Size * 0.38)} m\n" +
            $"{Format(note.Size * 0.62)} {Format(note.Size * 0.38)} l\nS\nQ\n");
        return Appearance(note.Size, note.Size, Dictionary(), output.ToArray());
    }

    private static PdfDictionary TextMarkupDictionary(
        PendingTextMarkup markup, PdfIndirectReference page, PdfIndirectReference annotation,
        PdfIndirectReference appearance)
    {
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Annot")), ("Subtype", Name(markup.Type.ToString())),
            ("Rect", Rectangle(markup.X, markup.Y, markup.Width, markup.Height)),
            ("QuadPoints", new PdfArray([
                Number(markup.X), Number(markup.Y + markup.Height),
                Number(markup.X + markup.Width), Number(markup.Y + markup.Height),
                Number(markup.X), Number(markup.Y),
                Number(markup.X + markup.Width), Number(markup.Y)])),
            ("P", page), ("F", new PdfInteger(4)),
            ("NM", Latin1String($"KillerPDF-{markup.Type}-{annotation.ObjectNumber}")),
            ("C", ColorArray(markup.Color)), ("CA", Number(markup.Opacity)),
            ("AP", Dictionary(("N", appearance)))
        };
        if (!string.IsNullOrEmpty(markup.Contents))
            entries.Add(("Contents", UnicodeString(markup.Contents)));
        return Dictionary(entries.ToArray());
    }

    private static PdfStream TextMarkupAppearance(PendingTextMarkup markup)
    {
        PdfDictionary graphicsState = Dictionary(
            ("Type", Name("ExtGState")), ("ca", Number(markup.Opacity)),
            ("CA", Number(markup.Opacity)), ("BM", Name("Multiply")));
        PdfDictionary resources = Dictionary(("ExtGState", new PdfDictionary([
            new KeyValuePair<PdfName, PdfObject>(Name("GS1"), graphicsState)])));
        string drawing = markup.Type switch
        {
            PdfTextMarkupType.Highlight =>
                $"{ColorOperands(markup.Color)} rg\n0 0 {Format(markup.Width)} {Format(markup.Height)} re\nf\n",
            PdfTextMarkupType.Underline => MarkupLine(markup, markup.Height * 0.08),
            PdfTextMarkupType.StrikeOut => MarkupLine(markup, markup.Height * 0.48),
            PdfTextMarkupType.Squiggly => SquigglyLine(markup),
            _ => throw new ArgumentOutOfRangeException(nameof(markup.Type))
        };
        byte[] content = Encoding.ASCII.GetBytes($"q\n/GS1 gs\n{drawing}Q\n");
        return Appearance(markup.Width, markup.Height, resources, content);
    }

    private static string MarkupLine(PendingTextMarkup markup, double y) =>
        $"{ColorOperands(markup.Color)} RG\n{Format(Math.Max(0.75, markup.Height * 0.07))} w\n" +
        $"0 {Format(y)} m\n{Format(markup.Width)} {Format(y)} l\nS\n";

    private static string SquigglyLine(PendingTextMarkup markup)
    {
        double amplitude = Math.Max(0.75, markup.Height * 0.1);
        double step = Math.Max(1.5, amplitude * 2);
        var output = new StringBuilder(
            $"{ColorOperands(markup.Color)} RG\n{Format(Math.Max(0.75, amplitude * 0.55))} w\n0 {Format(amplitude)} m\n");
        bool high = false;
        for (double x = step; x < markup.Width; x += step)
        {
            output.Append(Format(x)).Append(' ')
                .Append(Format(high ? amplitude * 2 : 0)).Append(" l\n");
            high = !high;
        }
        output.Append(Format(markup.Width)).Append(' ')
            .Append(Format(high ? amplitude * 2 : 0)).Append(" l\nS\n");
        return output.ToString();
    }

    private static PdfDictionary FreeTextDictionary(
        PendingFreeText value, PdfIndirectReference page, PdfIndirectReference annotation,
        PdfIndirectReference appearance, PdfName fontResource)
    {
        var entries = CommonEntries("FreeText", value.X, value.Y, value.Width, value.Height,
            page, annotation, appearance, value.BorderColor, value.Opacity, value.Contents);
        entries.Add(("DA", Latin1String(
            $"{NameToken(fontResource)} {Format(value.FontSize)} Tf {ColorOperands(value.TextColor)} rg")));
        entries.Add(("Q", new PdfInteger(0)));
        entries.Add(("BS", BorderStyle(value.BorderWidth)));
        if (value.FillColor.HasValue) entries.Add(("IC", ColorArray(value.FillColor.Value)));
        return Dictionary(entries.ToArray());
    }

    private static PdfStream FreeTextAppearance(
        PendingFreeText value, PdfName fontResource, PdfIndirectReference type0Reference,
        EmbeddedFontUsage usage)
    {
        PdfDictionary resources = OpacityResources(value.Opacity,
            (fontResource, type0Reference));
        using var output = new MemoryStream();
        WriteAscii(output, "q\n/GS1 gs\n");
        WriteBox(output, value.Width, value.Height, value.BorderWidth,
            value.BorderColor, value.FillColor, ellipse: false);
        WriteFreeText(output, value, fontResource, usage);
        output.Write("Q\n"u8);
        return Appearance(value.Width, value.Height, resources, output.ToArray());
    }

    private static PdfDictionary LineDictionary(
        PendingLine line, PdfIndirectReference page, PdfIndirectReference annotation,
        PdfIndirectReference appearance)
    {
        Bounds bounds = PointBounds([line.Start, line.End], line.LineWidth / 2);
        var entries = CommonEntries("Line", bounds.X, bounds.Y, bounds.Width, bounds.Height,
            page, annotation, appearance, line.Color, line.Opacity, line.Contents);
        entries.Add(("L", new PdfArray([
            Number(line.Start.X), Number(line.Start.Y), Number(line.End.X), Number(line.End.Y)])));
        entries.Add(("LE", new PdfArray([Name("None"), Name("None")])));
        entries.Add(("BS", BorderStyle(line.LineWidth)));
        return Dictionary(entries.ToArray());
    }

    private static PdfStream LineAppearance(PendingLine line)
    {
        Bounds bounds = PointBounds([line.Start, line.End], line.LineWidth / 2);
        byte[] content = Encoding.ASCII.GetBytes(
            $"q\n/GS1 gs\n{ColorOperands(line.Color)} RG\n{Format(line.LineWidth)} w\n" +
            $"{Format(line.Start.X - bounds.X)} {Format(line.Start.Y - bounds.Y)} m\n" +
            $"{Format(line.End.X - bounds.X)} {Format(line.End.Y - bounds.Y)} l\nS\nQ\n");
        return Appearance(bounds.Width, bounds.Height, OpacityResources(line.Opacity), content);
    }

    private static PdfDictionary ShapeDictionary(
        PendingShape shape, PdfIndirectReference page, PdfIndirectReference annotation,
        PdfIndirectReference appearance)
    {
        string subtype = shape.Type.ToString();
        var entries = CommonEntries(subtype, shape.X, shape.Y, shape.Width, shape.Height,
            page, annotation, appearance, shape.StrokeColor, shape.Opacity, shape.Contents);
        entries.Add(("BS", BorderStyle(shape.LineWidth)));
        if (shape.FillColor.HasValue) entries.Add(("IC", ColorArray(shape.FillColor.Value)));
        return Dictionary(entries.ToArray());
    }

    private static PdfStream ShapeAppearance(PendingShape shape)
    {
        using var output = new MemoryStream();
        WriteAscii(output, "q\n/GS1 gs\n");
        WriteBox(output, shape.Width, shape.Height, shape.LineWidth,
            shape.StrokeColor, shape.FillColor, shape.Type == PendingShapeType.Circle);
        output.Write("Q\n"u8);
        return Appearance(shape.Width, shape.Height, OpacityResources(shape.Opacity), output.ToArray());
    }

    private static PdfDictionary InkDictionary(
        PendingInk ink, PdfIndirectReference page, PdfIndirectReference annotation,
        PdfIndirectReference appearance)
    {
        Bounds bounds = PointBounds(ink.Strokes.SelectMany(stroke => stroke), ink.LineWidth / 2);
        var entries = CommonEntries("Ink", bounds.X, bounds.Y, bounds.Width, bounds.Height,
            page, annotation, appearance, ink.Color, ink.Opacity, ink.Contents);
        entries.Add(("InkList", new PdfArray(ink.Strokes.Select(stroke =>
            (PdfObject)new PdfArray(stroke.SelectMany(point => new PdfObject[]
                { Number(point.X), Number(point.Y) }))))));
        entries.Add(("BS", BorderStyle(ink.LineWidth)));
        return Dictionary(entries.ToArray());
    }

    private static PdfStream InkAppearance(PendingInk ink)
    {
        Bounds bounds = PointBounds(ink.Strokes.SelectMany(stroke => stroke), ink.LineWidth / 2);
        using var output = new MemoryStream();
        WriteAscii(output,
            $"q\n/GS1 gs\n{ColorOperands(ink.Color)} RG\n{Format(ink.LineWidth)} w\n1 J\n1 j\n");
        foreach (IReadOnlyList<PdfPoint> stroke in ink.Strokes)
        {
            WriteAscii(output,
                $"{Format(stroke[0].X - bounds.X)} {Format(stroke[0].Y - bounds.Y)} m\n");
            foreach (PdfPoint point in stroke.Skip(1))
                WriteAscii(output, $"{Format(point.X - bounds.X)} {Format(point.Y - bounds.Y)} l\n");
            output.Write("S\n"u8);
        }
        output.Write("Q\n"u8);
        return Appearance(bounds.Width, bounds.Height, OpacityResources(ink.Opacity), output.ToArray());
    }

    private static PdfDictionary ImageStampDictionary(
        PendingImageStamp stamp, PdfIndirectReference page, PdfIndirectReference annotation,
        PdfIndirectReference appearance)
    {
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Annot")), ("Subtype", Name("Stamp")),
            ("Rect", Rectangle(stamp.X, stamp.Y, stamp.Width, stamp.Height)),
            ("P", page), ("F", new PdfInteger(4)),
            ("NM", Latin1String($"KillerPDF-Image-{annotation.ObjectNumber}")),
            ("Name", Name("Image")), ("AP", Dictionary(("N", appearance)))
        };
        if (!string.IsNullOrEmpty(stamp.Contents))
            entries.Add(("Contents", UnicodeString(stamp.Contents)));
        return Dictionary(entries.ToArray());
    }

    private static PdfStream ImageStampAppearance(
        PendingImageStamp stamp, PdfIndirectReference imageReference)
    {
        PdfDictionary resources = Dictionary(("XObject", new PdfDictionary([
            new KeyValuePair<PdfName, PdfObject>(Name("Im1"), imageReference)])));
        byte[] content = Encoding.ASCII.GetBytes(
            $"q\n{Format(stamp.Width)} 0 0 {Format(stamp.Height)} 0 0 cm\n/Im1 Do\nQ\n");
        return Appearance(stamp.Width, stamp.Height, resources, content);
    }

    private static List<(string Name, PdfObject Value)> CommonEntries(
        string subtype, double x, double y, double width, double height,
        PdfIndirectReference page, PdfIndirectReference annotation, PdfIndirectReference appearance,
        PdfRgbColor color, double opacity, string? contents)
    {
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Annot")), ("Subtype", Name(subtype)),
            ("Rect", Rectangle(x, y, width, height)), ("P", page),
            ("F", new PdfInteger(4)),
            ("NM", Latin1String($"KillerPDF-{subtype}-{annotation.ObjectNumber}")),
            ("C", ColorArray(color)), ("CA", Number(opacity)),
            ("AP", Dictionary(("N", appearance)))
        };
        if (!string.IsNullOrEmpty(contents)) entries.Add(("Contents", UnicodeString(contents)));
        return entries;
    }

    private static PdfDictionary BorderStyle(double width) =>
        Dictionary(("W", Number(width)), ("S", Name("S")));

    private static PdfDictionary OpacityResources(
        double opacity, (PdfName Name, PdfObject Reference)? font = null)
    {
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("ExtGState", new PdfDictionary([
                new KeyValuePair<PdfName, PdfObject>(Name("GS1"), Dictionary(
                    ("Type", Name("ExtGState")), ("ca", Number(opacity)), ("CA", Number(opacity))))]))
        };
        if (font.HasValue)
            entries.Add(("Font", new PdfDictionary([
                new KeyValuePair<PdfName, PdfObject>(font.Value.Name, font.Value.Reference)])));
        return Dictionary(entries.ToArray());
    }

    private static void WriteBox(
        Stream output, double width, double height, double lineWidth,
        PdfRgbColor stroke, PdfRgbColor? fill, bool ellipse)
    {
        double inset = lineWidth / 2;
        if (fill.HasValue) WriteAscii(output, $"{ColorOperands(fill.Value)} rg\n");
        WriteAscii(output, $"{ColorOperands(stroke)} RG\n{Format(lineWidth)} w\n");
        if (ellipse)
            WriteEllipse(output, inset, inset, Math.Max(0, width - lineWidth), Math.Max(0, height - lineWidth));
        else
            WriteAscii(output,
                $"{Format(inset)} {Format(inset)} {Format(Math.Max(0, width - lineWidth))} {Format(Math.Max(0, height - lineWidth))} re\n");
        output.Write(fill.HasValue ? "B\n"u8 : "S\n"u8);
    }

    private static void WriteEllipse(Stream output, double x, double y, double width, double height)
    {
        const double kappa = 0.5522847498307936;
        double rx = width / 2, ry = height / 2, cx = x + rx, cy = y + ry;
        WriteAscii(output, $"{Format(cx + rx)} {Format(cy)} m\n");
        WriteAscii(output, $"{Format(cx + rx)} {Format(cy + ry * kappa)} {Format(cx + rx * kappa)} {Format(cy + ry)} {Format(cx)} {Format(cy + ry)} c\n");
        WriteAscii(output, $"{Format(cx - rx * kappa)} {Format(cy + ry)} {Format(cx - rx)} {Format(cy + ry * kappa)} {Format(cx - rx)} {Format(cy)} c\n");
        WriteAscii(output, $"{Format(cx - rx)} {Format(cy - ry * kappa)} {Format(cx - rx * kappa)} {Format(cy - ry)} {Format(cx)} {Format(cy - ry)} c\n");
        WriteAscii(output, $"{Format(cx + rx * kappa)} {Format(cy - ry)} {Format(cx + rx)} {Format(cy - ry * kappa)} {Format(cx + rx)} {Format(cy)} c\nh\n");
    }

    private static void WriteFreeText(
        Stream output, PendingFreeText value, PdfName fontResource, EmbeddedFontUsage usage)
    {
        double padding = Math.Max(3, value.BorderWidth + 2);
        double lineHeight = value.FontSize * 1.2;
        IReadOnlyList<string> lines = WrapText(value.Contents, value.Font, value.FontSize,
            Math.Max(1, value.Width - padding * 2));
        WriteAscii(output,
            $"BT\n{NameToken(fontResource)} {Format(value.FontSize)} Tf\n{ColorOperands(value.TextColor)} rg\n" +
            $"{Format(padding)} {Format(Math.Max(padding, value.Height - padding - value.FontSize))} Td\n");
        for (int index = 0; index < lines.Count; index++)
        {
            if (index > 0) WriteAscii(output, $"0 -{Format(lineHeight)} Td\n");
            WriteGlyphText(output, lines[index], value.Font, usage);
            if ((index + 2) * lineHeight > value.Height - padding) break;
        }
        output.Write("ET\n"u8);
    }

    private static IReadOnlyList<string> WrapText(
        string text, TrueTypeFont font, double fontSize, double maximumWidth)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { lines.Add(string.Empty); continue; }
            var current = new StringBuilder();
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : $"{current} {word}";
                if (current.Length > 0 && TextWidth(candidate, font, fontSize) > maximumWidth)
                {
                    lines.Add(current.ToString()); current.Clear(); current.Append(word);
                }
                else
                {
                    if (current.Length > 0) current.Append(' ');
                    current.Append(word);
                }
            }
            lines.Add(current.ToString());
        }
        return lines;
    }

    private static double TextWidth(string value, TrueTypeFont font, double fontSize) =>
        font.MapText(value).Sum(mapping => font.GetPdfAdvanceWidth(mapping.Glyph))
            * fontSize / 1000;

    private static void WriteGlyphText(
        Stream output, string value, TrueTypeFont font, EmbeddedFontUsage usage)
    {
        output.WriteByte((byte)'<');
        foreach (FontGlyphMapping mapping in font.MapText(value))
            WriteAscii(output, usage.AddMapping(mapping.Glyph, mapping.UnicodeSequence)
                .ToString("X4", CultureInfo.InvariantCulture));
        output.Write("> Tj\n"u8);
    }

    private static Bounds PointBounds(IEnumerable<PdfPoint> points, double padding)
    {
        PdfPoint[] values = points.ToArray();
        double minX = values.Min(point => point.X) - padding;
        double minY = values.Min(point => point.Y) - padding;
        double maxX = values.Max(point => point.X) + padding;
        double maxY = values.Max(point => point.Y) + padding;
        return new Bounds(minX, minY, maxX - minX, maxY - minY);
    }

    private static PdfStream Appearance(
        double width, double height, PdfDictionary resources, byte[] content) =>
        new(Dictionary(
            ("Type", Name("XObject")), ("Subtype", Name("Form")),
            ("FormType", new PdfInteger(1)),
            ("BBox", new PdfArray([new PdfInteger(0), new PdfInteger(0), Number(width), Number(height)])),
            ("Resources", resources)), content);

    private void ValidatePage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
    }

    private static void ValidateCoordinate(double value, string parameterName)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateRectangle(double x, double y, double width, double height)
    {
        ValidateCoordinate(x, nameof(x)); ValidateCoordinate(y, nameof(y));
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    }

    private static void ValidateStroke(double lineWidth, double opacity)
    {
        if (!double.IsFinite(lineWidth) || lineWidth <= 0) throw new ArgumentOutOfRangeException(nameof(lineWidth));
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
    }

    private static void ValidateDrawableText(TrueTypeFont font, string value, string parameterName)
    {
        if (!font.EmbeddingAllowed)
            throw new ArgumentException($"Font {font.PostScriptName} prohibits PDF embedding.", parameterName);
        foreach (FontGlyphMapping mapping in font.MapText(value))
        {
            if (mapping.UnicodeSequence is "\r" or "\n") continue;
            if (mapping.Glyph == 0 && mapping.UnicodeSequence != "\0")
                throw new ArgumentException(
                    $"Font {font.PostScriptName} has no glyph for {FormatUnicodeSequence(mapping.UnicodeSequence)}.", parameterName);
        }
    }

    private static string FormatUnicodeSequence(string value) =>
        string.Join(" ", value.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"));

    private static PdfArray Rectangle(double x, double y, double width, double height) =>
        new([Number(x), Number(y), Number(x + width), Number(y + height)]);
    private static PdfArray ColorArray(PdfRgbColor color) =>
        new([Number(color.Red), Number(color.Green), Number(color.Blue)]);
    private static string ColorOperands(PdfRgbColor color) =>
        $"{Format(color.Red)} {Format(color.Green)} {Format(color.Blue)}";
    private static PdfObject Number(double value) => value == Math.Truncate(value)
        ? new PdfInteger(checked((long)value)) : new PdfReal(value);
    private static string Format(double value) =>
        Encoding.ASCII.GetString(PdfObjectWriter.Write(Number(value)));
    private static string NameToken(PdfName value) =>
        Encoding.ASCII.GetString(PdfObjectWriter.Write(value));
    private static PdfString Latin1String(string value) =>
        new(Encoding.Latin1.GetBytes(value), PdfStringForm.Literal);
    private static PdfString UnicodeString(string value) =>
        new([0xFE, 0xFF, .. PdfUnicodeEncoding.EncodeBigEndian(value)],
            PdfStringForm.Hexadecimal);
    private static PdfDictionary Dictionary(params (string Name, PdfObject Value)[] entries) =>
        new(entries.Select(entry => new KeyValuePair<PdfName, PdfObject>(Name(entry.Name), entry.Value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
    private static void WriteAscii(Stream output, string value)
    {
        foreach (char character in value) output.WriteByte(checked((byte)character));
    }

    private abstract record PendingAnnotation(int PageIndex);
    private sealed record PendingTextNote(
        int PageIndex, double X, double Y, double Size, string Contents, PdfRgbColor Color, bool Open)
        : PendingAnnotation(PageIndex);
    private sealed record PendingTextMarkup(
        PdfTextMarkupType Type, int PageIndex, double X, double Y, double Width, double Height,
        string? Contents, PdfRgbColor Color, double Opacity) : PendingAnnotation(PageIndex);
    private sealed record PendingFreeText(
        int PageIndex, double X, double Y, double Width, double Height, string Contents,
        TrueTypeFont Font, double FontSize, PdfRgbColor TextColor, PdfRgbColor? FillColor,
        PdfRgbColor BorderColor, double BorderWidth, double Opacity) : PendingAnnotation(PageIndex);
    private sealed record PendingLine(
        int PageIndex, PdfPoint Start, PdfPoint End, PdfRgbColor Color,
        double LineWidth, double Opacity, string? Contents) : PendingAnnotation(PageIndex);
    private sealed record PendingShape(
        PendingShapeType Type, int PageIndex, double X, double Y, double Width, double Height,
        PdfRgbColor StrokeColor, PdfRgbColor? FillColor, double LineWidth, double Opacity,
        string? Contents) : PendingAnnotation(PageIndex);
    private sealed record PendingInk(
        int PageIndex, IReadOnlyList<IReadOnlyList<PdfPoint>> Strokes, PdfRgbColor Color,
        double LineWidth, double Opacity, string? Contents) : PendingAnnotation(PageIndex);
    private sealed record PendingImageStamp(
        int PageIndex, double X, double Y, double Width, double Height,
        PdfImage Image, string? Contents) : PendingAnnotation(PageIndex);
    private sealed record AllocatedAnnotation(
        PendingAnnotation Definition, PdfIndirectReference AnnotationReference,
        PdfIndirectReference AppearanceReference);
    private sealed record EditorFontBinding(
        PdfName Resource, PdfIndirectReference Type0Reference, EmbeddedFontUsage Usage);
    private sealed record Bounds(double X, double Y, double Width, double Height);
    private enum PendingShapeType { Square, Circle }
}
