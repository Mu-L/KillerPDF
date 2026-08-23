using System.Globalization;
using System.Text;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Authoring;

public sealed partial class PdfDocumentBuilder
{
    private readonly List<FreeTextDefinition> _freeTexts = [];
    private readonly List<VisualAnnotationDefinition> _visualAnnotations = [];

    public PdfDocumentBuilder AddFreeText(
        int pageIndex, double x, double y, double width, double height,
        string contents, TrueTypeFont font, double fontSize = 12,
        PdfRgbColor? textColor = null, PdfRgbColor? fillColor = null,
        PdfRgbColor? borderColor = null, double borderWidth = 1, double opacity = 1)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(font);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        ValidateStroke(borderWidth, opacity);
        ValidateDrawableText(font, contents, nameof(contents));
        _freeTexts.Add(new FreeTextDefinition(
            pageIndex, x, y, width, height, contents, font, fontSize,
            textColor ?? new PdfRgbColor(0, 0, 0), fillColor,
            borderColor ?? new PdfRgbColor(0, 0, 0), borderWidth, opacity));
        return this;
    }

    public PdfDocumentBuilder AddLineAnnotation(
        int pageIndex, PdfPoint start, PdfPoint end, PdfRgbColor? color = null,
        double lineWidth = 1, double opacity = 1, string? contents = null)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateStroke(lineWidth, opacity);
        if (start == end) throw new ArgumentException("A line must have two distinct endpoints.", nameof(end));
        _visualAnnotations.Add(new LineAnnotationDefinition(
            pageIndex, start, end, color ?? new PdfRgbColor(0, 0, 0), lineWidth, opacity, contents));
        return this;
    }

    public PdfDocumentBuilder AddRectangleAnnotation(
        int pageIndex, double x, double y, double width, double height,
        PdfRgbColor? strokeColor = null, PdfRgbColor? fillColor = null,
        double lineWidth = 1, double opacity = 1, string? contents = null)
        => AddShapeAnnotation(PdfShapeAnnotationType.Square, pageIndex, x, y, width, height,
            strokeColor, fillColor, lineWidth, opacity, contents);

    public PdfDocumentBuilder AddEllipseAnnotation(
        int pageIndex, double x, double y, double width, double height,
        PdfRgbColor? strokeColor = null, PdfRgbColor? fillColor = null,
        double lineWidth = 1, double opacity = 1, string? contents = null)
        => AddShapeAnnotation(PdfShapeAnnotationType.Circle, pageIndex, x, y, width, height,
            strokeColor, fillColor, lineWidth, opacity, contents);

    public PdfDocumentBuilder AddInkAnnotation(
        int pageIndex, IReadOnlyList<PdfPoint> points, PdfRgbColor? color = null,
        double lineWidth = 2, double opacity = 1, string? contents = null)
        => AddInkAnnotation(pageIndex, [points], color, lineWidth, opacity, contents);

    public PdfDocumentBuilder AddInkAnnotation(
        int pageIndex, IReadOnlyList<IReadOnlyList<PdfPoint>> strokes, PdfRgbColor? color = null,
        double lineWidth = 2, double opacity = 1, string? contents = null)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(strokes);
        ValidateStroke(lineWidth, opacity);
        if (strokes.Count == 0 || strokes.Any(stroke => stroke is null || stroke.Count < 2))
            throw new ArgumentException("Ink requires at least one stroke containing two points.", nameof(strokes));
        _visualAnnotations.Add(new InkAnnotationDefinition(
            pageIndex, strokes.Select(stroke => stroke.ToArray()).ToArray(),
            color ?? new PdfRgbColor(0, 0, 0), lineWidth, opacity, contents));
        return this;
    }

    private PdfDocumentBuilder AddShapeAnnotation(
        PdfShapeAnnotationType type, int pageIndex, double x, double y, double width, double height,
        PdfRgbColor? strokeColor, PdfRgbColor? fillColor,
        double lineWidth, double opacity, string? contents)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        ValidateStroke(lineWidth, opacity);
        _visualAnnotations.Add(new ShapeAnnotationDefinition(
            type, pageIndex, x, y, width, height, strokeColor ?? new PdfRgbColor(0, 0, 0),
            fillColor, lineWidth, opacity, contents));
        return this;
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

    private static void AddFreeTextObjects(
        ICollection<PdfIndirectObject> objects, AllocatedFreeText allocated,
        IReadOnlyList<AllocatedPage> pages, int sequence, PdfName fontResource, int fontNumber)
    {
        FreeTextDefinition value = allocated.Definition;
        string defaultAppearance =
            $"{NameToken(fontResource)} {FormatNumber(value.FontSize)} Tf {ColorOperands(value.TextColor)} rg";
        var entries = CommonAnnotationEntries(
            "FreeText", value.PageIndex, value.X, value.Y, value.Width, value.Height,
            pages, $"KillerPDF-FreeText-{sequence}", value.BorderColor, value.Opacity,
            value.Contents, allocated.AppearanceNumber);
        entries.Add(("DA", Latin1String(defaultAppearance)));
        entries.Add(("Q", new PdfInteger(0)));
        entries.Add(("BS", BorderStyle(value.BorderWidth)));
        if (value.FillColor.HasValue) entries.Add(("IC", ColorArray(value.FillColor.Value)));
        objects.Add(new PdfIndirectObject(allocated.AnnotationNumber, 0, Dictionary(entries.ToArray()), 0));

        PdfDictionary resources = AnnotationResources(value.Opacity,
            (fontResource, new PdfIndirectReference(fontNumber, 0)));
        using var appearance = new MemoryStream();
        WriteAscii(appearance, $"q\n/GS1 gs\n");
        WriteBox(appearance, value.Width, value.Height, value.BorderWidth,
            value.BorderColor, value.FillColor, ellipse: false);
        WriteFreeText(appearance, value, fontResource);
        appearance.Write("Q\n"u8);
        objects.Add(new PdfIndirectObject(allocated.AppearanceNumber, 0,
            AnnotationAppearance(value.Width, value.Height, resources, appearance.ToArray()), 0));
    }

    private static void AddVisualAnnotationObjects(
        ICollection<PdfIndirectObject> objects, AllocatedVisualAnnotation allocated,
        IReadOnlyList<AllocatedPage> pages, int sequence)
    {
        switch (allocated.Definition)
        {
            case LineAnnotationDefinition line:
                AddLineObjects(objects, allocated, line, pages, sequence);
                break;
            case ShapeAnnotationDefinition shape:
                AddShapeObjects(objects, allocated, shape, pages, sequence);
                break;
            case InkAnnotationDefinition ink:
                AddInkObjects(objects, allocated, ink, pages, sequence);
                break;
            default:
                throw new InvalidOperationException("Unknown visual annotation type.");
        }
    }

    private static void AddLineObjects(
        ICollection<PdfIndirectObject> objects, AllocatedVisualAnnotation allocated,
        LineAnnotationDefinition line, IReadOnlyList<AllocatedPage> pages, int sequence)
    {
        Bounds bounds = PointBounds([line.Start, line.End], line.LineWidth / 2);
        var entries = CommonAnnotationEntries("Line", line.PageIndex,
            bounds.X, bounds.Y, bounds.Width, bounds.Height, pages,
            $"KillerPDF-Line-{sequence}", line.Color, line.Opacity,
            line.Contents, allocated.AppearanceNumber);
        entries.Add(("L", new PdfArray([
            Number(line.Start.X), Number(line.Start.Y), Number(line.End.X), Number(line.End.Y)])));
        entries.Add(("LE", new PdfArray([Name("None"), Name("None")])));
        entries.Add(("BS", BorderStyle(line.LineWidth)));
        objects.Add(new PdfIndirectObject(allocated.AnnotationNumber, 0, Dictionary(entries.ToArray()), 0));

        using var appearance = new MemoryStream();
        WriteAscii(appearance,
            $"q\n/GS1 gs\n{ColorOperands(line.Color)} RG\n{FormatNumber(line.LineWidth)} w\n" +
            $"{FormatNumber(line.Start.X - bounds.X)} {FormatNumber(line.Start.Y - bounds.Y)} m\n" +
            $"{FormatNumber(line.End.X - bounds.X)} {FormatNumber(line.End.Y - bounds.Y)} l\nS\nQ\n");
        objects.Add(new PdfIndirectObject(allocated.AppearanceNumber, 0,
            AnnotationAppearance(bounds.Width, bounds.Height, AnnotationResources(line.Opacity), appearance.ToArray()), 0));
    }

    private static void AddShapeObjects(
        ICollection<PdfIndirectObject> objects, AllocatedVisualAnnotation allocated,
        ShapeAnnotationDefinition shape, IReadOnlyList<AllocatedPage> pages, int sequence)
    {
        string subtype = shape.Type.ToString();
        var entries = CommonAnnotationEntries(subtype, shape.PageIndex,
            shape.X, shape.Y, shape.Width, shape.Height, pages,
            $"KillerPDF-{subtype}-{sequence}", shape.StrokeColor, shape.Opacity,
            shape.Contents, allocated.AppearanceNumber);
        entries.Add(("BS", BorderStyle(shape.LineWidth)));
        if (shape.FillColor.HasValue) entries.Add(("IC", ColorArray(shape.FillColor.Value)));
        objects.Add(new PdfIndirectObject(allocated.AnnotationNumber, 0, Dictionary(entries.ToArray()), 0));

        using var appearance = new MemoryStream();
        WriteAscii(appearance, "q\n/GS1 gs\n");
        WriteBox(appearance, shape.Width, shape.Height, shape.LineWidth,
            shape.StrokeColor, shape.FillColor, shape.Type == PdfShapeAnnotationType.Circle);
        appearance.Write("Q\n"u8);
        objects.Add(new PdfIndirectObject(allocated.AppearanceNumber, 0,
            AnnotationAppearance(shape.Width, shape.Height, AnnotationResources(shape.Opacity), appearance.ToArray()), 0));
    }

    private static void AddInkObjects(
        ICollection<PdfIndirectObject> objects, AllocatedVisualAnnotation allocated,
        InkAnnotationDefinition ink, IReadOnlyList<AllocatedPage> pages, int sequence)
    {
        PdfPoint[] allPoints = ink.Strokes.SelectMany(stroke => stroke).ToArray();
        Bounds bounds = PointBounds(allPoints, ink.LineWidth / 2);
        var entries = CommonAnnotationEntries("Ink", ink.PageIndex,
            bounds.X, bounds.Y, bounds.Width, bounds.Height, pages,
            $"KillerPDF-Ink-{sequence}", ink.Color, ink.Opacity,
            ink.Contents, allocated.AppearanceNumber);
        entries.Add(("InkList", new PdfArray(ink.Strokes.Select(stroke =>
            (PdfObject)new PdfArray(stroke.SelectMany(point => new PdfObject[]
                { Number(point.X), Number(point.Y) }))))));
        entries.Add(("BS", BorderStyle(ink.LineWidth)));
        objects.Add(new PdfIndirectObject(allocated.AnnotationNumber, 0, Dictionary(entries.ToArray()), 0));

        using var appearance = new MemoryStream();
        WriteAscii(appearance,
            $"q\n/GS1 gs\n{ColorOperands(ink.Color)} RG\n{FormatNumber(ink.LineWidth)} w\n1 J\n1 j\n");
        foreach (IReadOnlyList<PdfPoint> stroke in ink.Strokes)
        {
            WriteAscii(appearance,
                $"{FormatNumber(stroke[0].X - bounds.X)} {FormatNumber(stroke[0].Y - bounds.Y)} m\n");
            foreach (PdfPoint point in stroke.Skip(1))
                WriteAscii(appearance,
                    $"{FormatNumber(point.X - bounds.X)} {FormatNumber(point.Y - bounds.Y)} l\n");
            appearance.Write("S\n"u8);
        }
        appearance.Write("Q\n"u8);
        objects.Add(new PdfIndirectObject(allocated.AppearanceNumber, 0,
            AnnotationAppearance(bounds.Width, bounds.Height, AnnotationResources(ink.Opacity), appearance.ToArray()), 0));
    }

    private static List<(string Name, PdfObject Value)> CommonAnnotationEntries(
        string subtype, int pageIndex, double x, double y, double width, double height,
        IReadOnlyList<AllocatedPage> pages, string identity, PdfRgbColor color,
        double opacity, string? contents, int appearanceNumber)
    {
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Annot")),
            ("Subtype", Name(subtype)),
            ("Rect", new PdfArray([Number(x), Number(y), Number(x + width), Number(y + height)])),
            ("P", new PdfIndirectReference(pages[pageIndex].PageNumber, 0)),
            ("F", new PdfInteger(4)),
            ("NM", Latin1String(identity)),
            ("C", ColorArray(color)),
            ("CA", new PdfReal(opacity)),
            ("AP", Dictionary(("N", new PdfIndirectReference(appearanceNumber, 0))))
        };
        if (!string.IsNullOrEmpty(contents)) entries.Add(("Contents", UnicodeString(contents)));
        return entries;
    }

    private static PdfDictionary BorderStyle(double width) =>
        Dictionary(("W", Number(width)), ("S", Name("S")));

    private static PdfDictionary AnnotationResources(
        double opacity, (PdfName Name, PdfObject Reference)? font = null)
    {
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("ExtGState", new PdfDictionary([
                new KeyValuePair<PdfName, PdfObject>(Name("GS1"), Dictionary(
                    ("Type", Name("ExtGState")), ("ca", Number(opacity)), ("CA", Number(opacity))
                ))]))
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
        WriteAscii(output, $"{ColorOperands(stroke)} RG\n{FormatNumber(lineWidth)} w\n");
        if (ellipse)
            WriteEllipse(output, inset, inset, Math.Max(0, width - lineWidth), Math.Max(0, height - lineWidth));
        else
            WriteAscii(output,
                $"{FormatNumber(inset)} {FormatNumber(inset)} {FormatNumber(Math.Max(0, width - lineWidth))} {FormatNumber(Math.Max(0, height - lineWidth))} re\n");
        output.Write(fill.HasValue ? "B\n"u8 : "S\n"u8);
    }

    private static void WriteEllipse(Stream output, double x, double y, double width, double height)
    {
        const double kappa = 0.5522847498307936;
        double rx = width / 2, ry = height / 2, cx = x + rx, cy = y + ry;
        WriteAscii(output, $"{FormatNumber(cx + rx)} {FormatNumber(cy)} m\n");
        WriteAscii(output, $"{FormatNumber(cx + rx)} {FormatNumber(cy + ry * kappa)} {FormatNumber(cx + rx * kappa)} {FormatNumber(cy + ry)} {FormatNumber(cx)} {FormatNumber(cy + ry)} c\n");
        WriteAscii(output, $"{FormatNumber(cx - rx * kappa)} {FormatNumber(cy + ry)} {FormatNumber(cx - rx)} {FormatNumber(cy + ry * kappa)} {FormatNumber(cx - rx)} {FormatNumber(cy)} c\n");
        WriteAscii(output, $"{FormatNumber(cx - rx)} {FormatNumber(cy - ry * kappa)} {FormatNumber(cx - rx * kappa)} {FormatNumber(cy - ry)} {FormatNumber(cx)} {FormatNumber(cy - ry)} c\n");
        WriteAscii(output, $"{FormatNumber(cx + rx * kappa)} {FormatNumber(cy - ry)} {FormatNumber(cx + rx)} {FormatNumber(cy - ry * kappa)} {FormatNumber(cx + rx)} {FormatNumber(cy)} c\nh\n");
    }

    private static void WriteFreeText(Stream output, FreeTextDefinition value, PdfName fontResource)
    {
        double padding = Math.Max(3, value.BorderWidth + 2);
        double lineHeight = value.FontSize * 1.2;
        IReadOnlyList<string> lines = WrapText(value.Contents, value.Font, value.FontSize,
            Math.Max(1, value.Width - padding * 2));
        WriteAscii(output,
            $"BT\n{NameToken(fontResource)} {FormatNumber(value.FontSize)} Tf\n{ColorOperands(value.TextColor)} rg\n" +
            $"{FormatNumber(padding)} {FormatNumber(Math.Max(padding, value.Height - padding - value.FontSize))} Td\n");
        for (int index = 0; index < lines.Count; index++)
        {
            if (index > 0) WriteAscii(output, $"0 -{FormatNumber(lineHeight)} Td\n");
            WriteShownText(output, lines[index], value.Font);
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
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(word);
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

    private static Bounds PointBounds(IEnumerable<PdfPoint> points, double padding)
    {
        PdfPoint[] values = points.ToArray();
        double minX = values.Min(point => point.X) - padding;
        double minY = values.Min(point => point.Y) - padding;
        double maxX = values.Max(point => point.X) + padding;
        double maxY = values.Max(point => point.Y) + padding;
        return new Bounds(minX, minY, maxX - minX, maxY - minY);
    }

    private sealed record FreeTextDefinition(
        int PageIndex, double X, double Y, double Width, double Height, string Contents,
        TrueTypeFont Font, double FontSize, PdfRgbColor TextColor, PdfRgbColor? FillColor,
        PdfRgbColor BorderColor, double BorderWidth, double Opacity);
    private sealed record AllocatedFreeText(
        FreeTextDefinition Definition, int AnnotationNumber, int AppearanceNumber);
    private abstract record VisualAnnotationDefinition(
        int PageIndex, PdfRgbColor Color, double LineWidth, double Opacity, string? Contents);
    private sealed record LineAnnotationDefinition(
        int PageIndex, PdfPoint Start, PdfPoint End, PdfRgbColor Color,
        double LineWidth, double Opacity, string? Contents)
        : VisualAnnotationDefinition(PageIndex, Color, LineWidth, Opacity, Contents);
    private sealed record ShapeAnnotationDefinition(
        PdfShapeAnnotationType Type, int PageIndex, double X, double Y, double Width, double Height,
        PdfRgbColor StrokeColor, PdfRgbColor? FillColor, double LineWidth, double Opacity, string? Contents)
        : VisualAnnotationDefinition(PageIndex, StrokeColor, LineWidth, Opacity, Contents);
    private sealed record InkAnnotationDefinition(
        int PageIndex, IReadOnlyList<IReadOnlyList<PdfPoint>> Strokes, PdfRgbColor Color,
        double LineWidth, double Opacity, string? Contents)
        : VisualAnnotationDefinition(PageIndex, Color, LineWidth, Opacity, Contents);
    private sealed record AllocatedVisualAnnotation(
        VisualAnnotationDefinition Definition, int AnnotationNumber, int AppearanceNumber);
    private sealed record Bounds(double X, double Y, double Width, double Height);
    private enum PdfShapeAnnotationType { Square, Circle }
}
