using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KillerPDF.Services;
using KillerPdf.Engine.Documents;

namespace KillerPDF.Controls;

public partial class PdfViewer
{
    private Line? _measurementLine;
    private Line? _measurementStartCap;
    private Line? _measurementEndCap;
    private Border? _measurementReadout;
    private int _measurementPage = -1;
    private PdfPageInformation? _measurementPageInfo;
    private (int w, int h) _measurementRenderSize;

    private void BeginMeasurement(int pageIndex, Point start)
    {
        ClearMeasurement();
        _measurementPage = pageIndex;
        if (_currentFile is not null)
        {
            try
            {
                var pages = PdfEngineIntegration.ReadPageInformation(_currentFile);
                if ((uint)pageIndex < (uint)pages.Count && _renderDims.TryGetValue(pageIndex, out var render))
                {
                    _measurementPageInfo = pages[pageIndex];
                    _measurementRenderSize = render;
                }
            }
            catch
            {
                _measurementPageInfo = null;
            }
        }
        _drawStart = start;
        _isDrawing = true;

        Brush accent = AccentBrush();
        _measurementLine = new Line
        {
            X1 = start.X, Y1 = start.Y, X2 = start.X, Y2 = start.Y,
            Stroke = accent, StrokeThickness = 2, IsHitTestVisible = false
        };
        _measurementStartCap = MeasurementCap(accent);
        _measurementEndCap = MeasurementCap(accent);
        _measurementReadout = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 32, 32, 32)),
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 5, 8, 5),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11
            }
        };

        foreach (UIElement element in new UIElement[]
                 { _measurementLine, _measurementStartCap, _measurementEndCap, _measurementReadout })
        {
            Panel.SetZIndex(element, 9500);
            _activeCanvas.Children.Add(element);
        }
        _activePreview = _measurementLine;
        UpdateMeasurement(start);
        _activeCanvas.CaptureMouse();
    }

    private static Line MeasurementCap(Brush brush) => new()
    {
        Stroke = brush,
        StrokeThickness = 2,
        IsHitTestVisible = false
    };

    private void UpdateMeasurement(Point end)
    {
        if (_measurementLine is null || _measurementStartCap is null ||
            _measurementEndCap is null || _measurementReadout is null ||
            _measurementPage < 0 || _currentFile is null) return;

        _measurementLine.X2 = end.X;
        _measurementLine.Y2 = end.Y;

        Vector direction = end - _drawStart;
        double length = direction.Length;
        Vector normal = length > 0.001
            ? new Vector(-direction.Y / length * 6, direction.X / length * 6)
            : new Vector(0, 6);
        PositionCap(_measurementStartCap, _drawStart, normal);
        PositionCap(_measurementEndCap, end, normal);

        string text = MeasurementText(direction);
        if (_measurementReadout.Child is TextBlock tb) tb.Text = text;
        _measurementReadout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double x = Math.Min(Math.Max(4, end.X + 12),
            Math.Max(4, _activeCanvas.ActualWidth - _measurementReadout.DesiredSize.Width - 4));
        double y = Math.Min(Math.Max(4, end.Y + 12),
            Math.Max(4, _activeCanvas.ActualHeight - _measurementReadout.DesiredSize.Height - 4));
        Canvas.SetLeft(_measurementReadout, x);
        Canvas.SetTop(_measurementReadout, y);
    }

    private static void PositionCap(Line cap, Point center, Vector normal)
    {
        cap.X1 = center.X - normal.X;
        cap.Y1 = center.Y - normal.Y;
        cap.X2 = center.X + normal.X;
        cap.Y2 = center.Y + normal.Y;
    }

    private string MeasurementText(Vector canvasDelta)
    {
        if (_measurementPageInfo is null || _measurementRenderSize.w <= 0 || _measurementRenderSize.h <= 0)
            return "Measurement unavailable";

        MeasurementValues value = MeasurementCalculator.Calculate(
            _measurementPageInfo.Width, _measurementPageInfo.Height,
            _measurementPageInfo.Rotation, _measurementRenderSize.w, _measurementRenderSize.h,
            canvasDelta.X, canvasDelta.Y);
        return string.Create(CultureInfo.CurrentCulture,
            $"{value.Inches:0.###} in  |  {value.Millimetres:0.##} mm  |  {value.Points:0.##} pt\n" +
            $"Page {value.PageWidthPoints / 72.0:0.##} × {value.PageHeightPoints / 72.0:0.##} in  |  " +
            $"{value.PageWidthPoints / 72.0 * 25.4:0.#} × {value.PageHeightPoints / 72.0 * 25.4:0.#} mm");
    }

    private void FinishMeasurement(Point end)
    {
        UpdateMeasurement(end);
        _isDrawing = false;
        _activePreview = null;
        _activeCanvas.ReleaseMouseCapture();
    }

    private void ClearMeasurement()
    {
        foreach (UIElement? element in new UIElement?[]
                 { _measurementLine, _measurementStartCap, _measurementEndCap, _measurementReadout })
            if (element is not null)
                (VisualTreeHelper.GetParent(element) as Panel)?.Children.Remove(element);
        _measurementLine = null;
        _measurementStartCap = null;
        _measurementEndCap = null;
        _measurementReadout = null;
        _measurementPage = -1;
        _measurementPageInfo = null;
        _measurementRenderSize = default;
    }
}
