using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace KillerPDF
{
    // Configurable sidebar placement (left or right). The layout uses three columns in
    // MainContentGrid: one sized sidebar column, a 6px splitter, and a star document column.
    // ApplySidebarSide swaps which outer column is the sidebar and repoints _sidebarCol so the
    // existing collapse / resize logic keeps working unchanged.
    public partial class MainWindow
    {
        private void ApplySidebarSide()
        {
            var tabStrip      = FindName("TabStripBorder") as FrameworkElement;
            var tabScroll     = FindName("TabScroll") as FrameworkElement;
            if (FindName("SidebarCol") is not ColumnDefinition sidebarColDef ||
                FindName("DocCol") is not ColumnDefinition docColDef ||
                FindName("SidebarOuterGrid") is not Grid sbOuter ||
                FindName("SidebarBorder") is not Border sbContent ||
                FindName("SidebarToggleStrip") is not Border sbToggle ||
                FindName("DocPaneBorder") is not FrameworkElement docPane ||
                FindName("SbContentCol") is not ColumnDefinition sbContentCol ||
                FindName("SbToggleCol") is not ColumnDefinition sbToggleCol)
                return;

            // Carry the sized column's current width across a flip (24px when collapsed, else the
            // user's width). A star length means it isn't the sized column yet, so fall back.
            GridLength sized = (_sidebarCol != null && _sidebarCol.Width.GridUnitType == GridUnitType.Pixel)
                ? _sidebarCol.Width
                : new GridLength(SbPx(180));
            double maxW = SbPx(_sidebarShowingOutlines ? SidebarMaxOutlines : SidebarMaxPages);

            if (!_sidebarRight)
            {
                // Sidebar on the LEFT (column 0); document fills column 2.
                sidebarColDef.MinWidth = SbPx(_sidebarCollapsed ? 24 : SidebarMinOpen); sidebarColDef.MaxWidth = maxW; sidebarColDef.Width = sized;
                docColDef.MinWidth = 0; docColDef.MaxWidth = double.PositiveInfinity;
                docColDef.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(sbOuter, 0);
                Grid.SetColumn(docPane, 2);
                // Grainy tab band spans the splitter column (1) + document column (2) so it's one
                // continuous strip; the 6px tab offset cancels the wider band so tabs don't move.
                if (tabStrip != null) { Grid.SetColumn(tabStrip, 1); Grid.SetColumnSpan(tabStrip, 2); }
                if (tabScroll != null) tabScroll.Margin = new Thickness(6, 0, 0, 0);
                _sidebarCol = sidebarColDef;
                // Toggle strip faces the document: right edge of the sidebar.
                sbContentCol.Width = new GridLength(1, GridUnitType.Star);
                sbToggleCol.Width  = new GridLength(24, GridUnitType.Pixel);
                Grid.SetColumn(sbContent, 0);
                Grid.SetColumn(sbToggle, 1);
            }
            else
            {
                // Sidebar on the RIGHT (column 2); document fills column 0.
                docColDef.MinWidth = SbPx(_sidebarCollapsed ? 24 : SidebarMinOpen); docColDef.MaxWidth = maxW; docColDef.Width = sized;
                sidebarColDef.MinWidth = 0; sidebarColDef.MaxWidth = double.PositiveInfinity;
                sidebarColDef.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(sbOuter, 2);
                Grid.SetColumn(docPane, 0);
                // Band spans document column (0) + splitter column (1); tabs sit at the document edge
                // (col 0) with no offset, and the band extends right over the gap to the sidebar.
                if (tabStrip != null) { Grid.SetColumn(tabStrip, 0); Grid.SetColumnSpan(tabStrip, 2); }
                if (tabScroll != null) tabScroll.Margin = new Thickness(0, 0, 0, 0);
                _sidebarCol = docColDef;
                // Toggle strip faces the document: left edge of the sidebar (inner column 0). The
                // inner column defs are fixed in position, so size them by position, not by name.
                sbContentCol.Width = new GridLength(24, GridUnitType.Pixel);   // inner col 0 -> toggle
                sbToggleCol.Width  = new GridLength(1, GridUnitType.Star);     // inner col 1 -> content
                Grid.SetColumn(sbToggle, 0);
                Grid.SetColumn(sbContent, 1);
            }

            // The splitter's edge-line and the SidebarShadow gradient were both handled here. The
            // splitter draws a single centered line now, which is symmetric and so needs no side
            // handling, and the fake elevation gradient is gone - DocPaneBorder casts a real
            // PaneShadow on all four sides. (2026-07-31.)

            // The DocTopAccent / DocBottomAccent repositioning was here. Those two 1px rules are
            // gone with the squared layout - the card carries its own border on all four sides now,
            // so there is nothing left to bridge to the toolbar and footer. (2026-07-31.)

            // The document card's 8px inset always sits on its OUTER edge - the window side, away
            // from the splitter - so the gap reads as a margin off the window rather than a gutter
            // between the pane and the sidebar. Full screen clears the margin, so leave it alone
            // there; ApplyFullScreen restores it from this same helper on exit.
            // The grip rides the CONTENT column and faces the rail, so it always sits on the list's
            // inner lip: right edge with the sidebar on the left, left edge with it on the right.
            if (FindName("SidebarSplitter") is Thumb grip)
            {
                Grid.SetColumn(grip, _sidebarRight ? 1 : 0);
                grip.HorizontalAlignment = _sidebarRight ? HorizontalAlignment.Left
                                                         : HorizontalAlignment.Right;
            }

            // The shadow caster is a separate sibling (see MainWindow.xaml), so it has to track the
            // pane through every column swap and margin flip or the shadow detaches from the card.
            var docShadow = FindName("DocPaneShadow") as FrameworkElement;
            if (docShadow != null) Grid.SetColumn(docShadow, _sidebarRight ? 0 : 2);

            if (!_fullScreen)
            {
                docPane.Margin = DocPaneInsetMargin();
                if (docShadow != null) docShadow.Margin = docPane.Margin;
                // The tab band has to stop where the card stops, or it overhangs the card's rounded
                // outer top corner and squares it off. Outer inset only - the band's other edge is
                // the splitter column, which is where the card's -6 pull-back already puts its own.
                if (tabStrip != null)
                    tabStrip.Margin = _sidebarRight ? new Thickness(8, 0, 0, 0)
                                                    : new Thickness(0, 0, 8, 0);
            }

            UpdateSidebarToggleGlyph();
            UpdateTabStripFade();
            // The column swap repositions the document pane; re-anchor the footer shadow once layout
            // settles (TransformToVisual needs the final positions).
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)UpdateFooterFade);
        }

        // Document on the right (sidebar left) -> 8px inset on the right; mirrored when the sidebar
        // moves. Top and bottom stay 0: PaneShadow is Direction 270 and draws outside the element's
        // bounds without taking layout space, so a bottom gap would be real padding lifting the
        // card off the footer, not room for the shadow (KillerShell's ResultsPane comment).
        //
        // The sidebar side is 0. That 6px column is a plain gap now - KillerShell's TreeGapCol -
        // rather than something to pull back over: the grip moved inside the sidebar, so nothing
        // lives there and the grain layer paints it like the rest of the surface. It briefly held
        // a -6 pull-back while the grip was still a top-level GridSplitter. (Steve, 2026-07-31.)
        // Top is -1, KillerShell's ResultsPane margin verbatim: the card's own top border tucks
        // UNDER the tab band, which is opaque, so the active tab and the pane read as one surface
        // instead of being split by a hairline. With no tabs open the band is collapsed and the -1
        // just eats a pixel against the toolbar. (Steve, 2026-07-31: "remove the horizontal line
        // under the active tab so it blends into the pane".)
        private Thickness DocPaneInsetMargin()
            => _sidebarRight ? new Thickness(8, -1, 0, 0) : new Thickness(0, -1, 8, 0);

        // The grip drives SidebarCol's width directly (see the XAML comment). Dragging toward the
        // document grows the sidebar when it is on the left and shrinks it when on the right, so
        // the delta is signed by side. Clamped to the column's own Min/MaxWidth, which the collapse
        // and outline/pages modes already maintain, so this cannot drag past a readable minimum.
        private void SidebarGrip_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_sidebarCol == null) return;
            double w = _sidebarCol.ActualWidth + (_sidebarRight ? -e.HorizontalChange : e.HorizontalChange);
            double min = _sidebarCol.MinWidth > 0 ? _sidebarCol.MinWidth : SbPx(24);
            double max = double.IsPositiveInfinity(_sidebarCol.MaxWidth) ? double.MaxValue : _sidebarCol.MaxWidth;
            _sidebarCol.Width = new GridLength(Math.Max(min, Math.Min(max, w)));
        }

        // A Border with a CornerRadius does not clip its child, so the canvas, the grain and the
        // page itself all square the card's corners straight back off. Radius 5 = the card's 6
        // less its 1px border, which is where the inner edge of the curve actually falls.
        private void DocPane_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not FrameworkElement el) return;
            el.Clip = new RectangleGeometry(new Rect(0, 0, el.ActualWidth, el.ActualHeight), 5, 5);
        }


        // Clip the tab-strip shadow gradient to the document column so it never falls over the
        // sidebar (on whichever side the sidebar sits).
        private void UpdateTabStripFade()
        {
            // The tab-strip gradient band spans the splitter column + document column. Feather its
            // sidebar-facing edge (the same fixed-offset OpacityMask the footer uses) so it blends into
            // the sidebar instead of ending in a hard vertical cut. The document-facing edge keeps its
            // hard stop - that one is the tab/window edge and is meant to be crisp.
            if (TabStripFade != null)
            {
                TabStripFade.Margin = new Thickness(0);
                double w = TabStripFade.ActualWidth;
                if (w > 0)
                {
                    double f = Math.Min(0.5, 32.0 / w);   // wider (~32px) feather than the footer - the top
                                                          // shadow is darker, so a 15px fade still read as a
                                                          // hard vertical edge near the sidebar corner
                    var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    if (_sidebarRight)
                    {
                        mask.GradientStops.Add(new GradientStop(Colors.White, 0));
                        mask.GradientStops.Add(new GradientStop(Colors.White, 1 - f));
                        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                    }
                    else
                    {
                        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
                        mask.GradientStops.Add(new GradientStop(Colors.White, f));
                        mask.GradientStops.Add(new GradientStop(Colors.White, 1));
                    }
                    TabStripFade.OpacityMask = mask;
                }
                else TabStripFade.OpacityMask = null;
            }
            UpdateFooterFade();
        }

        // ROOT-CAUSE FIX for the recurring footer-shadow disappearance: position the footer shadow by
        // reading the DOCUMENT PANE's real on-screen position/width, not the sidebar width. The old code
        // clipped via _sidebarCol.ActualWidth and feathered via the fade's own ActualWidth - both read
        // mid-layout, so any shuffle (resize, toggle, restructure) left it mis-clipped and uncorrected.
        // Anchoring directly to the document is deterministic and self-corrects on every layout change.
        private void UpdateFooterFade()
        {
            if (FooterFade is null) return;
            // Direct generated x:Name fields instead of FindName - this runs on every resize tick.
            if (DocPaneBorder is not FrameworkElement doc) return;
            if (FooterBorder is not FrameworkElement footer) return;
            if (doc.ActualWidth <= 0 || footer.ActualWidth <= 0) return;
            try
            {
                double left  = doc.TransformToVisual(footer).Transform(new Point(0, 0)).X;
                double right = footer.ActualWidth - left - doc.ActualWidth;
                FooterFade.Margin = new Thickness(Math.Max(0, left), 0, Math.Max(0, right), 0);
                // Soft, FIXED-offset feather on the sidebar-facing edge (relative to the element, so it
                // can't go stale on width changes) - removes the hard vertical corner of the gradient.
                double f = Math.Min(0.4, 15.0 / doc.ActualWidth);   // ~15px feather regardless of width, so it reaches the corner
                var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                if (_sidebarRight)
                {
                    mask.GradientStops.Add(new GradientStop(Colors.White, 0));
                    mask.GradientStops.Add(new GradientStop(Colors.White, 1 - f));
                    mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                }
                else
                {
                    mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
                    mask.GradientStops.Add(new GradientStop(Colors.White, f));
                    mask.GradientStops.Add(new GradientStop(Colors.White, 1));
                }
                FooterFade.OpacityMask = mask;
            }
            catch { /* not laid out yet - a later layout pass will retry */ }
        }

        // The collapse arrow points toward where the page-list content goes when toggled, which
        // depends on both the side and the collapsed state.
        private void UpdateSidebarToggleGlyph()
        {
            if (_sidebarToggleBtn == null) return;
            bool pointLeft = _sidebarRight ? _sidebarCollapsed : !_sidebarCollapsed;
            _sidebarToggleBtn.Content = pointLeft ? "" : "";   // ChevronLeft / ChevronRight
        }

        private void SelectSidebarSide(bool right)
        {
            if (right == _sidebarRight) return;   // no change (e.g. picking the side it's already on)
            _sidebarRight = right;
            App.SetSetting("SidebarSide", right ? "Right" : "Left");
            ApplySidebarSide();
        }

        // Ctrl+Shift+B (pairs with Ctrl+B, the sidebar collapse toggle).
        private void ToggleSidebarSide() => SelectSidebarSide(!_sidebarRight);
    }
}
