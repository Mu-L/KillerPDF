using System.Windows;
using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

// #169: rotating a page must keep its overlay annotations and turn them with the page.
// The mapping runs in render-dim space; a +90 turn takes frame (W, H) to (H, W), so
// round-trip tests swap the frame between passes exactly as the live reload does.
public sealed class AnnotationRotateTests
{
    private const double W = 800, H = 600;

    [Fact]
    public void Highlight_QuarterTurn_TurnsWithTheContent()
    {
        var ha = new HighlightAnnotation { Bounds = new Rect(10, 20, 100, 40) };
        AnnotationRotate.Remap([ha], 90, W, H);

        // Corners (10,20)-(110,60) map to (580,10)-(540,110): the region swaps its axes.
        Assert.Equal(540, ha.Bounds.X, 3);
        Assert.Equal(10, ha.Bounds.Y, 3);
        Assert.Equal(40, ha.Bounds.Width, 3);
        Assert.Equal(100, ha.Bounds.Height, 3);
    }

    [Fact]
    public void Highlight_FourQuarterTurns_IsIdentity()
    {
        var start = new Rect(10, 20, 100, 40);
        var ha = new HighlightAnnotation { Bounds = start };
        AnnotationRotate.Remap([ha], 90, W, H);
        AnnotationRotate.Remap([ha], 90, H, W);
        AnnotationRotate.Remap([ha], 90, W, H);
        AnnotationRotate.Remap([ha], 90, H, W);
        Assert.Equal(start, ha.Bounds);
    }

    [Fact]
    public void Highlight_TurnThenCounterTurn_IsIdentity()
    {
        var start = new Rect(33, 44, 55, 66);
        var ha = new HighlightAnnotation { Bounds = start };
        AnnotationRotate.Remap([ha], 90, W, H);
        AnnotationRotate.Remap([ha], -90, H, W);
        Assert.Equal(start, ha.Bounds);
    }

    [Fact]
    public void Highlight_TwoQuarterTurns_MatchHalfTurn()
    {
        var a = new HighlightAnnotation { Bounds = new Rect(10, 20, 100, 40) };
        var b = new HighlightAnnotation { Bounds = new Rect(10, 20, 100, 40) };
        AnnotationRotate.Remap([a], 90, W, H);
        AnnotationRotate.Remap([a], 90, H, W);
        AnnotationRotate.Remap([b], 180, W, H);
        Assert.Equal(b.Bounds, a.Bounds);
    }

    [Fact]
    public void TextBox_KeepsSize_CenterFollowsThePage()
    {
        var ta = new TextAnnotation { Position = new Point(10, 20), Width = 100, Height = 40 };
        AnnotationRotate.Remap([ta], 90, W, H);

        // Center (60,40) maps to (560,60); the box keeps 100x40 around it.
        Assert.Equal(510, ta.Position.X, 3);
        Assert.Equal(40, ta.Position.Y, 3);
        Assert.Equal(100, ta.Width, 3);
        Assert.Equal(40, ta.Height, 3);
    }

    [Fact]
    public void InkPoints_MapPerPoint()
    {
        var ia = new InkAnnotation { Points = [new Point(0, 0), new Point(100, 50)] };
        AnnotationRotate.Remap([ia], 90, W, H);
        Assert.Equal(new Point(H, 0), ia.Points[0]);
        Assert.Equal(new Point(H - 50, 100), ia.Points[1]);
    }
}
