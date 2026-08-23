namespace KillerPdf.Engine.Authoring;

public enum PdfIndexedBaseColorSpace { Gray, Rgb, Cmyk }

/// <summary>A compact palette whose entries use a device Gray, RGB, or CMYK base space.</summary>
public sealed class PdfIndexedColorSpace
{
    private readonly byte[] _palette;

    public PdfIndexedColorSpace(
        PdfIndexedBaseColorSpace baseColorSpace, ReadOnlyMemory<byte> palette)
    {
        if (!Enum.IsDefined(baseColorSpace))
            throw new ArgumentOutOfRangeException(nameof(baseColorSpace));
        int components = baseColorSpace switch
        {
            PdfIndexedBaseColorSpace.Gray => 1,
            PdfIndexedBaseColorSpace.Rgb => 3,
            PdfIndexedBaseColorSpace.Cmyk => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(baseColorSpace))
        };
        if (palette.Length == 0 || palette.Length % components != 0)
            throw new ArgumentException(
                $"The palette must contain complete {baseColorSpace} entries.", nameof(palette));
        int entries = palette.Length / components;
        if (entries > 256)
            throw new ArgumentException("An Indexed color space supports at most 256 entries.", nameof(palette));
        BaseColorSpace = baseColorSpace;
        ComponentCount = components;
        EntryCount = entries;
        _palette = palette.ToArray();
    }

    public PdfIndexedBaseColorSpace BaseColorSpace { get; }
    public int ComponentCount { get; }
    public int EntryCount { get; }
    public ReadOnlyMemory<byte> Palette => _palette;
}
