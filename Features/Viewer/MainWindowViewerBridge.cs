using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfSharpCore.Pdf;

namespace KillerPDF
{
    /// <summary>
    /// The window's half of the viewer bridge. Split pane stage 3 - read this alongside
    /// Controls/PdfViewer.Bridge.cs, which is the other end of every line here.
    ///
    /// TWO DIRECTIONS, and they are kept apart on purpose:
    ///
    ///   INWARD  - accessors the viewer reads. MainWindow's own members are private and a control
    ///             in another namespace cannot see them. Rather than widen ~40 fields in place and
    ///             scatter `internal` through fifteen files, each is exposed once, here, under a
    ///             name that says it is a bridge rather than an ordinary member. The private
    ///             fields stay private, so nothing else in the app gains reach by accident.
    ///
    ///   OUTWARD - stubs for the ~30 viewer methods that the rest of the window calls
    ///             (RenderPage from WindowChrome/SettingsPanel/PageSelection/Annotations,
    ///             SetViewMode from KeyboardShortcuts/RailFlyouts, and so on). Keeping the old
    ///             names resolvable meant roughly 60 call sites across 20 files were not touched
    ///             by this change at all.
    ///
    /// Both halves shrink as later stages move their owning code across; nothing here is meant to
    /// be permanent except what eventually becomes IViewerHost.
    /// </summary>
    public partial class MainWindow
    {
        // ══ INWARD: chrome the viewer reads ═════════════════════════════════════════════════
        // PageList is not here - x:Name fields are generated internal, so the control already
        // sees it. These four are hand-declared private fields assigned from FindName, so they
        // are not.
        internal ComboBox  ZoomBoxCtl        => _zoomBox;
        internal TextBox   PageJumpBoxCtl    => _pageJumpBox;
        internal Button    CloseFileBtnCtl   => _closeFileBtnRef;
        internal TextBlock PageTotalLabelCtl => _pageTotalLabel;

        internal string LocText(string key)   => Loc(key);
        internal void   SetStatusText(string text) => SetStatus(text);

        internal EditTool CurrentToolValue => _currentTool;
        internal bool VScrollVisible { get => _vScrollVisible; set => _vScrollVisible = value; }
        internal bool SpaceHeld => _spaceHeld;

        /// <summary>The window's ONE PageList selection delegate. Handed out rather than rebuilt
        /// because SyncCurrentPageTo detaches and reattaches it - a method group would make a new
        /// delegate per call and the -= would quietly remove nothing.</summary>
        internal SelectionChangedEventHandler PageListSelectionHandler
            => _pageListSelectionHandler ??= PageList_SelectionChanged;
        private SelectionChangedEventHandler? _pageListSelectionHandler;

        // ══ INWARD: per-document state (group B - goes when the viewer owns its session) ═════
        // Settable: the xref-repair path in Annotations.cs reopens the document and re-points the
        // temp file, and that code moved into the viewer in stage 4.
        internal PdfDocument? DocRef { get => _doc; set => _doc = value; }
        internal string? CurrentFileRef { get => _currentFile; set => _currentFile = value; }
        internal Dictionary<int, List<PageAnnotation>> AnnotationsRef => _annotations;
        internal Dictionary<int, (int w, int h)> RenderDimsRef => _renderDims;
        internal Dictionary<int, int> PageRotationsRef => _pageRotations;
        internal DocumentSession? ActiveSession => _active;
        internal List<Canvas> LinkOverlaysRef => _linkOverlays;
        // Direction REVERSED in stage 4: the link-rect map moved into the viewer with Links.cs, so
        // this now reads OUT of the control rather than exposing a window field. ContextMenu.cs and
        // FileOperations.cs still call it by the old name and were not touched.
        private Dictionary<int, List<LinkInfo>> _continuousLinks => Viewer.ContinuousLinks;

        // Live gesture state, shared with the annotation and crop tools that have not moved yet.
        internal bool   IsPanning     { get => _isPanning;     set => _isPanning = value; }
        internal Point  PanStart      { get => _panStart;      set => _panStart = value; }
        internal double PanScrollH    { get => _panScrollH;    set => _panScrollH = value; }
        internal double PanScrollV    { get => _panScrollV;    set => _panScrollV = value; }
        internal bool   IsDrawing     { get => _isDrawing;     set => _isDrawing = value; }
        internal Point  DrawStart     { get => _drawStart;     set => _drawStart = value; }
        internal UIElement? ActivePreview { get => _activePreview; set => _activePreview = value; }
        internal bool   IsSelecting   { get => _isSelecting;   set => _isSelecting = value; }
        internal Point  SelectStart   { get => _selectStart;   set => _selectStart = value; }
        internal Rectangle? SelectRect { get => _selectRect;   set => _selectRect = value; }
        internal int    CropPageIndex { get => _cropPageIndex; set => _cropPageIndex = value; }
        internal Rectangle? CropPreviewRect => _cropPreviewRect;
        internal Border?    CropConfirmBar  => _cropConfirmBar;

