using System.Globalization;
using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Editing;

/// <summary>
/// Adds annotations to existing pages through a byte-preserving incremental revision.
/// Original page contents and every source byte remain untouched.
/// </summary>
public sealed class PdfIncrementalAnnotationEditor
{
    private static readonly PdfName RootName = new("Root"u8);
    private static readonly PdfName PagesName = new("Pages"u8);
    private static readonly PdfName KidsName = new("Kids"u8);
    private static readonly PdfName TypeName = new("Type"u8);
    private static readonly PdfName PageName = new("Page"u8);
    private static readonly PdfName AnnotsName = new("Annots"u8);
    private const int MaximumPageTreeDepth = 256;
    private const int MaximumPageCount = 1_000_000;

    private readonly PdfDocument _document;
    private readonly IReadOnlyList<PageEntry> _pages;
    private readonly List<PendingAnnotation> _annotations = [];

    public PdfIncrementalAnnotationEditor(PdfDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _pages = ReadPages(document);
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

    public byte[] Build()
    {
        if (_annotations.Count == 0)
            throw new InvalidOperationException("The incremental annotation update is empty.");
        var update = new PdfIncrementalUpdateBuilder(_document);
        var allocated = _annotations.Select(annotation => new AllocatedAnnotation(
            annotation, update.ReserveObject(), update.ReserveObject())).ToArray();
        Dictionary<TrueTypeFont, EditorFontBinding> fonts = AllocateFonts(update);
        Dictionary<PdfImage, PdfIndirectReference> images = AllocateImages(update);

        foreach (AllocatedAnnotation item in allocated)
        {
            PageEntry page = _pages[item.Definition.PageIndex];
            switch (item.Definition)
            {
                case PendingTextNote note:
                    update.SetObject(item.AnnotationReference,
                        TextNoteDictionary(note, page.Reference, item.AnnotationReference, item.AppearanceReference));
                    update.SetObject(item.AppearanceReference, TextNoteAppearance(note));
                    break;
                case PendingTextMarkup markup:
                    update.SetObject(item.AnnotationReference,
                        TextMarkupDictionary(markup, page.Reference, item.AnnotationReference, item.AppearanceReference));
                    update.SetObject(item.AppearanceReference, TextMarkupAppearance(markup));
                    break;
                case PendingFreeText freeText:
                    EditorFontBinding binding = fonts[freeText.Font];
                    update.SetObject(item.AnnotationReference,
                        FreeTextDictionary(freeText, page.Reference, item.AnnotationReference,
                            item.AppearanceReference, binding.Resource));
                    update.SetObject(item.AppearanceReference,
                        FreeTextAppearance(freeText, binding.Resource, binding.Type0Reference));
                    break;
                case PendingLine line:
                    update.SetObject(item.AnnotationReference,
                        LineDictionary(line, page.Reference, item.AnnotationReference, item.AppearanceReference));
                    update.SetObject(item.AppearanceReference, LineAppearance(line));
                    break;
                case PendingShape shape:
                    update.SetObject(item.AnnotationReference,
                        ShapeDictionary(shape, page.Reference, item.AnnotationReference, item.AppearanceReference));
                    update.SetObject(item.AppearanceReference, ShapeAppearance(shape));
                    break;
                case PendingInk ink:
                    update.SetObject(item.AnnotationReference,
                        InkDictionary(ink, page.Reference, item.AnnotationReference, item.AppearanceReference));
                    update.SetObject(item.AppearanceReference, InkAppearance(ink));
                    break;
                case PendingImageStamp stamp:
                    update.SetObject(item.AnnotationReference,
                        ImageStampDictionary(stamp, page.Reference, item.AnnotationReference,
                            item.AppearanceReference));
                    update.SetObject(item.AppearanceReference,
                        ImageStampAppearance(stamp, images[stamp.Image]));
                    break;
                default:
                    throw new InvalidOperationException("Unknown annotation definition.");
            }
        }

        foreach (IGrouping<int, AllocatedAnnotation> group in allocated.GroupBy(item => item.Definition.PageIndex))
            AppendPageAnnotations(update, _pages[group.Key], group.Select(item => item.AnnotationReference));
        return update.Build();
    }

    private Dictionary<TrueTypeFont, EditorFontBinding> AllocateFonts(PdfIncrementalUpdateBuilder update)
    {
        var result = new Dictionary<TrueTypeFont, EditorFontBinding>();
        int sequence = 0;
        foreach (IGrouping<TrueTypeFont, PendingFreeText> group in
            _annotations.OfType<PendingFreeText>().GroupBy(value => value.Font))
        {
            var mappings = new SortedDictionary<ushort, int>();
            foreach (Rune rune in group.SelectMany(value => value.Contents.EnumerateRunes()))
            {
                if (rune.Value is '\r' or '\n') continue;
                ushort glyph = group.Key.GetGlyphId(rune.Value);
                if (mappings.TryGetValue(glyph, out int existing) && existing != rune.Value)
                    throw new InvalidOperationException($"Glyph {glyph} has conflicting Unicode mappings.");
                mappings[glyph] = rune.Value;
            }
            PdfIndirectReference type0 = update.ReserveObject();
            PdfIndirectReference cidFont = update.ReserveObject();
            PdfIndirectReference descriptor = update.ReserveObject();
            PdfIndirectReference fontFile = update.ReserveObject();
            PdfIndirectReference toUnicode = update.ReserveObject();
            EmbeddedTrueTypeFontObjects values = PdfEmbeddedTrueTypeFontFactory.Create(
                group.Key, mappings, type0, cidFont, descriptor, fontFile, toUnicode);
            update.SetObject(type0, values.Type0).SetObject(cidFont, values.CidFont)
                .SetObject(descriptor, values.Descriptor).SetObject(fontFile, values.FontFile)
                .SetObject(toUnicode, values.ToUnicode);
            result.Add(group.Key, new EditorFontBinding(
                new PdfName(Encoding.ASCII.GetBytes($"KpF{++sequence}")), type0));
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
        PdfIncrementalUpdateBuilder update, PageEntry page,
        IEnumerable<PdfIndirectReference> additions)
    {
        var values = new List<PdfObject>();
        if (page.Dictionary.TryGetValue(AnnotsName, out PdfObject existing))
        {
            PdfArray array;
            if (existing is PdfIndirectReference reference)
            {
                array = _document.Resolve(reference) as PdfArray
                    ?? throw new InvalidOperationException($"Page {page.Index + 1} /Annots reference is not an array.");
                values.AddRange(array);
                values.AddRange(additions);
                update.ReplaceObject(reference.ObjectNumber, new PdfArray(values));
                return;
            }
            array = existing as PdfArray
                ?? throw new InvalidOperationException($"Page {page.Index + 1} /Annots value is not an array.");
            values.AddRange(array);
        }
        values.AddRange(additions);
        var replacement = new PdfDictionary(page.Dictionary
            .Where(entry => !entry.Key.Equals(AnnotsName))
            .Append(new KeyValuePair<PdfName, PdfObject>(AnnotsName, new PdfArray(values))));
        update.ReplaceObject(page.Reference.ObjectNumber, replacement);
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
        PendingFreeText value, PdfName fontResource, PdfIndirectReference type0Reference)
    {
        PdfDictionary resources = OpacityResources(value.Opacity,
            (fontResource, type0Reference));
        using var output = new MemoryStream();
        WriteAscii(output, "q\n/GS1 gs\n");
        WriteBox(output, value.Width, value.Height, value.BorderWidth,
            value.BorderColor, value.FillColor, ellipse: false);
        WriteFreeText(output, value, fontResource);
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

    private static void WriteFreeText(Stream output, PendingFreeText value, PdfName fontResource)
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
            WriteGlyphText(output, lines[index], value.Font);
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
        value.EnumerateRunes().Sum(rune => font.GetPdfAdvanceWidth(font.GetGlyphId(rune.Value)))
            * fontSize / 1000;

    private static void WriteGlyphText(Stream output, string value, TrueTypeFont font)
    {
        output.WriteByte((byte)'<');
        foreach (Rune rune in value.EnumerateRunes())
            WriteAscii(output, font.GetGlyphId(rune.Value).ToString("X4", CultureInfo.InvariantCulture));
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

    private static IReadOnlyList<PageEntry> ReadPages(PdfDocument document)
    {
        PdfIndirectReference rootReference = document.CrossReferences.TryGetTrailerValue(RootName, out PdfObject root)
            ? root as PdfIndirectReference
                ?? throw new InvalidOperationException("The trailer /Root is not an indirect reference.")
            : throw new InvalidOperationException("The PDF trailer has no /Root.");
        PdfDictionary catalog = document.Resolve(rootReference) as PdfDictionary
            ?? throw new InvalidOperationException("The document catalog is not a dictionary.");
        PdfIndirectReference pagesReference = catalog.TryGetValue(PagesName, out PdfObject pages)
            ? pages as PdfIndirectReference
                ?? throw new InvalidOperationException("The catalog /Pages is not an indirect reference.")
            : throw new InvalidOperationException("The document catalog has no /Pages tree.");

        var result = new List<PageEntry>();
        var active = new HashSet<int>();
        Visit(pagesReference, 0);
        return result;

        void Visit(PdfIndirectReference reference, int depth)
        {
            if (depth > MaximumPageTreeDepth)
                throw new InvalidOperationException("The page tree exceeds the supported nesting depth.");
            if (!active.Add(reference.ObjectNumber))
                throw new InvalidOperationException("The page tree contains a cycle.");
            try
            {
                PdfDictionary node = document.Resolve(reference) as PdfDictionary
                    ?? throw new InvalidOperationException("A page-tree reference is not a dictionary.");
                if (node.TryGetValue(TypeName, out PdfObject type)
                    && type is PdfName typeName && typeName.Equals(PageName))
                {
                    if (result.Count >= MaximumPageCount)
                        throw new InvalidOperationException("The PDF contains too many pages.");
                    result.Add(new PageEntry(result.Count, reference, node));
                    return;
                }
                PdfArray kids = node.TryGetValue(KidsName, out PdfObject kidsValue)
                    ? kidsValue as PdfArray
                        ?? throw new InvalidOperationException("A page-tree /Kids value is not an array.")
                    : throw new InvalidOperationException("A page-tree node has neither /Type /Page nor /Kids.");
                foreach (PdfObject kid in kids)
                    Visit(kid as PdfIndirectReference
                        ?? throw new InvalidOperationException("A page-tree kid is not an indirect reference."), depth + 1);
            }
            finally
            {
                active.Remove(reference.ObjectNumber);
            }
        }
    }

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
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (rune.Value is '\r' or '\n') continue;
            if (font.GetGlyphId(rune.Value) == 0 && rune.Value != 0)
                throw new ArgumentException(
                    $"Font {font.PostScriptName} has no glyph for U+{rune.Value:X4}.", parameterName);
        }
    }

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
        new([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(value)], PdfStringForm.Hexadecimal);
    private static PdfDictionary Dictionary(params (string Name, PdfObject Value)[] entries) =>
        new(entries.Select(entry => new KeyValuePair<PdfName, PdfObject>(Name(entry.Name), entry.Value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
    private static void WriteAscii(Stream output, string value)
    {
        foreach (char character in value) output.WriteByte(checked((byte)character));
    }

    private sealed record PageEntry(int Index, PdfIndirectReference Reference, PdfDictionary Dictionary);
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
    private sealed record EditorFontBinding(PdfName Resource, PdfIndirectReference Type0Reference);
    private sealed record Bounds(double X, double Y, double Width, double Height);
    private enum PendingShapeType { Square, Circle }
}
