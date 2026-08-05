using KillerPDF.Services;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace KillerPDF.Tests;

public sealed class PdfScrubTests
{
    [Fact]
    public void ScrubDegenerateCropBoxes_RemovesCropOutsideMediaBox()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(1191, 842));
        page.CropBox = new PdfRectangle(new XPoint(0, 0), new XPoint(842, 1191));
        page.Rotate = 90;

        PdfScrub.ScrubDegenerateCropBoxes(doc);

        Assert.Null(page.Elements["/CropBox"]);
    }

    [Fact]
    public void ScrubDegenerateCropBoxes_PreservesValidInsetCrop()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(612, 792));
        page.CropBox = new PdfRectangle(new XPoint(18, 24), new XPoint(594, 768));

        PdfScrub.ScrubDegenerateCropBoxes(doc);

        Assert.NotNull(page.Elements["/CropBox"]);
    }
}
