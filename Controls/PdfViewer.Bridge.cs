using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfSharpCore.Pdf;

namespace KillerPDF.Controls
{
    /// <summary>
    /// Everything the moved render pipeline still reaches for on the window, forwarded to Owner.
    /// Split pane stage 3.
    ///
    /// WHY THIS FILE EXISTS. PdfViewer.Viewport.cs and PdfViewer.Zoom.cs moved across VERBATIM -
    /// roughly 2,100 lines carrying about 700 references to window members spelled bare (PageList,
    /// _doc, Loc, RenderAllAnnotations...). Rewriting those 700 sites in the same change that moved
    /// the files would have been unreviewable. Declaring the names here instead means the moved
    /// files did not change a character of logic, and the entire coupling surface between viewer
    /// and window is one readable list - which is exactly the list stages 4 to 6 work through.
    ///
    /// THIS IS SCAFFOLDING, NOT THE DESTINATION. Read the groups below as a to-do list:
    ///   - Group B is per-DOCUMENT state that belongs in DocumentSession. When the viewer holds
    ///     its own active session, that block deletes itself.
    ///   - Group C members live in files that move in stage 4. Each deletes itself as its defining
    ///     file arrives; the defining file is named against every one.
    ///   - Group A is the only group meant to survive, and it should end up expressed as
    ///     IViewerHost rather than as raw Owner reach.
    ///
    /// Owner is null only between construction and the window wiring it up, which happens in the
    /// MainWindow constructor before any of this can run. The null-forgiving operator is therefore
    /// deliberate: a null here is a wiring bug and should throw loudly, not render nothing.
    /// </summary>
    public partial class PdfViewer
    {
        private MainWindow W => Owner!;

        // ── The view's own state ─────────────────────────────────────────────────────────────
        // Not forwards: this viewer OWNS its ViewerState (see PdfViewer.xaml.cs). These mirror the
        // window's forwarding properties one for one, so the moved code reads identically.
        private ViewMode _viewMode { get => State.Mode; set => State.Mode = value; }
        private ViewMode? _pendingViewMode { get => State.Pending; set => State.Pending = value; }
        private double _zoomLevel { get => State.ZoomLevel; set => State.ZoomLevel = value; }
        private double _lastRenderZoom { get => State.LastRenderZoom; set => State.LastRenderZoom = value; }
        private int _renderedPrimaryPage { get => State.RenderedPrimaryPage; set => State.RenderedPrimaryPage = value; }
        private FitMode _fitMode { get => State.Fit; set => State.Fit = value; }
        private System.Windows.Threading.DispatcherTimer? _rerenderTimer { get => State.RerenderTimer; set => State.RerenderTimer = value; }
        private System.Threading.CancellationTokenSource? _secondaryRenderCts { get => State.SecondaryRenderCts; set => State.SecondaryRenderCts = value; }
        private System.Threading.CancellationTokenSource? _continuousRenderCts { get => State.ContinuousRenderCts; set => State.ContinuousRenderCts = value; }
        private System.Threading.CancellationTokenSource? _continuousSharpenCts { get => State.ContinuousSharpenCts; set => State.ContinuousSharpenCts = value; }
        private HashSet<int> _continuousSharpPages => State.ContinuousSharpPages;
        private int _continuousSharpW { get => State.ContinuousSharpW; set => State.ContinuousSharpW = value; }
        private List<double> _continuousTops => State.ContinuousTops;
        private int _gridScrollToPage { get => State.GridScrollToPage; set => State.GridScrollToPage = value; }
        private int _continuousScrollTarget { get => State.ContinuousScrollTarget; set => State.ContinuousScrollTarget = value; }
        private double _continuousPageW { get => State.ContinuousPageW; set => State.ContinuousPageW = value; }
        private Dictionary<int, Canvas> _pages => State.Pages;
        private Dictionary<int, Canvas> _continuousCanvases => State.ContinuousCanvases;
        private Canvas _annotationCanvas { get => State.AnnotationCanvas; set => State.AnnotationCanvas = value; }
        private Canvas _activeCanvas { get => State.ActiveCanvas; set => State.ActiveCanvas = value; }
        private Canvas? _gestureCanvas { get => State.GestureCanvas; set => State.GestureCanvas = value; }
        private int _gesturePage { get => State.GesturePage; set => State.GesturePage = value; }
        private Image PageImage { get => State.PageImage; set => State.PageImage = value; }
        private StackPanel _continuousPanel { get => State.ContinuousPanel; set => State.ContinuousPanel = value; }
        private WrapPanel _pageContentPanel { get => State.PageContentPanel; set => State.PageContentPanel = value; }
        private Grid _pageContentGrid { get => State.PageContentGrid; set => State.PageContentGrid = value; }

        // ── Group A: host chrome and services ────────────────────────────────────────────────
        // One toolbar, one sidebar, one status line serving both panes. These are the forwards
        // meant to survive, and they should become IViewerHost calls rather than raw Owner reach.
        private ListBox PageList => W.PageList;
        private ComboBox _zoomBox => W.ZoomBoxCtl;
        private TextBox _pageJumpBox => W.PageJumpBoxCtl;
        private Button _closeFileBtnRef => W.CloseFileBtnCtl;
        private TextBlock _pageTotalLabel => W.PageTotalLabelCtl;

