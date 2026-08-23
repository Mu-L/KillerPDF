using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;
using KillerPdf.Engine.Fonts;
using System.Text;

namespace KillerPdf.Engine.Authoring;

/// <summary>Builds deterministic PDF graphics operators for a page content stream.</summary>
public sealed class PdfContentStreamBuilder
{
    private readonly MemoryStream _output = new();
    private readonly Dictionary<PdfStandardFont, PdfName> _fonts = [];
    private readonly Dictionary<TrueTypeFont, EmbeddedFontUsage> _embeddedFonts = [];
    private readonly Dictionary<PdfImage, PdfName> _images = [];
    private readonly Dictionary<PdfOptionalContentGroup, PdfName> _optionalContentGroups = [];
    private readonly Dictionary<PdfGraphicsState, PdfName> _graphicsStates = [];
    private readonly Dictionary<PdfShading, PdfName> _shadings = [];
    private readonly Dictionary<PdfFormXObject, PdfName> _forms = [];
    private readonly Dictionary<PdfTilingPattern, PdfName> _patterns = [];
    private int _savedStateDepth;
    private readonly Stack<bool> _markedContentStack = [];
    private int _accessibleMarkedContentDepth;
    private bool _insideText;
    private bool _hasUntaggedContent;
    private bool _hasColorOperators;
    private int _nextFontResource = 1;
    private EmbeddedFontUsage? _activeEmbeddedFont;
    private readonly HashSet<int> _markedContentIds = [];

    internal IReadOnlyDictionary<PdfStandardFont, PdfName> FontResources => _fonts;
    internal IReadOnlyCollection<EmbeddedFontUsage> EmbeddedFontResources => _embeddedFonts.Values;
    internal IReadOnlyDictionary<PdfImage, PdfName> ImageResources => _images;
    internal IReadOnlyDictionary<PdfOptionalContentGroup, PdfName> OptionalContentResources =>
        _optionalContentGroups;
    internal IReadOnlyDictionary<PdfGraphicsState, PdfName> GraphicsStateResources =>
        _graphicsStates;
    internal IReadOnlyDictionary<PdfShading, PdfName> ShadingResources => _shadings;
    internal IReadOnlyDictionary<PdfFormXObject, PdfName> FormResources => _forms;
    internal IReadOnlyDictionary<PdfTilingPattern, PdfName> PatternResources => _patterns;
    internal IReadOnlyCollection<int> MarkedContentIds => _markedContentIds;
    internal bool HasUntaggedContent => _hasUntaggedContent;
    internal bool HasColorOperators => _hasColorOperators;

