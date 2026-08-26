using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KillerPDF.Services;

namespace KillerPDF.Controls
{
    public partial class PdfViewer
    {
        private void BeginFormFieldDrag(int _, Point position)
        {
            ClearSelection();
            _isDrawing = true;
            _drawStart = position;
            var preview = new Rectangle
            {
                Width = 0,
                Height = 0,
                Fill = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                Stroke = new SolidColorBrush(Color.FromRgb(220, 32, 32)),
                StrokeThickness = 2,
                StrokeDashArray = [4, 2],
                IsHitTestVisible = false
            };
            Canvas.SetLeft(preview, position.X);
            Canvas.SetTop(preview, position.Y);
            Panel.SetZIndex(preview, 20);
            _activeCanvas.Children.Add(preview);
            _activePreview = preview;
            _activeCanvas.CaptureMouse();
            SetStatus("Drag to create a fillable text field");
        }

        private void CommitFormFieldDrag(int pageIndex, Rectangle preview)
        {
            Rect canvasRect = new(
                Canvas.GetLeft(preview), Canvas.GetTop(preview),
                preview.Width, preview.Height);
            _activeCanvas?.Children.Remove(preview);
            if (canvasRect.Width < 12 || canvasRect.Height < 12)
            {
                SetStatus("Fillable text field canceled");
                return;
            }
            if (_currentFile is null || _activeCanvas is null)
                return;

            IReadOnlyList<KillerPdf.Engine.Documents.PdfPageInformation> pages =
                PdfEngineIntegration.ReadPageInformation(_currentFile);
            if ((uint)pageIndex >= (uint)pages.Count)
                return;
            KillerPdf.Engine.Documents.PdfPageInformation page = pages[pageIndex];
            int rotation = _pageRotations.TryGetValue(pageIndex, out int storedRotation)
                ? ((storedRotation % 360) + 360) % 360
                : page.Rotation;
            double canvasWidth = Math.Max(1, _activeCanvas.ActualWidth);
            double canvasHeight = Math.Max(1, _activeCanvas.ActualHeight);
            (double x1, double y1, double x2, double y2) = CanvasToPdfRect(
                canvasRect, page.Width, page.Height, canvasWidth, canvasHeight, rotation);

            string? fieldName = null;
            SaveTempAndReload(
                keepAnnotations: true,
                preserveZoom: true,
                finalizeSavedFile: path => fieldName = PdfEngineIntegration.AddTextField(
                    path, pageIndex, x1, y1, x2 - x1, y2 - y1),
                selectedPageAfterReload: pageIndex);
            SetStatus(fieldName is null
                ? "Could not create the fillable text field"
                : $"Created fillable text field {fieldName}");
        }
    }
}
