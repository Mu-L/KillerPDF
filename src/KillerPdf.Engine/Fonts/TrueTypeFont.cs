using System.Buffers.Binary;
using System.Text;

namespace KillerPdf.Engine.Fonts;

/// <summary>A bounded reader for OpenType fonts with TrueType or CFF outlines.</summary>
public sealed class TrueTypeFont
{
    private const int MaximumTableCount = 4_096;
    private readonly byte[] _data;
    private readonly Dictionary<uint, Table> _tables;
    private readonly ICmap _cmap;
    private readonly ushort[] _advanceWidths;

    private TrueTypeFont(byte[] data, Dictionary<uint, Table> tables, bool hasCffOutlines)
    {
        _data = data;
        _tables = tables;
        HasCffOutlines = hasCffOutlines;
        Table head = Required("head");
        Table hhea = Required("hhea");
        Table maxp = Required("maxp");
        Table hmtx = Required("hmtx");
        UnitsPerEm = U16(head, 18);
        if (UnitsPerEm is < 16 or > 16_384)
            throw Error("head.unitsPerEm is outside the TrueType range");
        Bounds = new TrueTypeBounds(S16(head, 36), S16(head, 38), S16(head, 40), S16(head, 42));
        Ascender = S16(hhea, 4);
        Descender = S16(hhea, 6);
        GlyphCount = U16(maxp, 4);
        int horizontalMetricCount = U16(hhea, 34);
        if (GlyphCount == 0 || horizontalMetricCount is < 1 || horizontalMetricCount > GlyphCount)
            throw Error("The horizontal metric count is invalid");
        _advanceWidths = ReadWidths(hmtx, GlyphCount, horizontalMetricCount);
        _cmap = ReadCmap(Required("cmap"));
        PostScriptName = ReadPostScriptName(Required("name"));

        if (TryTable("OS/2", out Table os2) && os2.Length >= 10)
            EmbeddingFlags = U16(os2, 8);
        if (TryTable("post", out Table post) && post.Length >= 8)
            ItalicAngle = S32(post, 4) / 65536d;
    }

    public string PostScriptName { get; }
    public int UnitsPerEm { get; }
    public int GlyphCount { get; }
    public int Ascender { get; }
    public int Descender { get; }
    public TrueTypeBounds Bounds { get; }
    public ushort EmbeddingFlags { get; }
    public double ItalicAngle { get; }
    public bool EmbeddingAllowed => (EmbeddingFlags & 0x0202) == 0;
    public bool SubsettingAllowed => (EmbeddingFlags & 0x0100) == 0;
    public bool HasCffOutlines { get; }
    public ReadOnlyMemory<byte> FontData => _data;

    public static TrueTypeFont Load(ReadOnlyMemory<byte> source)
    {
        byte[] data = source.ToArray();
        if (data.Length < 12)
            throw Error("The font is shorter than an sfnt header");
        uint scaler = ReadU32(data, 0);
        bool hasCffOutlines = scaler == Tag("OTTO");
        if (!hasCffOutlines && scaler is not 0x00010000 && scaler != Tag("true"))
            throw Error("The file is not a supported OpenType font");

        int tableCount = ReadU16(data, 4);
        if (tableCount is < 1 or > MaximumTableCount || 12L + tableCount * 16L > data.Length)
            throw Error("The sfnt table directory is invalid");
        var tables = new Dictionary<uint, Table>();
        for (int index = 0; index < tableCount; index++)
        {
            int record = 12 + index * 16;
            uint tag = ReadU32(data, record);
            uint offset = ReadU32(data, record + 8);
            uint length = ReadU32(data, record + 12);
            if (offset > data.Length || length > data.Length - offset)
                throw Error($"Font table {TagText(tag)} points outside the file");
            if (!tables.TryAdd(tag, new Table((int)offset, (int)length)))
                throw Error($"Font table {TagText(tag)} is duplicated");
        }
        if (hasCffOutlines && !tables.ContainsKey(Tag("CFF "))
            && !tables.ContainsKey(Tag("CFF2")))
            throw Error("An OTTO font has no CFF or CFF2 outline table");
        return new TrueTypeFont(data, tables, hasCffOutlines);
    }

    public ushort GetGlyphId(int unicodeScalar)
    {
        if (!Rune.IsValid(unicodeScalar))
            throw new ArgumentOutOfRangeException(nameof(unicodeScalar));
        return _cmap.Map(unicodeScalar);
    }

