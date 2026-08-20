using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using KillerPDF.Services;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace KillerPDF.Tests;

// #169: content placed on a rotated page must burn where the user placed it. The burn draws in
// the VISUAL frame and maps back through a quarter-turn matrix. Two parts have regressed
// independently and each is pinned here by reading the saved content stream:
//   1. the scale basis - sx/sy must come from the visual page size, not the raw page box
//      (a regression squeezes the rect by exactly the page's aspect ratio), and
//   2. the quarter-turn matrix reaching both burn paths (annotations AND stamps) - dropping
//      it leaves the rect numbers right but the content turned 90 degrees on the page.
public sealed class PdfBurnRotationTests
{
    // A5-ish landscape map on a portrait MediaBox, the shape from the #169 repro files.
    private const double BoxW = 842, BoxH = 1191;

    private static string BurnHighlightContent(int nativeRotate, Dictionary<int, int>? rotations,
        double pageW, double pageH, int renderW, int renderH, Rect bounds)
    {
        using var doc = new PdfDocument();
        doc.Options.NoCompression = true;
        var page = doc.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(pageW, pageH));
        if (nativeRotate != 0) page.Rotate = nativeRotate;

        var annots = new Dictionary<int, List<PageAnnotation>>
        {
            [0] = [new HighlightAnnotation { PageIndex = 0, Bounds = bounds, Style = HighlightStyle.Fill }],
        };
        var dims = new Dictionary<int, (int w, int h)> { [0] = (renderW, renderH) };
        PdfBurn.DrawAnnotationsIntoDoc(doc, annots, dims, null, rotations);
        return SaveToText(doc);
    }

    private static string SaveToText(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return Encoding.GetEncoding("ISO-8859-1").GetString(ms.ToArray());
    }

    // Every `x y w h re` operator in the saved file, as (w, h).
    private static List<(double w, double h)> RectSizes(string pdf)
    {
        var list = new List<(double, double)>();
        foreach (Match m in Regex.Matches(pdf,
            @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) re"))
        {
            list.Add((double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                      double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }
        return list;
    }

    // True when the content carries a quarter-turn cm (a and d zero, b and c unit) - the visual-to-page
    // mapping the rotated burn must emit. The unrotated base transform is axis-aligned and never matches.
    private static bool HasQuarterTurnCm(string pdf)
    {
        foreach (Match m in Regex.Matches(pdf,
            @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) cm"))
        {
            double a = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            double b = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            double c = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            double d = double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (Math.Abs(a) < 0.001 && Math.Abs(d) < 0.001 &&
                Math.Abs(Math.Abs(b) - 1) < 0.001 && Math.Abs(Math.Abs(c) - 1) < 0.001)
                return true;
        }
        return false;
    }

    [Fact]
    public void UnrotatedPage_BurnsAtRenderScale_NoTurn()
    {
        // Letter page rendered at 2x: a 300x60 canvas rect must burn as 150x30 points.
        string pdf = BurnHighlightContent(0, null, 612, 792, 1224, 1584, new Rect(100, 200, 300, 60));

        var rects = RectSizes(pdf);
        var r = Assert.Single(rects);
        Assert.Equal(150, r.w, 2);
        Assert.Equal(30, r.h, 2);
        Assert.False(HasQuarterTurnCm(pdf));
    }

    // Native /Rotate on a freshly opened file (the 1.7.1 fallback - the rotation map is empty),
    // and both quarter turns.
    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void NativeRotate_UsesVisualScaleAndTurns(int rotate)
    {
        // Visual frame is 1191x842, rendered at 2x. A 300x60 canvas rect must burn as 150x30.
        // The #169 regression scaled against the raw 842x1191 box instead, which burns exactly
        // 106.07x42.45 - the aspect-ratio squeeze from terada-d's measurements.
        string pdf = BurnHighlightContent(rotate, null, BoxW, BoxH, 2382, 1684, new Rect(200, 400, 300, 60));

        var rects = RectSizes(pdf);
        var r = Assert.Single(rects);
        Assert.Equal(150, r.w, 2);
        Assert.Equal(30, r.h, 2);
        Assert.True(HasQuarterTurnCm(pdf), "rotated burn emitted no quarter-turn cm - content will land turned 90 degrees");
    }

    [Fact]
    public void InAppRotation_MapOverridesStrippedPage()
    {
        // In-app rotation: the working copy has /Rotate stripped to 0 and the angle lives in the
        // shell's rotation map. The burn must honor the map exactly as it honors a native /Rotate.
        var rotations = new Dictionary<int, int> { [0] = 90 };
        string pdf = BurnHighlightContent(0, rotations, BoxW, BoxH, 2382, 1684, new Rect(200, 400, 300, 60));

        var rects = RectSizes(pdf);
        var r = Assert.Single(rects);
        Assert.Equal(150, r.w, 2);
        Assert.Equal(30, r.h, 2);
        Assert.True(HasQuarterTurnCm(pdf));
    }

    [Fact]
    public void Rotate180_ScalesUnswappedAndTurns()
    {
        // 180 keeps the axes (no dimension swap) but still needs its half-turn mapping.
        string pdf = BurnHighlightContent(180, null, BoxW, BoxH, 1684, 2382, new Rect(200, 400, 300, 60));

        var rects = RectSizes(pdf);
        var r = Assert.Single(rects);
        Assert.Equal(150, r.w, 2);
        Assert.Equal(30, r.h, 2);
        // A half turn is (-1 0 0 -1) pre-flip; composed with the base flip it is axis-aligned,
        // so assert on the rect numbers plus the annotation surviving - not on the cm shape.
    }

    [Fact]
    public void StampBurn_RotatedPage_GetsTheTurnToo()
    {
        // Stamps share the visual-frame helpers; the original #169 gap was the stamp burn never
        // receiving the angle at all, so preview and output disagreed.
        using var doc = new PdfDocument();
        doc.Options.NoCompression = true;
        var page = doc.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(BoxW, BoxH));
        page.Rotate = 90;

        var spec = new StampSpec { NumbersEnabled = true, Format = "{n} / {N}" };
        PdfBurn.DrawStampsIntoDoc(doc, spec);

        string pdf = SaveToText(doc);
        Assert.True(HasQuarterTurnCm(pdf), "stamp burn on a rotated page emitted no quarter-turn cm");
    }
}
