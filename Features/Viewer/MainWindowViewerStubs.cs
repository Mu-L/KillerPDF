using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PdfSharpCore.Pdf;

namespace KillerPDF
{
    /// <summary>
    /// Stage 4's outward stubs: the annotation, text, crop, form and link members under the names
    /// the rest of the window already calls them by.
    ///
    /// The point of this file is that ~170 call sites across ContextMenu.cs, TextSettingsBar.cs,
    /// KeyboardShortcuts.cs, FileOperations.cs, Tabs.cs, Signing.cs, Shapes.cs, Search.cs,
    /// SidebarOutline.cs, Stamps.cs, ToolSelection.cs, TempReload.cs, Rotate.cs, Ocr.cs and
    /// DirtyTracking.cs did not change at all. Same technique as stage 3's outward block, applied
    /// to a much bigger surface.
    ///
    /// THE XAML ONES ARE NOT OPTIONAL. WPF resolves Click="Undo_Click" against the code-behind of
    /// the XAML ROOT - MainWindow - not against whichever class the method ended up in. Eleven
    /// handlers in MainWindow.xaml point at members that now live in the viewer, and without these
    /// declarations InitializeComponent throws XamlParseException before the window ever appears.
    /// Verified against MainWindow.xaml rather than remembered: Undo_Click (2 bindings),
    /// ClearAllAnnotations_Click (2), PageJumpBox_KeyDown, PageJumpBox_GotFocus,
    /// PageList_SelectionChanged, ShortcutHelp_Click, ShortcutOverlay_MouseLeftButtonDown (2),
    /// ShortcutOverlayCard_MouseLeftButtonDown (2), ShortcutOverlayClose_Click,
    /// Hyperlink_RequestNavigate.
    /// </summary>
    public partial class MainWindow
    {
        // ── Annotations ──────────────────────────────────────────────────────────────────────
        private void RenderAllAnnotations(int pageIndex) => Viewer.RenderAllAnnotations(pageIndex);
        private void ClearSelection() => Viewer.ClearSelection();
        private void ClearTextSelection() => Viewer.ClearTextSelection();
        private SolidColorBrush AccentBrush(byte alpha = 255) => Viewer.AccentBrush(alpha);
        private void AddAnnotation(PageAnnotation a) => Viewer.AddAnnotationExt(a);
        private Rect AnnotBounds(PageAnnotation a) => Viewer.AnnotBoundsExt(a);
        private static Point AnnotGetPos(PageAnnotation a) => Controls.PdfViewer.AnnotGetPosExt(a);
        private static void AnnotSetPos(PageAnnotation a, Point pos) => Controls.PdfViewer.AnnotSetPosExt(a, pos);
        private Point ClampAnnotPos(PageAnnotation a) => Viewer.ClampAnnotPosExt(a);
        private bool HitTestAnnotation(PageAnnotation a, Point pos, out Rect bounds)
            => Viewer.HitTestAnnotationExt(a, pos, out bounds);
        private static bool IsDraggable(PageAnnotation a) => Controls.PdfViewer.IsDraggableExt(a);
        private void SelectAnnotation(PageAnnotation a, Rect bounds) => Viewer.SelectAnnotationExt(a, bounds);
        private void ToggleMultiSelect(PageAnnotation a, Rect bounds, Canvas canvas)
            => Viewer.ToggleMultiSelectExt(a, bounds, canvas);
        private void SelectGroup(PageAnnotation lead) => Viewer.SelectGroupExt(lead);
        private PageAnnotation? SelectedPaired() => Viewer.SelectedPairedExt();
        private int SelectionCount() => Viewer.SelectionCountExt();
        private void ReattachSelectionVisuals() => Viewer.ReattachSelectionVisualsExt();
        private void UnpairSelected() => Viewer.UnpairSelectedExt();
        private void GroupSelected() => Viewer.GroupSelectedExt();
        private void UngroupAnnotation(PageAnnotation a) => Viewer.UngroupAnnotationExt(a);
        private void RemoveFromGroup(PageAnnotation a) => Viewer.RemoveFromGroupExt(a);
        private void DeleteSelected() => Viewer.DeleteSelectedExt();
        private bool SelectAllAnnotations() => Viewer.SelectAllAnnotationsExt();
        private void HideBrushPreview() => Viewer.HideBrushPreviewExt();
        private void FinishStuckGesture() => Viewer.FinishStuckGestureExt();
        private void RefreshSelectionAccent() => Viewer.RefreshSelectionAccentExt();