        // ══ OUTWARD: the viewer's members, under the names the window already calls ══════════
        // Signatures mirror the originals exactly, defaults included, so no call site changed.
        private void RenderPage(int pageIndex, bool keepTiles = false) => Viewer.RenderPage(pageIndex, keepTiles);
        private void SetupContinuousView(int initialPage, bool fitDefault = true) => Viewer.SetupContinuousView(initialPage, fitDefault);
        private System.Threading.Tasks.Task RenderContinuousPages(int centerPage) => Viewer.RenderContinuousPages(centerPage);
        private void BootstrapDocumentView(int initialPage, bool autoFit, bool restoreFitMode = false)
            => Viewer.BootstrapDocumentView(initialPage, autoFit, restoreFitMode);
        private void RefreshPageView(int pageIndex) => Viewer.RefreshPageView(pageIndex);
        private void ScrollContinuousToPage(int pageIndex) => Viewer.ScrollContinuousToPage(pageIndex);

        private void ApplyZoom(bool lite = false) => Viewer.ApplyZoom(lite);
        private void StartRerenderTimer() => Viewer.StartRerenderTimer();
        private void SetZoom(double level) => Viewer.SetZoom(level);
        private void SetTrueZoom(double trueZoom) => Viewer.SetTrueZoom(trueZoom);
        private void GridZoomStep(bool zoomOut) => Viewer.GridZoomStep(zoomOut);
        private double GridZoomForN(int n) => Viewer.GridZoomForN(n);
        private double DisplayZoomPct() => Viewer.DisplayZoomPct();
        private void SyncZoomBox() => Viewer.SyncZoomBox();
        private void FitToWidth(bool lite = false) => Viewer.FitToWidth(lite);
        private void FitToPage(bool lite = false) => Viewer.FitToPage(lite);
        private void ReapplyGridOrFit() => Viewer.ReapplyGridOrFit();

        private void SetViewMode(ViewMode mode) => Viewer.SetViewMode(mode);
        private void SelectViewMode(ViewMode mode) => Viewer.SelectViewMode(mode);
        private void ApplyViewMode(ViewMode mode) => Viewer.ApplyViewMode(mode);
        private ViewMode? _pendingViewMode { get => Viewer.PendingViewMode; set => Viewer.PendingViewMode = value; }

        private bool NavigatePageStep(int direction) => Viewer.NavigatePageStep(direction);
        private void NavigatePageByWheel(int delta) => Viewer.NavigatePageByWheel(delta);

        private int _gridColumns { get => Viewer.GridColumns; set => Viewer.GridColumns = value; }

        private void BuildPrimaryTile() => Viewer.BuildPrimaryTile();
        private void PagePreviewPanel_SizeChanged(object sender, SizeChangedEventArgs e)
            => Viewer.PagePreviewPanel_SizeChanged(sender, e);

        // Bound from MainWindow.xaml (the zoom toolbar stays on the window) and from
        // ContextMenu.cs, so these three cannot simply live on the control.
        private void ZoomIn_Click(object sender, RoutedEventArgs e) => Viewer.ZoomIn_Click(sender, e);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => Viewer.ZoomOut_Click(sender, e);

        // NULL-CONDITIONAL, and it must stay that way. ZoomBox declares
        // <ComboBoxItem Tag="1.0" IsSelected="True"> (MainWindow.xaml), so SelectionChanged fires
        // while InitializeComponent is still walking the tree - and ZoomBox is declared at line
        // ~1245 against Viewer's ~2179, so the generated Viewer field is still NULL when it does.
        // The old body opened with `if (_zoomBox?.SelectedItem is not ComboBoxItem item) return;`
        // and _zoomBox was assigned later from FindName, so it early-returned harmlessly during
        // construction; a plain forward dereferences Viewer before that guard can run and throws
        // NullReferenceException at startup. The `?.` restores exactly the old no-op.
        // Click handlers do not need this - a click cannot happen mid-parse.
        private void ZoomBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => Viewer?.ZoomBox_SelectionChanged(sender, e);
    }
}
