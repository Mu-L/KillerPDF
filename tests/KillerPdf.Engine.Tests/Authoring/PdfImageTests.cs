using System.IO.Compression;
using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfImageTests
{
    [Fact]
    public void FromRgb_CompressesExactPixelBytes()
    {
        byte[] pixels = [255, 0, 0, 0, 255, 0];
        PdfImage image = PdfImage.FromRgb(2, 1, pixels);
        using var compressed = new MemoryStream(image.Data.ToArray());
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        zlib.CopyTo(decoded);

        Assert.Equal(pixels, decoded.ToArray());
        Assert.Equal(PdfImageColorSpace.Rgb, image.ColorSpace);
    }

    [Fact]
    public void FromJpeg_ReadsFrameWithoutRecompressing()
    {
        byte[] jpeg = MinimalJpeg(width: 320, height: 200, components: 3);
        PdfImage image = PdfImage.FromJpeg(jpeg);

        Assert.Equal(320, image.Width);
        Assert.Equal(200, image.Height);
        Assert.Equal(8, image.BitsPerComponent);
        Assert.Equal(PdfImageColorSpace.Rgb, image.ColorSpace);
        Assert.Equal(jpeg, image.Data.ToArray());
    }

    [Fact]
    public void FromRgba_WritesAlphaAsImageSoftMask()
    {
        PdfImage image = PdfImage.FromRgba(1, 1, new byte[] { 10, 20, 30, 64 });
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder().AddPage(
            100, 100, new PdfContentStreamBuilder().DrawImage(image, 0, 0, 10, 10)).Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var pages = ResolveDictionary(document, catalog[Name("Pages")]);
        var page = ResolveDictionary(document, Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);
        var resources = Assert.IsType<PdfDictionary>(page[Name("Resources")]);
        var xobjects = Assert.IsType<PdfDictionary>(resources[Name("XObject")]);
        var color = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(xobjects[Name("Im1")])));
        var mask = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(color.Dictionary[Name("SMask")])));

        Assert.Equal("DeviceGray", Assert.IsType<PdfName>(
            mask.Dictionary[Name("ColorSpace")]).ValueAsLatin1());
        using var input = new MemoryStream(mask.EncodedData.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        Assert.Equal(64, zlib.ReadByte());
    }

    [Fact]
    public void DrawImage_CreatesImageXObjectAndPlacementOperators()
    {
        PdfImage image = PdfImage.FromRgb(1, 1, new byte[] { 10, 20, 30 });
        var content = new PdfContentStreamBuilder().DrawImage(image, 10, 20, 30, 40);
        PdfDocument document = PdfDocument.Open(
            new PdfDocumentBuilder().AddPage(100, 100, content).Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var pages = ResolveDictionary(document, catalog[Name("Pages")]);
        var page = ResolveDictionary(document, Assert.IsType<PdfArray>(pages[Name("Kids")])[0]);
        var resources = Assert.IsType<PdfDictionary>(page[Name("Resources")]);
        var xobjects = Assert.IsType<PdfDictionary>(resources[Name("XObject")]);
        var imageStream = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(xobjects[Name("Im1")])));
        var contentStream = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(page[Name("Contents")])));

        Assert.Equal(1, Assert.IsType<PdfInteger>(imageStream.Dictionary[Name("Width")]).Value);
        Assert.Equal("FlateDecode", Assert.IsType<PdfName>(
            imageStream.Dictionary[Name("Filter")]).ValueAsLatin1());
        Assert.Equal("q\n30 0 0 40 10 20 cm\n/Im1 Do\nQ\n",
            Encoding.ASCII.GetString(contentStream.EncodedData.Span));
    }

    [Fact]
    public void FromJpeg_RejectsUnsupportedComponentCount()
    {
        Assert.Throws<NotSupportedException>(() =>
            PdfImage.FromJpeg(MinimalJpeg(1, 1, components: 2)));
    }

    private static byte[] MinimalJpeg(int width, int height, int components) =>
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x08,
        0x08,
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        (byte)components,
        0xFF, 0xD9
    ];

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