    /// <summary>Begins tagged marked content associated with a page-local MCID.</summary>
    public PdfContentStreamBuilder BeginMarkedContent(
        PdfStructureType type, int markedContentId)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (markedContentId < 0)
            throw new ArgumentOutOfRangeException(nameof(markedContentId));
        if (!_markedContentIds.Add(markedContentId))
            throw new ArgumentException(
                "Marked-content identifiers must be unique within a page.", nameof(markedContentId));
        _output.Write(PdfObjectWriter.Write(new PdfName(
            Encoding.ASCII.GetBytes(PdfStructureTypeNames.Name(type)))));
        _output.WriteByte((byte)' ');
        _output.Write(PdfObjectWriter.Write(new PdfDictionary([
            new KeyValuePair<PdfName, PdfObject>(
                new PdfName("MCID"u8), new PdfInteger(markedContentId))])));
        _output.Write(" BDC\n"u8);
        _markedContentStack.Push(true);
        _accessibleMarkedContentDepth++;
        return this;
    }

    /// <summary>Begins pagination or layout content that assistive technology should ignore.</summary>
    public PdfContentStreamBuilder BeginArtifact()
    {
        _output.Write("/Artifact BMC\n"u8);
        _markedContentStack.Push(true);
        _accessibleMarkedContentDepth++;
        return this;
    }

    /// <summary>Begins content controlled by a named PDF layer.</summary>
    public PdfContentStreamBuilder BeginOptionalContent(PdfOptionalContentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!_optionalContentGroups.TryGetValue(group, out PdfName? resource))
        {
            resource = new PdfName(Encoding.ASCII.GetBytes(
                $"OC{_optionalContentGroups.Count + 1}"));
            _optionalContentGroups.Add(group, resource);
        }
        _output.Write("/OC "u8);
        _output.Write(PdfObjectWriter.Write(resource));
        _output.Write(" BDC\n"u8);
        _markedContentStack.Push(false);
        return this;
    }

    public PdfContentStreamBuilder EndMarkedContent()
    {
        if (_markedContentStack.Count == 0)
            throw new InvalidOperationException("The marked-content stack is empty.");
        WriteOperator("EMC"u8);
        if (_markedContentStack.Pop()) _accessibleMarkedContentDepth--;
        return this;
    }

    public PdfContentStreamBuilder SaveState()
    {
        WriteOperator("q"u8);
        _savedStateDepth++;
        return this;
    }

    public PdfContentStreamBuilder RestoreState()
    {
        if (_savedStateDepth == 0)
            throw new InvalidOperationException("The graphics state stack is empty.");
        WriteOperator("Q"u8);
        _savedStateDepth--;
        return this;
    }

    public PdfContentStreamBuilder Transform(double a, double b, double c, double d, double e, double f) =>
        Operator("cm"u8, a, b, c, d, e, f);

    public PdfContentStreamBuilder SetLineWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return Operator("w"u8, width);
    }

    /// <summary>Applies reusable fill opacity, stroke opacity, and blend-mode settings.</summary>
    public PdfContentStreamBuilder SetGraphicsState(PdfGraphicsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_graphicsStates.TryGetValue(state, out PdfName? resource))
        {
            resource = new PdfName(Encoding.ASCII.GetBytes($"GS{_graphicsStates.Count + 1}"));
            _graphicsStates.Add(state, resource);
        }
        _output.Write(PdfObjectWriter.Write(resource));
        _output.Write(" gs\n"u8);
        return this;
    }

    public PdfContentStreamBuilder SetOpacity(double opacity) =>
        SetGraphicsState(new PdfGraphicsState(opacity, opacity));

    public PdfContentStreamBuilder SetOpacity(double fillOpacity, double strokeOpacity) =>
        SetGraphicsState(new PdfGraphicsState(fillOpacity, strokeOpacity));

    public PdfContentStreamBuilder SetBlendMode(PdfBlendMode blendMode) =>
        SetGraphicsState(new PdfGraphicsState(blendMode: blendMode));

    /// <summary>Paints an axial or radial gradient across the current clipping path.</summary>
    public PdfContentStreamBuilder PaintShading(PdfShading shading)
    {
        ArgumentNullException.ThrowIfNull(shading);
        if (!_shadings.TryGetValue(shading, out PdfName? resource))
        {
            resource = new PdfName(Encoding.ASCII.GetBytes($"Sh{_shadings.Count + 1}"));
            _shadings.Add(shading, resource);
        }
        RecordPaintedContent();
        _hasColorOperators = true;
        _output.Write(PdfObjectWriter.Write(resource));
        _output.Write(" sh\n"u8);
        return this;
    }

    /// <summary>Places reusable form content at its natural size.</summary>
    public PdfContentStreamBuilder DrawForm(PdfFormXObject form, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(form);
        return DrawForm(form, x, y, form.Width, form.Height);
    }

    /// <summary>Places reusable form content in the requested rectangle.</summary>
    public PdfContentStreamBuilder DrawForm(
        PdfFormXObject form, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        if (!double.IsFinite(width) || width == 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height == 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!_forms.TryGetValue(form, out PdfName? resource))
        {
            resource = new PdfName(Encoding.ASCII.GetBytes($"Fm{_forms.Count + 1}"));
            _forms.Add(form, resource);
        }
        RecordPaintedContent();
        WriteOperator("q"u8);
        Operator("cm"u8, width / form.Width, 0, 0, height / form.Height, x, y);
        _output.Write(PdfObjectWriter.Write(resource));
        _output.Write(" Do\n"u8);
        WriteOperator("Q"u8);
        return this;
    }

    /// <summary>Selects a reusable coloured tiling pattern for subsequent fills.</summary>
    public PdfContentStreamBuilder SetFillPattern(PdfTilingPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.PaintType != PdfTilingPatternPaintType.Colored)
            throw new ArgumentException(
                "An uncolored pattern must be selected together with a base color.", nameof(pattern));
        PdfName resource = PatternResource(pattern);
        _hasColorOperators = true;
        _output.Write("/Pattern cs\n"u8);
        _output.Write(PdfObjectWriter.Write(resource));
        _output.Write(" scn\n"u8);
        return this;
    }

    /// <summary>Selects an uncoloured stencil pattern and supplies its DeviceRGB base colour.</summary>
    public PdfContentStreamBuilder SetFillPattern(PdfTilingPattern pattern, PdfRgbColor color)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.PaintType != PdfTilingPatternPaintType.Uncolored)
            throw new ArgumentException(
                "A base color can only be supplied for an uncolored pattern.", nameof(pattern));
        PdfName resource = PatternResource(pattern);
        _hasColorOperators = true;
        _output.Write("[/Pattern /DeviceRGB] cs\n"u8);
        WriteNumber(color.Red);
        _output.WriteByte((byte)' ');
        WriteNumber(color.Green);
        _output.WriteByte((byte)' ');
        WriteNumber(color.Blue);
        _output.WriteByte((byte)' ');
        _output.Write(PdfObjectWriter.Write(resource));
        _output.Write(" scn\n"u8);
        return this;
    }

    private PdfName PatternResource(PdfTilingPattern pattern)
    {
        if (!_patterns.TryGetValue(pattern, out PdfName? resource))
        {
            resource = new PdfName(Encoding.ASCII.GetBytes($"P{_patterns.Count + 1}"));
            _patterns.Add(pattern, resource);
        }
        return resource;
    }

    public PdfContentStreamBuilder MoveTo(double x, double y) => Operator("m"u8, x, y);
    public PdfContentStreamBuilder LineTo(double x, double y) => Operator("l"u8, x, y);
    public PdfContentStreamBuilder CurveTo(
        double x1, double y1, double x2, double y2, double x3, double y3) =>
        Operator("c"u8, x1, y1, x2, y2, x3, y3);
    public PdfContentStreamBuilder Rectangle(double x, double y, double width, double height) =>
        Operator("re"u8, x, y, width, height);
    public PdfContentStreamBuilder ClosePath() => NoOperand("h"u8);
    public PdfContentStreamBuilder Stroke() => PaintingOperator("S"u8);
    public PdfContentStreamBuilder Fill() => PaintingOperator("f"u8);
    public PdfContentStreamBuilder FillEvenOdd() => PaintingOperator("f*"u8);
    public PdfContentStreamBuilder FillAndStroke() => PaintingOperator("B"u8);
    public PdfContentStreamBuilder EndPath() => NoOperand("n"u8);
    public PdfContentStreamBuilder Clip()
    {
        WriteOperator("W"u8);
        WriteOperator("n"u8);
        return this;
    }
    public PdfContentStreamBuilder ClipEvenOdd()
    {
        WriteOperator("W*"u8);
        WriteOperator("n"u8);
        return this;
    }

    /// <summary>Places an image in the page coordinate system.</summary>
    public PdfContentStreamBuilder DrawImage(
        PdfImage image, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!double.IsFinite(width) || width == 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height == 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        if (!_images.TryGetValue(image, out PdfName? resource))
        {
            resource = new PdfName(Encoding.ASCII.GetBytes($"Im{_images.Count + 1}"));
            _images.Add(image, resource);
        }
        RecordPaintedContent();
        WriteOperator("q"u8);
        Operator("cm"u8, width, 0, 0, height, x, y);
        _output.Write(PdfObjectWriter.Write(resource));
        _output.Write(" Do\n"u8);
        WriteOperator("Q"u8);
        return this;
    }

    public PdfContentStreamBuilder SetStrokeGray(double gray) => ColorOperator("G"u8, Component(gray, nameof(gray)));
    public PdfContentStreamBuilder SetFillGray(double gray) => ColorOperator("g"u8, Component(gray, nameof(gray)));
    public PdfContentStreamBuilder SetStrokeRgb(double red, double green, double blue) =>
        ColorOperator("RG"u8, Component(red, nameof(red)), Component(green, nameof(green)), Component(blue, nameof(blue)));
    public PdfContentStreamBuilder SetFillRgb(double red, double green, double blue) =>
        ColorOperator("rg"u8, Component(red, nameof(red)), Component(green, nameof(green)), Component(blue, nameof(blue)));

    public PdfContentStreamBuilder BeginText()
    {
        if (_insideText)
            throw new InvalidOperationException("A text object is already open.");
        WriteOperator("BT"u8);
        _insideText = true;
        return this;
    }

    public PdfContentStreamBuilder EndText()
    {
        RequireText();
        WriteOperator("ET"u8);
        _insideText = false;
        return this;
    }

    public PdfContentStreamBuilder SetFont(PdfStandardFont font, double size)
    {
        RequireText();
        if (!Enum.IsDefined(font))
            throw new ArgumentOutOfRangeException(nameof(font));
        if (!double.IsFinite(size) || size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (!_fonts.TryGetValue(font, out PdfName? resource))
        {
            resource = FontResourceName();
            _fonts.Add(font, resource);
        }
        _activeEmbeddedFont = null;
        _output.Write(PdfObjectWriter.Write(resource));
        _output.WriteByte((byte)' ');
        WriteNumber(size);
        _output.Write(" Tf\n"u8);
        return this;
    }

    /// <summary>Selects an embedded TrueType font for Unicode text.</summary>
    public PdfContentStreamBuilder SetFont(TrueTypeFont font, double size)
    {
        RequireText();
        ArgumentNullException.ThrowIfNull(font);
        if (!font.EmbeddingAllowed)
            throw new InvalidOperationException($"The embedding permissions in {font.PostScriptName} prohibit PDF embedding.");
        if (!double.IsFinite(size) || size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (!_embeddedFonts.TryGetValue(font, out EmbeddedFontUsage? usage))
        {
            usage = new EmbeddedFontUsage(font, FontResourceName());
            _embeddedFonts.Add(font, usage);
        }
        _activeEmbeddedFont = usage;
        _output.Write(PdfObjectWriter.Write(usage.ResourceName));
        _output.WriteByte((byte)' ');
        WriteNumber(size);
        _output.Write(" Tf\n"u8);
        return this;
    }

    public PdfContentStreamBuilder MoveText(double x, double y)
    {
        RequireText();
        return Operator("Td"u8, x, y);
    }

    public PdfContentStreamBuilder ShowLatin1Text(string text)
    {
        RequireText();
        ArgumentNullException.ThrowIfNull(text);
        RecordPaintedContent();
        byte[] bytes = new byte[text.Length];
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] > 0xFF)
                throw new ArgumentException("Built-in font text is limited to Latin-1 bytes.", nameof(text));
            bytes[index] = (byte)text[index];
        }
        _output.Write(PdfObjectWriter.Write(new PdfString(bytes, PdfStringForm.Literal)));
        _output.Write(" Tj\n"u8);
        return this;
    }

    /// <summary>Writes Unicode text as two-byte glyph identifiers and records its ToUnicode mappings.</summary>
    public PdfContentStreamBuilder ShowUnicodeText(string text)
    {
        RequireText();
        ArgumentNullException.ThrowIfNull(text);
        RecordPaintedContent();
        EmbeddedFontUsage usage = _activeEmbeddedFont
            ?? throw new InvalidOperationException("Select an embedded TrueType font before writing Unicode text.");
        _output.WriteByte((byte)'<');
        Span<byte> encoded = stackalloc byte[2];
        foreach (Rune rune in text.EnumerateRunes())
        {
            ushort glyph = usage.Font.GetGlyphId(rune.Value);
            if (glyph == 0 && rune.Value != 0)
                throw new ArgumentException(
                    $"Font {usage.Font.PostScriptName} has no glyph for U+{rune.Value:X4}.", nameof(text));
            usage.AddMapping(glyph, rune.Value);
            encoded[0] = (byte)(glyph >> 8);
            encoded[1] = (byte)glyph;
            WriteHexByte(encoded[0]);
            WriteHexByte(encoded[1]);
        }
        _output.Write("> Tj\n"u8);
        return this;
    }

    public byte[] Build()
    {
        if (_savedStateDepth != 0)
            throw new InvalidOperationException("Every saved graphics state must be restored before building.");
        if (_insideText)
            throw new InvalidOperationException("The text object must be ended before building.");
        if (_markedContentStack.Count != 0)
            throw new InvalidOperationException(
                "Every marked-content sequence must be ended before building.");
        return _output.ToArray();
    }

    private PdfContentStreamBuilder Operator(ReadOnlySpan<byte> name, params double[] operands)
    {
        foreach (double operand in operands)
        {
            WriteNumber(operand);
            _output.WriteByte((byte)' ');
        }
        WriteOperator(name);
        return this;
    }

    private PdfContentStreamBuilder NoOperand(ReadOnlySpan<byte> name)
    {
        WriteOperator(name);
        return this;
    }

    private PdfContentStreamBuilder ColorOperator(ReadOnlySpan<byte> name, params double[] operands)
    {
        _hasColorOperators = true;
        return Operator(name, operands);
    }

    private PdfContentStreamBuilder PaintingOperator(ReadOnlySpan<byte> name)
    {
        RecordPaintedContent();
        return NoOperand(name);
    }

    private void RecordPaintedContent()
    {
        if (_accessibleMarkedContentDepth == 0) _hasUntaggedContent = true;
    }

    private void WriteNumber(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Graphics operands must be finite.");
        PdfObject number = value == Math.Truncate(value) && value is >= long.MinValue and <= long.MaxValue
            ? new PdfInteger((long)value)
            : new PdfReal(value);
        _output.Write(PdfObjectWriter.Write(number));
    }

    private void WriteOperator(ReadOnlySpan<byte> value)
    {
        _output.Write(value);
        _output.WriteByte((byte)'\n');
    }

    private static double Component(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, "Color components must be between zero and one.");
        return value;
    }

    private void RequireText()
    {
        if (!_insideText)
            throw new InvalidOperationException("This operator requires an open text object.");
    }

    private PdfName FontResourceName() =>
        new(Encoding.ASCII.GetBytes($"F{_nextFontResource++}"));

    private void WriteHexByte(byte value)
    {
        const string digits = "0123456789ABCDEF";
        _output.WriteByte((byte)digits[value >> 4]);
        _output.WriteByte((byte)digits[value & 0x0F]);
    }
}

internal sealed class EmbeddedFontUsage(TrueTypeFont font, PdfName resourceName)
{
    private readonly SortedDictionary<ushort, int> _unicodeByGlyph = [];

    public TrueTypeFont Font { get; } = font;
    public PdfName ResourceName { get; } = resourceName;
    public IReadOnlyDictionary<ushort, int> UnicodeByGlyph => _unicodeByGlyph;

    public void AddMapping(ushort glyph, int unicodeScalar)
    {
        if (_unicodeByGlyph.TryGetValue(glyph, out int existing) && existing != unicodeScalar)
            throw new InvalidOperationException(
                $"Glyph {glyph} maps to both U+{existing:X4} and U+{unicodeScalar:X4}; this font requires shaped text support.");
        _unicodeByGlyph[glyph] = unicodeScalar;
    }
}