    public int GetPdfAdvanceWidth(ushort glyphId)
    {
        if (glyphId >= _advanceWidths.Length)
            throw new ArgumentOutOfRangeException(nameof(glyphId));
        return (int)Math.Round(
            _advanceWidths[glyphId] * 1000d / UnitsPerEm,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>Builds a deterministic TrueType subset while retaining original glyph identifiers.</summary>
    public byte[] CreateSubset(IEnumerable<ushort> glyphIds)
    {
        ArgumentNullException.ThrowIfNull(glyphIds);
        if (HasCffOutlines)
            throw new NotSupportedException(
                "CFF OpenType fonts are embedded in full because CFF subsetting is not yet available.");
        if (!SubsettingAllowed)
            throw new InvalidOperationException($"The embedding permissions in {PostScriptName} prohibit subsetting.");
        return TrueTypeSubsetter.Create(_data, GlyphCount, glyphIds);
    }

    private ushort[] ReadWidths(Table table, int glyphCount, int metricCount)
    {
        int required = checked(metricCount * 4 + (glyphCount - metricCount) * 2);
        if (table.Length < required)
            throw Error("The hmtx table is truncated");
        var widths = new ushort[glyphCount];
        for (int index = 0; index < metricCount; index++)
            widths[index] = U16(table, index * 4);
        ushort last = widths[metricCount - 1];
        for (int index = metricCount; index < glyphCount; index++)
            widths[index] = last;
        return widths;
    }

    private ICmap ReadCmap(Table cmap)
    {
        if (cmap.Length < 4)
            throw Error("The cmap table is truncated");
        int count = U16(cmap, 2);
        if (4L + count * 8L > cmap.Length)
            throw Error("The cmap encoding records are truncated");

        (int Score, int Offset, int Format)? best = null;
        for (int index = 0; index < count; index++)
        {
            int record = 4 + index * 8;
            int platform = U16(cmap, record);
            int encoding = U16(cmap, record + 2);
            uint relative = U32(cmap, record + 4);
            if (relative > cmap.Length - 2)
                continue;
            int subtable = checked(cmap.Offset + (int)relative);
            int format = ReadU16(_data, subtable);
            int score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 500,
                (0, _, 12) => 450,
                (3, 1, 4) => 400,
                (0, _, 4) => 350,
                _ => 0
            };
            if (score > 0 && (!best.HasValue || score > best.Value.Score))
                best = (score, subtable, format);
        }
        if (!best.HasValue)
            throw new NotSupportedException("The font has no supported Unicode cmap format 4 or 12 subtable.");
        return best.Value.Format == 12
            ? ReadFormat12(best.Value.Offset, cmap)
            : ReadFormat4(best.Value.Offset, cmap);
    }

    private ICmap ReadFormat12(int offset, Table parent)
    {
        if (offset + 16 > parent.End || ReadU16(_data, offset) != 12)
            throw Error("The cmap format 12 header is truncated");
        uint length = ReadU32(_data, offset + 4);
        uint groupCount = ReadU32(_data, offset + 12);
        if (length < 16 || length > parent.End - offset
            || groupCount > (length - 16) / 12 || groupCount > 1_000_000)
            throw Error("The cmap format 12 groups are invalid");
        var groups = new Format12Group[groupCount];
        uint previousEnd = 0;
        for (int index = 0; index < groups.Length; index++)
        {
            int position = offset + 16 + index * 12;
            uint start = ReadU32(_data, position);
            uint end = ReadU32(_data, position + 4);
            uint glyph = ReadU32(_data, position + 8);
            if (start > end || end > 0x10FFFF || (index > 0 && start <= previousEnd)
                || glyph + (end - start) >= GlyphCount)
                throw Error("The cmap format 12 groups are not ordered valid glyph ranges");
            groups[index] = new Format12Group(start, end, glyph);
            previousEnd = end;
        }
        return new Format12Cmap(groups);
    }

    private ICmap ReadFormat4(int offset, Table parent)
    {
        if (offset + 14 > parent.End || ReadU16(_data, offset) != 4)
            throw Error("The cmap format 4 header is truncated");
        int length = ReadU16(_data, offset + 2);
        int segmentCount = ReadU16(_data, offset + 6) / 2;
        if (length < 16 || offset + length > parent.End || segmentCount < 1
            || 16 + segmentCount * 8 > length)
            throw Error("The cmap format 4 segments are invalid");
        return new Format4Cmap(_data, offset, length, segmentCount, GlyphCount);
    }

    private string ReadPostScriptName(Table name)
    {
        if (name.Length < 6)
            throw Error("The name table is truncated");
        int count = U16(name, 2);
        int strings = U16(name, 4);
        if (6L + count * 12L > name.Length || strings > name.Length)
            throw Error("The name table records are invalid");
        string? fallback = null;
        for (int index = 0; index < count; index++)
        {
            int record = 6 + index * 12;
            int platform = U16(name, record);
            int nameId = U16(name, record + 6);
            int length = U16(name, record + 8);
            int relative = U16(name, record + 10);
            if (nameId != 6 || strings + relative > name.Length || length > name.Length - strings - relative)
                continue;
            ReadOnlySpan<byte> bytes = _data.AsSpan(name.Offset + strings + relative, length);
            string value = platform is 0 or 3
                ? DecodeBigEndianUnicode(bytes)
                : Encoding.Latin1.GetString(bytes);
            if (platform == 3 && value.Length > 0)
                return value;
            if (value.Length > 0)
                fallback ??= value;
        }
        return fallback ?? "UnnamedTrueTypeFont";
    }

    private static string DecodeBigEndianUnicode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % 2 != 0)
            return string.Empty;
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    private Table Required(string tag) =>
        TryTable(tag, out Table table) ? table : throw Error($"Required font table {tag} is missing");
    private bool TryTable(string tag, out Table table) => _tables.TryGetValue(Tag(tag), out table);
    private ushort U16(Table table, int offset) =>
        offset >= 0 && offset + 2 <= table.Length ? ReadU16(_data, table.Offset + offset) : throw Error("A font table is truncated");
    private short S16(Table table, int offset) => unchecked((short)U16(table, offset));
    private uint U32(Table table, int offset) =>
        offset >= 0 && offset + 4 <= table.Length ? ReadU32(_data, table.Offset + offset) : throw Error("A font table is truncated");
    private int S32(Table table, int offset) => unchecked((int)U32(table, offset));

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    private static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    private static uint Tag(string value) =>
        value.Length == 4 ? BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(value)) : throw new ArgumentException("A font tag has four bytes.");
    private static string TagText(uint value) => Encoding.ASCII.GetString([
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
    private static FormatException Error(string message) => new(message);

    private readonly record struct Table(int Offset, int Length) { public int End => Offset + Length; }
    private interface ICmap { ushort Map(int scalar); }
    private readonly record struct Format12Group(uint Start, uint End, uint StartGlyph);

    private sealed class Format12Cmap(Format12Group[] groups) : ICmap
    {
        public ushort Map(int scalar)
        {
            int low = 0;
            int high = groups.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                Format12Group group = groups[middle];
                if ((uint)scalar < group.Start) high = middle - 1;
                else if ((uint)scalar > group.End) low = middle + 1;
                else return checked((ushort)(group.StartGlyph + (uint)scalar - group.Start));
            }
            return 0;
        }
    }

    private sealed class Format4Cmap(
        byte[] data, int offset, int length, int segmentCount, int glyphCount) : ICmap
    {
        public ushort Map(int scalar)
        {
            if (scalar > ushort.MaxValue)
                return 0;
            int startCodes = offset + 16 + segmentCount * 2;
            int deltas = startCodes + segmentCount * 2;
            int ranges = deltas + segmentCount * 2;
            for (int index = 0; index < segmentCount; index++)
            {
                int end = ReadU16(data, offset + 14 + index * 2);
                if (scalar > end)
                    continue;
                int start = ReadU16(data, startCodes + index * 2);
                if (scalar < start)
                    return 0;
                int delta = unchecked((short)ReadU16(data, deltas + index * 2));
                int range = ReadU16(data, ranges + index * 2);
                int glyph = range == 0
                    ? (scalar + delta) & 0xFFFF
                    : ReadGlyph(index, scalar, start, delta, range, ranges);
                return glyph >= glyphCount ? (ushort)0 : (ushort)glyph;
            }
            return 0;
        }

        private int ReadGlyph(int index, int scalar, int start, int delta, int range, int ranges)
        {
            int address = ranges + index * 2 + range + (scalar - start) * 2;
            if (address < offset || address + 2 > offset + length)
                return 0;
            int glyph = ReadU16(data, address);
            return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
        }
    }
}

public readonly record struct TrueTypeBounds(int XMin, int YMin, int XMax, int YMax);