        private string Loc(string key) => W.LocText(key);
        private void SetStatus(string text) => W.SetStatusText(text);
        private void RepositionAnnotationBars() => W.RepositionAnnotationBars();

        private EditTool _currentTool => W.CurrentToolValue;
        private bool _vScrollVisible { get => W.VScrollVisible; set => W.VScrollVisible = value; }
        private bool _spaceHeld => W.SpaceHeld;

        // Zoom limits stay defined on the window: MainWindow.xaml.cs and KeyboardShortcuts.cs read
        // them too, and a const aliases at no cost rather than being duplicated.
        private const double ZoomMin  = MainWindow.ZoomMin;
        private const double ZoomMax  = MainWindow.ZoomMax;
        private const double ZoomStep = MainWindow.ZoomStep;

        // ── Group B: per-document state ──────────────────────────────────────────────────────
        // NOT host services. Every one of these already rides in DocumentSession, which tab
        // switching swaps by reference. They forward for now because the window still owns the
        // active session; when the viewer holds its own, this whole block goes.
        private PdfDocument? _doc { get => W.DocRef; set => W.DocRef = value; }
        private string? _currentFile { get => W.CurrentFileRef; set => W.CurrentFileRef = value; }
        private Dictionary<int, List<PageAnnotation>> _annotations => W.AnnotationsRef;
        private Dictionary<int, (int w, int h)> _renderDims => W.RenderDimsRef;
        private Dictionary<int, int> _pageRotations => W.PageRotationsRef;
        private MainWindow.DocumentSession? _active => W.ActiveSession;
        private List<Canvas> _linkOverlays => W.LinkOverlaysRef;
        // _continuousLinks is no longer forwarded - the field itself arrived with Links.cs and the
        // viewer owns it now. ContextMenu.cs and FileOperations.cs read it from the window side
        // through MainWindowViewerBridge's ContinuousLinksRef, which points back here.

        // Live gesture state shared with the annotation and crop tools, which have not moved yet.
        private bool _isPanning { get => W.IsPanning; set => W.IsPanning = value; }
        private Point _panStart { get => W.PanStart; set => W.PanStart = value; }
        private double _panScrollH { get => W.PanScrollH; set => W.PanScrollH = value; }
        private double _panScrollV { get => W.PanScrollV; set => W.PanScrollV = value; }
        private bool _isDrawing { get => W.IsDrawing; set => W.IsDrawing = value; }
        private Point _drawStart { get => W.DrawStart; set => W.DrawStart = value; }
        private UIElement? _activePreview { get => W.ActivePreview; set => W.ActivePreview = value; }
        private bool _isSelecting { get => W.IsSelecting; set => W.IsSelecting = value; }
        private Point _selectStart { get => W.SelectStart; set => W.SelectStart = value; }
        private Rectangle? _selectRect { get => W.SelectRect; set => W.SelectRect = value; }
        private int _cropPageIndex { get => W.CropPageIndex; set => W.CropPageIndex = value; }
        // Get-only in stage 3; stage 4 brought Crop.cs and Annotations.cs, which ASSIGN both, so
        // these now go through the settable pair on the window side.
        private Rectangle? _cropPreviewRect { get => W.CropPreviewRectSet; set => W.CropPreviewRectSet = value; }
        private Border? _cropConfirmBar { get => W.CropConfirmBarSet; set => W.CropConfirmBarSet = value; }

        // ── Group C: methods in partials that have NOT moved yet ─────────────────────────────
        // Stage 4 emptied most of this block: RenderAllAnnotations, ClearSelection, UpdateMarquee,
        // IsDescendantOf, the four Canvas_Mouse* handlers, ClearTextSelection, AccentBrush,
        // RenderPageLinks, AddSecondaryPageLinks and the PageList_SelectionChanged delegate all
        // arrived with their defining files and are now real members of this class. What is left is
        // the four whose files stay on the window.
        private void PopulateContextMenu(Point pt, int page) => W.PopulateContextMenu(pt, page); // ContextMenu.cs
        private void RefreshPageList() => W.RefreshPageList();                              // PageOperations.cs
        private void LoadOutlines() => W.LoadOutlines();                                    // SidebarOutline.cs
        private static Cursor CursorForTool(EditTool t) => MainWindow.CursorForTool(t);     // ToolSelection.cs

        // ── Render cache, still on Tabs.cs ───────────────────────────────────────────────────
        private static System.Windows.Media.Imaging.BitmapSource? TryGetCachedRender(
            MainWindow.DocumentSession? s, int page, int bucket, int rot)
            => MainWindow.TryGetCachedRender(s, page, bucket, rot);
        private static void CacheRender(MainWindow.DocumentSession? s, int page, int bucket, int rot,
                                        System.Windows.Media.Imaging.BitmapSource bmp)
            => MainWindow.CacheRender(s, page, bucket, rot, bmp);
    }
}
