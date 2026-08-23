using System.Buffers.Binary;
using System.Text;
using KillerPdf.Engine.Fonts;
using Xunit;

namespace KillerPdf.Engine.Tests.Fonts;

public sealed class TrueTypeFontTests
{
    [Fact]
    public void Load_ReadsNameMetricsWidthsAndFormat4Cmap()
    {
        TrueTypeFont font = TrueTypeFont.Load(BuildTestFont(format12: false));

        Assert.Equal("KillerTest", font.PostScriptName);
        Assert.Equal(1000, font.UnitsPerEm);
        Assert.Equal(2, font.GlyphCount);
        Assert.Equal(800, font.Ascender);
        Assert.Equal(-200, font.Descender);
        Assert.Equal(new TrueTypeBounds(-10, -200, 900, 800), font.Bounds);
        Assert.Equal((ushort)1, font.GetGlyphId('A'));
        Assert.Equal((ushort)0, font.GetGlyphId('B'));
        Assert.Equal(600, font.GetPdfAdvanceWidth(1));
        Assert.True(font.EmbeddingAllowed);
        Assert.True(font.SubsettingAllowed);
    }

    [Fact]
    public void Load_ReadsFormat12SupplementaryPlaneMapping()
    {
        TrueTypeFont font = TrueTypeFont.Load(BuildTestFont(format12: true));

        Assert.Equal((ushort)1, font.GetGlyphId(0x1F600));
        Assert.Equal((ushort)0, font.GetGlyphId('A'));
    }

    [Fact]
    public void Load_RejectsTableThatPointsOutsideTheFile()
    {
        byte[] font = BuildTestFont(format12: false);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(20, 4), uint.MaxValue);

        Assert.Throws<FormatException>(() => TrueTypeFont.Load(font));
    }

    [Fact]
    public void Load_ExposesRestrictedEmbeddingPermissions()
    {
        TrueTypeFont font = TrueTypeFont.Load(BuildTestFont(format12: false, embeddingFlags: 0x0002));

        Assert.False(font.EmbeddingAllowed);
    }

    [Fact]
    public void CreateSubset_PreservesGlyphIdsAndProducesDeterministicFont()
    {
        TrueTypeFont font = TrueTypeFont.Load(BuildTestFont(format12: false, includeOutlines: true));

        byte[] first = font.CreateSubset([1]);
        byte[] second = font.CreateSubset([1]);
        TrueTypeFont reopened = TrueTypeFont.Load(first);

        Assert.Equal(first, second);
        Assert.Equal((ushort)1, reopened.GetGlyphId('A'));
        Assert.Equal(600, reopened.GetPdfAdvanceWidth(1));
    }

    internal static byte[] BuildTestFont(
        bool format12, ushort embeddingFlags = 0, bool includeOutlines = false)
    {
        var tables = new Dictionary<string, byte[]>
        {
            ["cmap"] = format12 ? Cmap12() : Cmap4(),
            ["head"] = Bytes(54, bytes =>
            {
                U16(bytes, 18, 1000);
                S16(bytes, 36, -10);
                S16(bytes, 38, -200);
                S16(bytes, 40, 900);
                S16(bytes, 42, 800);
            }),
            ["hhea"] = Bytes(36, bytes =>
            {
                S16(bytes, 4, 800);
                S16(bytes, 6, -200);
                U16(bytes, 34, 2);
            }),
            ["hmtx"] = Bytes(8, bytes =>
            {
                U16(bytes, 0, 500);
                U16(bytes, 4, 600);
            }),
            ["maxp"] = Bytes(6, bytes => U16(bytes, 4, 2)),
            ["name"] = NameTable()
        };
        if (embeddingFlags != 0)
            tables["OS/2"] = Bytes(10, bytes => U16(bytes, 8, embeddingFlags));
        if (includeOutlines)
        {
            tables["glyf"] = Bytes(12, bytes => U16(bytes, 10, 0));
            tables["loca"] = Bytes(12, bytes =>
            {
                U32(bytes, 0, 0);
                U32(bytes, 4, 0);
                U32(bytes, 8, 12);
            });
            S16(tables["head"], 50, 1);
        }
        int directoryLength = 12 + tables.Count * 16;
        int totalLength = directoryLength + tables.Values.Sum(value => Align4(value.Length));
        byte[] result = new byte[totalLength];
        U32(result, 0, 0x00010000);
        U16(result, 4, tables.Count);
        int record = 12;
        int offset = directoryLength;
        foreach ((string tag, byte[] value) in tables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Encoding.ASCII.GetBytes(tag).CopyTo(result, record);
            U32(result, record + 8, offset);
            U32(result, record + 12, value.Length);
            value.CopyTo(result, offset);
            record += 16;
            offset += Align4(value.Length);
        }
        return result;
    }

    private static byte[] Cmap4()
    {
        byte[] result = new byte[44];
        U16(result, 2, 1);
        U16(result, 4, 3);
        U16(result, 6, 1);
        U32(result, 8, 12);
        int subtable = 12;
        U16(result, subtable, 4);
        U16(result, subtable + 2, 32);
        U16(result, subtable + 6, 4);
        U16(result, subtable + 8, 4);
        U16(result, subtable + 10, 1);
        U16(result, subtable + 14, 0x0041);
        U16(result, subtable + 16, 0xFFFF);
        U16(result, subtable + 20, 0x0041);
        U16(result, subtable + 22, 0xFFFF);
        U16(result, subtable + 24, 0xFFC0);
        U16(result, subtable + 26, 1);
        return result;
    }

    private static byte[] Cmap12()
    {
        byte[] result = new byte[40];
        U16(result, 2, 1);
        U16(result, 4, 3);
        U16(result, 6, 10);
        U32(result, 8, 12);
        int subtable = 12;
        U16(result, subtable, 12);
        U32(result, subtable + 4, 28);
        U32(result, subtable + 12, 1);
        U32(result, subtable + 16, 0x1F600);
        U32(result, subtable + 20, 0x1F600);
        U32(result, subtable + 24, 1);
        return result;
    }

    private static byte[] NameTable()
    {
        byte[] name = Encoding.BigEndianUnicode.GetBytes("KillerTest");
        byte[] result = new byte[18 + name.Length];
        U16(result, 2, 1);
        U16(result, 4, 18);
        U16(result, 6, 3);
        U16(result, 8, 1);
        U16(result, 10, 0x0409);
        U16(result, 12, 6);
        U16(result, 14, name.Length);
        name.CopyTo(result, 18);
        return result;
    }

    private static byte[] Bytes(int length, Action<byte[]> initialize)
    {
        byte[] result = new byte[length];
        initialize(result);
        return result;
    }

    private static int Align4(int value) => (value + 3) & ~3;
    private static void U16(byte[] target, int offset, int value) =>
        BinaryPrimitives.WriteUInt16BigEndian(target.AsSpan(offset, 2), checked((ushort)value));
    private static void S16(byte[] target, int offset, int value) =>
        BinaryPrimitives.WriteInt16BigEndian(target.AsSpan(offset, 2), checked((short)value));
    private static void U32(byte[] target, int offset, int value) => U32(target, offset, checked((uint)value));
    private static void U32(byte[] target, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset, 4), value);
}