        // ── Page canvases ────────────────────────────────────────────────────────────────────
        private Canvas CanvasForPage(int page) => Viewer.CanvasForPageExt(page);
        private Canvas? VisibleCanvasForPage(int page) => Viewer.VisibleCanvasForPageExt(page);
        private IEnumerable<Canvas> AllPageCanvases() => Viewer.AllPageCanvasesExt();

        // ── Undo ─────────────────────────────────────────────────────────────────────────────
        private void PushDocUndo() => Viewer.PushDocUndoExt();
        private void PushPageSnapshotUndo(int pageIdx) => Viewer.PushPageSnapshotUndoExt(pageIdx);

        // ── Text editing ─────────────────────────────────────────────────────────────────────
        private void CommitActiveTextBox() => Viewer.CommitActiveTextBoxExt();
        private void RemoveTextEditHandles() => Viewer.RemoveTextEditHandlesExt();
        private void EditTextAtPosition(Point canvasPos, int pageIdx) => Viewer.EditTextAtPositionExt(canvasPos, pageIdx);
        private void PlaceTextBox(Point pos, int pageIdx) => Viewer.PlaceTextBoxExt(pos, pageIdx);
        private Brush TextEditBackground() => Viewer.TextEditBackgroundExt();
        private static ControlTemplate FlatTextBoxTemplate() => Controls.PdfViewer.FlatTextBoxTemplateExt();

        // ── Text selection ───────────────────────────────────────────────────────────────────
        private void CopySelectedText() => Viewer.CopySelectedTextExt();
        private void SelectAllText() => Viewer.SelectAllTextExt();

        // ── Crop ─────────────────────────────────────────────────────────────────────────────
        private void ApplyCrop(int[] pageIndices) => Viewer.ApplyCropExt(pageIndices);
        private void HideCropConfirmBar() => Viewer.HideCropConfirmBarExt();
        private void ShowDefaultCropBox() => Viewer.ShowDefaultCropBoxExt();
        private void RebuildCropBarForLocale() => Viewer.RebuildCropBarForLocaleExt();

        // ── Links ────────────────────────────────────────────────────────────────────────────
        private void CloseLinkPdfiumDoc() => Viewer.CloseLinkPdfiumDocExt();
        private void AddLinkMenuItems(ContextMenu menu, object target, int annotIndex, int pageIndex)
            => Viewer.AddLinkMenuItemsExt(menu, target, annotIndex, pageIndex);
        private int? ResolveDest(PdfItem? destItem) => Viewer.ResolveDestExt(destItem);
        private const double LinkHitPad = Controls.PdfViewer.LinkHitPadShared;
        internal const string ConfirmLinksSetting = Controls.PdfViewer.ConfirmLinksSetting;

        // ── Save paths ───────────────────────────────────────────────────────────────────────
        private void DrawAnnotationsOnDocument(int? onlyPage = null) => Viewer.DrawAnnotationsOnDocumentExt(onlyPage);
        private void WriteFormValuesToDocument() => Viewer.WriteFormValuesToDocumentExt();

        // ── Bound from MainWindow.xaml - see the class comment, these are load-bearing ───────
        private void Undo_Click(object sender, RoutedEventArgs e) => Viewer.UndoClickExt(sender, e);
        private void Redo_Click(object sender, RoutedEventArgs e) => Viewer.RedoClickExt(sender, e);
        private void ClearAnnotations_Click(object sender, RoutedEventArgs e) => Viewer.ClearAnnotationsClickExt(sender, e);
        private void ClearAllAnnotations_Click(object sender, RoutedEventArgs e) => Viewer.ClearAllAnnotationsClickExt(sender, e);
        private void PageJumpBox_KeyDown(object sender, KeyEventArgs e) => Viewer.PageJumpBoxKeyDownExt(sender, e);
        private void PageJumpBox_GotFocus(object sender, RoutedEventArgs e) => Viewer.PageJumpBoxGotFocusExt(sender, e);
        private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => Viewer.PageListSelectionChangedExt(sender, e);
        private void ShortcutHelp_Click(object sender, RoutedEventArgs e) => Viewer.ShortcutHelpClickExt(sender, e);
        private void ShortcutOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => Viewer.ShortcutOverlayMouseDownExt(sender, e);
        private void ShortcutOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => Viewer.ShortcutOverlayCardMouseDownExt(sender, e);
        private void ShortcutOverlayClose_Click(object sender, RoutedEventArgs e)
            => Viewer.ShortcutOverlayCloseClickExt(sender, e);
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
            => Viewer.HyperlinkRequestNavigateExt(sender, e);
    }
}
