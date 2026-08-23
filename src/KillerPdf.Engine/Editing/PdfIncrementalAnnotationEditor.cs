using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
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
                default:
                    throw new InvalidOperationException("Unknown annotation definition.");
            }
        }

        foreach (IGrouping<int, AllocatedAnnotation> group in allocated.GroupBy(item => item.Definition.PageIndex))
            AppendPageAnnotations(update, _pages[group.Key], group.Select(item => item.AnnotationReference));
        return update.Build();
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
    private sealed record AllocatedAnnotation(
        PendingAnnotation Definition, PdfIndirectReference AnnotationReference,
        PdfIndirectReference AppearanceReference);
}
