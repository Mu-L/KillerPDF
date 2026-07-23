using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerPDF
{
    // App-wide accessibility size, ported from KillerNotes: a LayoutTransform scale on the
    // chrome (toolbar row, sidebar, tab strip) grows or shrinks the UI crisply -
    // LayoutTransform reflows and re-rasterizes text rather than bitmap-stretching it. The
    // title bar and footer stay fixed, so the logo you scroll to drive this (MainWindow.xaml,
    // LogoBar) never moves. The document pane is deliberately NOT scaled: app size and page
    // zoom are two separate controls. Persisted app-wide ("AppScale").
    public partial class MainWindow
    {
        internal double _appScale = 1.0;
        private const double AppScaleMin = 0.7, AppScaleMax = 2.5, AppScaleStep = 0.02;

        // The sidebar column lives in the UNSCALED grid (screen px) while its content lays
        // out at screen/scale logical px. Every site that pushes a logical sidebar width
        // (SidebarMinOpen, SidebarMaxPages, the 24px collapse strip...) into the column
        // converts through this, so the sidebar's LOGICAL width holds steady across scales
        // and the thumbnails grow with the rest of the chrome instead of being squeezed.
        internal double SbPx(double logical) => logical * _appScale;

        private void InitAppScale()
        {
            if (double.TryParse(App.GetSetting("AppScale"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out double s))
                ApplyAppScale(s);
        }

        // Roll the wheel over the logo: one small step per notch (fine-grained, no big jumps).
        private void LogoBar_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ApplyAppScale(_appScale + (e.Delta > 0 ? AppScaleStep : -AppScaleStep), persist: true);
            e.Handled = true;
        }

        // The logo is marked IsHitTestVisibleInChrome (MainWindow.xaml) so the scroll wheel
        // reaches it for the zoom above - but that also takes it out of WindowChrome's native
        // caption, so window drag and double-click-maximize are restored here by hand.
        private void LogoBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeBtn_Click(this, new RoutedEventArgs());   // WindowChrome.cs
                e.Handled = true;
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed && WindowState != WindowState.Maximized)
                DragMove();
        }

        private void ApplyAppScale(double scale, bool persist = false)
        {
            double prev = _appScale;
            scale = Math.Round(Math.Max(AppScaleMin, Math.Min(AppScaleMax, scale)), 3);
            _appScale = scale;
            // CHROME ONLY: toolbar, sidebar, and tab strip scale; the document pane is
            // deliberately untouched, so the app size and the page zoom stay two separate
            // controls.
            var t = scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);
            ToolbarRowBorder.LayoutTransform = t;
            SidebarOuterGrid.LayoutTransform = t;
            TabStripBorder.LayoutTransform   = t;
            // Keep the sidebar's LOGICAL width constant across the change: the column and
            // the saved widths are screen px, so grow them with the scale (see SbPx above).
            if (scale != prev && prev > 0)
            {
                double f = scale / prev;
                _savedPagesWidth    *= f;
                _savedOutlinesWidth *= f;
                if (_sidebarCol is { } col)
                {
                    if (col.Width.GridUnitType == GridUnitType.Pixel)
                        col.Width = new GridLength(col.Width.Value * f);
                    if (col.MinWidth > 0) col.MinWidth *= f;
                    if (!double.IsPositiveInfinity(col.MaxWidth)) col.MaxWidth *= f;
                }
            }
            if (persist)
            {
                App.SetSetting("AppScale", scale.ToString("0.###", CultureInfo.InvariantCulture));
                // Held: the chrome resize re-runs the fit pipeline, whose page/zoom status
                // would otherwise overwrite this the same frame (MainWindow.xaml.cs SetStatus).
                SetStatusHeld(string.Format(Loc("Str_St_AppSize"), (int)Math.Round(scale * 100)));
            }
        }
    }
}
