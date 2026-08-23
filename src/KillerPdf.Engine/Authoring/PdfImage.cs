using System.IO.Compression;

namespace KillerPdf.Engine.Authoring;

/// <summary>An image prepared for use as a PDF image XObject.</summary>
public sealed class PdfImage
{
    private PdfImage(
        int width, int height, int bitsPerComponent, PdfImageColorSpace colorSpace,
        string filter, byte[] data, bool invertComponents, PdfImage? softMask = null)
    {
        Width = width;
        Height = height;
        BitsPerComponent = bitsPerComponent;
        ColorSpace = colorSpace;
        Filter = filter;
        Data = data;
        InvertComponents = invertComponents;
        SoftMask = softMask;
    }

    public int Width { get; }
    public int Height { get; }
    public int BitsPerComponent { get; }
    public PdfImageColorSpace ColorSpace { get; }
    public ReadOnlyMemory<byte> Data { get; }
    internal string Filter { get; }
    internal bool InvertComponents { get; }
    internal PdfImage? SoftMask { get; }

    /// <summary>Wraps a JPEG without recompressing its pixels.</summary>
    public static PdfImage FromJpeg(ReadOnlyMemory<byte> source)
    {
        byte[] data = source.ToArray();
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new FormatException("The image does not begin with a JPEG SOI marker.");
        int position = 2;
        while (position < data.Length)
        {
            while (position < data.Length && data[position] != 0xFF) position++;
            while (position < data.Length && data[position] == 0xFF) position++;
            if (position >= data.Length) break;
            byte marker = data[position++];
            if (marker is 0x00 or 0xD8 || marker is >= 0xD0 and <= 0xD9)
                continue;
            if (position + 2 > data.Length)
                throw new FormatException("A JPEG marker length is truncated.");
            int length = (data[position] << 8) | data[position + 1];
            if (length < 2 || position + length > data.Length)
                throw new FormatException("A JPEG marker points outside the image.");
            if (IsStartOfFrame(marker))
            {
                if (length < 8)
                    throw new FormatException("The JPEG frame header is truncated.");
                int bits = data[position + 2];
                int height = (data[position + 3] << 8) | data[position + 4];
                int width = (data[position + 5] << 8) | data[position + 6];
                int components = data[position + 7];
                if (width == 0 || height == 0 || bits == 0)
                    throw new FormatException("The JPEG frame dimensions are invalid.");
                PdfImageColorSpace colorSpace = components switch
                {
                    1 => PdfImageColorSpace.Gray,
                    3 => PdfImageColorSpace.Rgb,
                    4 => PdfImageColorSpace.Cmyk,
                    _ => throw new NotSupportedException($"JPEG images with {components} components are not supported.")
                };
                return new PdfImage(width, height, bits, colorSpace, "DCTDecode", data,
                    invertComponents: components == 4);
            }
            if (marker == 0xDA)
                break;
            position += length;
        }
        throw new FormatException("The JPEG has no supported start-of-frame marker.");
    }

    /// <summary>Compresses interleaved 8-bit RGB pixels with the PDF Flate filter.</summary>
    public static PdfImage FromRgb(
        int width, int height, ReadOnlyMemory<byte> pixels,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        ValidateDimensions(width, height);
        int required = checked(width * height * 3);
        if (pixels.Length != required)
            throw new ArgumentException($"An {width} by {height} RGB image requires {required} bytes.", nameof(pixels));
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, compressionLevel, leaveOpen: true))
            zlib.Write(pixels.Span);
        return new PdfImage(width, height, 8, PdfImageColorSpace.Rgb,
            "FlateDecode", output.ToArray(), invertComponents: false);
    }

    /// <summary>Compresses interleaved 8-bit RGBA pixels and preserves alpha as a soft mask.</summary>
    public static PdfImage FromRgba(
        int width, int height, ReadOnlyMemory<byte> pixels,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        ValidateDimensions(width, height);
        int pixelCount = checked(width * height);
        int required = checked(pixelCount * 4);
        if (pixels.Length != required)
            throw new ArgumentException($"An {width} by {height} RGBA image requires {required} bytes.", nameof(pixels));
        byte[] rgb = new byte[pixelCount * 3];
        byte[] alpha = new byte[pixelCount];
        ReadOnlySpan<byte> source = pixels.Span;
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            rgb[pixel * 3] = source[pixel * 4];
            rgb[pixel * 3 + 1] = source[pixel * 4 + 1];
            rgb[pixel * 3 + 2] = source[pixel * 4 + 2];
            alpha[pixel] = source[pixel * 4 + 3];
        }
        PdfImage color = FromRgb(width, height, rgb, compressionLevel);
        PdfImage mask = FromGray(width, height, alpha, compressionLevel);
        return new PdfImage(width, height, 8, PdfImageColorSpace.Rgb,
            color.Filter, color.Data.ToArray(), invertComponents: false, mask);
    }

    private static PdfImage FromGray(
        int width, int height, ReadOnlyMemory<byte> pixels, CompressionLevel compressionLevel)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, compressionLevel, leaveOpen: true))
            zlib.Write(pixels.Span);
        return new PdfImage(width, height, 8, PdfImageColorSpace.Gray,
            "FlateDecode", output.ToArray(), invertComponents: false);
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    }
}

public enum PdfImageColorSpace
{
    Gray,
    Rgb,
    Cmyk
}
