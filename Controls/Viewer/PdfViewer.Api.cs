using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PdfSharpCore.Pdf;

namespace KillerPDF.Controls
{
    /// <summary>
    /// The viewer's outward surface: what the window still calls into.
    ///
    /// WHY A FACADE RATHER THAN WIDENING. Roughly 55 of these are private members of the seven
    /// moved files. Making them internal in place would mean 55 edits scattered through code that
    /// is otherwise VERBATIM - and "verbatim" is the property that makes the move reviewable by
    /// diff. This file is part of the same partial class, so it can see those privates and
    /// re-expose them without touching a line of the moved code.
    ///
    /// The `Ext` suffix exists only because a wrapper cannot share a name with the member it wraps
    /// inside one class. Members that were already internal (RenderAllAnnotations, ClearSelection,
    /// ClearTextSelection, AccentBrush) are absent here - the window calls those directly.
    ///
    /// Every entry is one call site away from deletion: when a caller moves into the viewer, its
    /// line here goes with it.
    /// </summary>
    public partial class PdfViewer
    {
        // ── Annotations: selection, hit-testing, geometry ────────────────────────────────────
        internal void AddAnnotationExt(PageAnnotation a) => AddAnnotation(a);
        internal Rect AnnotBoundsExt(PageAnnotation a) => AnnotBounds(a);
        internal static Point AnnotGetPosExt(PageAnnotation a) => AnnotGetPos(a);
        internal static void AnnotSetPosExt(PageAnnotation a, Point pos) => AnnotSetPos(a, pos);
        internal Point ClampAnnotPosExt(PageAnnotation a) => ClampAnnotPos(a);
        internal bool HitTestAnnotationExt(PageAnnotation a, Point pos, out Rect bounds)
            => HitTestAnnotation(a, pos, out bounds);
        internal static bool IsDraggableExt(PageAnnotation a) => IsDraggable(a);
        internal void SelectAnnotationExt(PageAnnotation a, Rect bounds) => SelectAnnotation(a, bounds);
        internal void ToggleMultiSelectExt(PageAnnotation a, Rect bounds, Canvas canvas)
            => ToggleMultiSelect(a, bounds, canvas);
        internal void SelectGroupExt(PageAnnotation lead) => SelectGroup(lead);
        internal PageAnnotation? SelectedPairedExt() => SelectedPaired();
        internal int SelectionCountExt() => SelectionCount();
        internal void ReattachSelectionVisualsExt() => ReattachSelectionVisuals();
        internal void UnpairSelectedExt() => UnpairSelected();
        internal void GroupSelectedExt() => GroupSelected();
        internal void UngroupAnnotationExt(PageAnnotation a) => UngroupAnnotation(a);
        internal void RemoveFromGroupExt(PageAnnotation a) => RemoveFromGroup(a);
        internal void DeleteSelectedExt() => DeleteSelected();
        internal bool SelectAllAnnotationsExt() => SelectAllAnnotations();
        internal void HideBrushPreviewExt() => HideBrushPreview();
        internal void FinishStuckGestureExt() => FinishStuckGesture();
        internal void RefreshSelectionAccentExt() => RefreshSelectionAccent();

        // ── Page canvases ────────────────────────────────────────────────────────────────────
        internal Canvas CanvasForPageExt(int page) => CanvasForPage(page);
        internal Canvas? VisibleCanvasForPageExt(int page) => VisibleCanvasForPage(page);
        internal IEnumerable<Canvas> AllPageCanvasesExt() => AllPageCanvases();

        // ── Undo / commands bound from MainWindow.xaml and the context menu ──────────────────
        internal void PushDocUndoExt() => PushDocUndo();
        internal void PushPageSnapshotUndoExt(int pageIdx) => PushPageSnapshotUndo(pageIdx);
        internal void UndoClickExt(object sender, RoutedEventArgs e) => Undo_Click(sender, e);
        internal void RedoClickExt(object sender, RoutedEventArgs e) => Redo_Click(sender, e);
        internal void ClearAnnotationsClickExt(object sender, RoutedEventArgs e) => ClearAnnotations_Click(sender, e);
        internal void ClearAllAnnotationsClickExt(object sender, RoutedEventArgs e) => ClearAllAnnotations_Click(sender, e);

        // ── Text editing ─────────────────────────────────────────────────────────────────────
        internal void CommitActiveTextBoxExt() => CommitActiveTextBox();
        internal void RemoveTextEditHandlesExt() => RemoveTextEditHandles();
        internal void EditTextAtPositionExt(Point canvasPos, int pageIdx) => EditTextAtPosition(canvasPos, pageIdx);
        internal void PlaceTextBoxExt(Point pos, int pageIdx) => PlaceTextBox(pos, pageIdx);
        internal Brush TextEditBackgroundExt() => TextEditBackground();
        internal static ControlTemplate FlatTextBoxTemplateExt() => FlatTextBoxTemplate();

        // ── Text selection ───────────────────────────────────────────────────────────────────
        internal void CopySelectedTextExt() => CopySelectedText();
        internal void SelectAllTextExt() => SelectAllText();

        // ── Crop ─────────────────────────────────────────────────────────────────────────────
        internal void ApplyCropExt(int[] pageIndices) => ApplyCrop(pageIndices);
        internal void HideCropConfirmBarExt() => HideCropConfirmBar();
        internal void ShowDefaultCropBoxExt() => ShowDefaultCropBox();
        internal void RebuildCropBarForLocaleExt() => RebuildCropBarForLocale();

        // ── Links ────────────────────────────────────────────────────────────────────────────
        internal void CloseLinkPdfiumDocExt() => CloseLinkPdfiumDoc();
        internal void AddLinkMenuItemsExt(ContextMenu menu, object target, int annotIndex, int pageIndex)
            => AddLinkMenuItems(menu, target, annotIndex, pageIndex);
        internal int? ResolveDestExt(PdfItem? destItem) => ResolveDest(destItem);
        internal bool IsPanning => _isPanning;
        internal EditTool CurrentToolRef { get => _currentTool; set => _currentTool = value; }
        internal PdfDocument? DocumentRef { get => _doc; set => _doc = value; }
        internal string? CurrentFileRef { get => _currentFile; set => _currentFile = value; }
        internal Dictionary<int, List<PageAnnotation>> AnnotationsRef { get => _annotations; set => _annotations = value; }
        internal Dictionary<int, (int w, int h)> RenderDimsRef { get => _renderDims; set => _renderDims = value; }
        internal Dictionary<int, int> PageRotationsRef { get => _pageRotations; set => _pageRotations = value; }
        internal bool IsDrawingRef { get => _isDrawing; set => _isDrawing = value; }
        internal Point DrawStartRef { get => _drawStart; set => _drawStart = value; }
        internal UIElement? ActivePreviewRef { get => _activePreview; set => _activePreview = value; }
        internal System.Windows.Shapes.Rectangle? CropPreviewRectRef { get => _cropPreviewRect; set => _cropPreviewRect = value; }
        internal Border? CropConfirmBarRef { get => _cropConfirmBar; set => _cropConfirmBar = value; }
        internal PageAnnotation? SelectedAnnotationRef { get => _selectedAnnotation; set => _selectedAnnotation = value; }
        internal Border? SelectionBorderRef { get => _selectionBorder; set => _selectionBorder = value; }
        internal List<PageAnnotation> SelectedSetRef => _selectedSet;
        internal List<Border> SelectionOutlinesRef => _selectionOutlines;
        internal System.Windows.Shapes.Rectangle? PairedCoverOutlineRef { get => _pairedCoverOutline; set => _pairedCoverOutline = value; }
        internal System.Windows.Shapes.Rectangle? ReeditCoverOutlineRef { get => _reeditCoverOutline; set => _reeditCoverOutline = value; }
        internal string? SelectedTextRef { get => _selectedText; set => _selectedText = value; }
        internal List<(PageAnnotation a, Point orig)> DragGroupOrigRef => _dragGroupOrig;
        internal Color DrawColorRef { get => _drawColor; set => _drawColor = value; }
        internal double DrawWidthRef { get => _drawWidth; set => _drawWidth = value; }
        internal byte DrawOpacityRef { get => _drawOpacity; set => _drawOpacity = value; }
        internal bool LineLevelRef { get => _lineLevel; set => _lineLevel = value; }
        internal bool HighlightEraseRef { get => _highlightErase; set => _highlightErase = value; }
        internal bool DrawEraseRef { get => _drawErase; set => _drawErase = value; }
        internal Color HighlightColorRef { get => _highlightColor; set => _highlightColor = value; }
        internal Color LineAnnotColorRef { get => _lineAnnotColor; set => _lineAnnotColor = value; }
        internal InkAnnotation? ActiveInkRef { get => _activeInk; set => _activeInk = value; }
        internal TextBox? ActiveTextBoxRef { get => _activeTextBox; set => _activeTextBox = value; }
        internal double TextFontSizeRef { get => _textFontSize; set => _textFontSize = value; }
        internal string TextFontNameRef { get => _textFontName; set => _textFontName = value; }
        internal bool TextBoldRef { get => _textBold; set => _textBold = value; }
        internal bool TextItalicRef { get => _textItalic; set => _textItalic = value; }
        internal bool TextStrikeRef { get => _textStrike; set => _textStrike = value; }
        internal bool TextUnderlineRef { get => _textUnderline; set => _textUnderline = value; }
        internal Color TextColorRef { get => _textColor; set => _textColor = value; }
        internal byte TextOpacityRef { get => _textOpacity; set => _textOpacity = value; }
        internal Color TextFillColorRef { get => _textFillColor; set => _textFillColor = value; }
        internal TextAnnotation? ReeditOriginalRef { get => _reeditOriginal; set => _reeditOriginal = value; }
        internal CoverAnnotation? PendingCoverRef { get => _pendingCover; set => _pendingCover = value; }
        internal bool PendingEditWasDirtyRef { get => _pendingEditWasDirty; set => _pendingEditWasDirty = value; }
        internal Border? TextSettingsBarRef { get => _textSettingsBar; set => _textSettingsBar = value; }
        internal bool IsResizingSigRef { get => _isResizingSig; set => _isResizingSig = value; }
        internal Point ResizeSigStartRef { get => _resizeSigStart; set => _resizeSigStart = value; }
        internal double ResizeSigStartScaleRef { get => _resizeSigStartScale; set => _resizeSigStartScale = value; }
        internal PlacedAnnotation? ResizeSigAnnotRef { get => _resizeSigAnnot; set => _resizeSigAnnot = value; }
        internal TextAnnotation? ResizeTextAnnotRef { get => _resizeTextAnnot; set => _resizeTextAnnot = value; }
        internal HighlightAnnotation? ResizeHlAnnotRef { get => _resizeHlAnnot; set => _resizeHlAnnot = value; }
        internal InkAnnotation? ResizeInkAnnotRef { get => _resizeInkAnnot; set => _resizeInkAnnot = value; }
        internal List<Point>? ResizeInkOrigPointsRef { get => _resizeInkOrigPoints; set => _resizeInkOrigPoints = value; }
        internal Rect ResizeInkOrigBoundsRef { get => _resizeInkOrigBounds; set => _resizeInkOrigBounds = value; }
        internal List<System.Windows.Shapes.Rectangle> ResizeHandlesRef => _resizeHandles;
        internal string ResizeCornerRef { get => _resizeCorner; set => _resizeCorner = value; }
        internal Point ResizeAnchorRef { get => _resizeAnchor; set => _resizeAnchor = value; }
        internal List<System.Windows.Shapes.Rectangle> TextEditHandlesRef => _textEditHandles;
        internal bool DraggingTextEditHandleRef { get => _draggingTextEditHandle; set => _draggingTextEditHandle = value; }
        internal string TehCornerRef { get => _tehCorner; set => _tehCorner = value; }
        internal Point TehAnchorRef { get => _tehAnchor; set => _tehAnchor = value; }
        internal TextBox? TehBoxRef { get => _tehBox; set => _tehBox = value; }
        internal bool IsDraggingAnnotRef { get => _isDraggingAnnot; set => _isDraggingAnnot = value; }
        internal Point DragAnnotStartRef { get => _dragAnnotStart; set => _dragAnnotStart = value; }
        internal Point DragAnnotOrigPosRef { get => _dragAnnotOrigPos; set => _dragAnnotOrigPos = value; }
        internal PageAnnotation? DragAnnotRef { get => _dragAnnot; set => _dragAnnot = value; }
        internal Rect CropCanvasRectRef { get => _cropCanvasRect; set => _cropCanvasRect = value; }
        internal System.Windows.Shapes.Rectangle? CropPreviewRectBorderRef { get => _cropPreviewRectBorder; set => _cropPreviewRectBorder = value; }
        internal List<System.Windows.Shapes.Path> CropBracketsRef => _cropBrackets;
        internal List<System.Windows.Shapes.Rectangle> CropHandlesRef => _cropHandles;
        internal string? ActiveCropHandleTagRef { get => _activeCropHandleTag; set => _activeCropHandleTag = value; }
        internal Point CropHandleDragStartRef { get => _cropHandleDragStart; set => _cropHandleDragStart = value; }
        internal Rect CropRectAtHandleDragRef { get => _cropRectAtHandleDrag; set => _cropRectAtHandleDrag = value; }
        internal TextBox? CropXBoxRef { get => _cropXBox; set => _cropXBox = value; }
        internal TextBox? CropYBoxRef { get => _cropYBox; set => _cropYBox = value; }
        internal TextBox? CropWBoxRef { get => _cropWBox; set => _cropWBox = value; }
        internal TextBox? CropHBoxRef { get => _cropHBox; set => _cropHBox = value; }
        internal TextBox? CropRangeBoxRef { get => _cropRangeBox; set => _cropRangeBox = value; }
        internal string CropUnitRef { get => _cropUnit; set => _cropUnit = value; }
        internal bool UpdatingCropInputsRef { get => _updatingCropInputs; set => _updatingCropInputs = value; }
        internal Dictionary<int, string> FormTextValuesRef { get => _formTextValues; set => _formTextValues = value; }
        internal Dictionary<int, bool> FormCheckValuesRef { get => _formCheckValues; set => _formCheckValues = value; }
        internal Dictionary<string, string> FormRadioValuesRef { get => _formRadioValues; set => _formRadioValues = value; }
        internal Dictionary<int, double> FormFontSizesRef { get => _formFontSizes; set => _formFontSizes = value; }
        internal Border? FormSizeBarRef { get => _formSizeBar; set => _formSizeBar = value; }
        internal TextBox? ActiveFormTbRef { get => _activeFormTb; set => _activeFormTb = value; }
        internal int ActiveFormObjRef { get => _activeFormObj; set => _activeFormObj = value; }
        internal double ActiveFormScaleRef { get => _activeFormScale; set => _activeFormScale = value; }
        internal Stack<UndoEntry> UndoStackRef { get => _undoStack; set => _undoStack = value; }
        internal Stack<UndoEntry> RedoStackRef { get => _redoStack; set => _redoStack = value; }
        internal bool IsDirtyRef { get => _isDirty; set => _isDirty = value; }
        internal string? OriginalFileRef { get => _originalFile; set => _originalFile = value; }
        internal bool OpenedFromProtectedRef { get => _openedFromProtected; set => _openedFromProtected = value; }
        internal bool AsyncOpenPendingRef { get => _asyncOpenPending; set => _asyncOpenPending = value; }
        internal Stack<int> NavBackRef => _navBack;
        internal Stack<int> NavForwardRef => _navForward;
        internal bool OcrRegionModeRef { get => _ocrRegionMode; set => _ocrRegionMode = value; }
        internal SavedSignature? PendingSignatureRef { get => _pendingSignature; set => _pendingSignature = value; }
        internal List<Point> ShapePolyPointsRef => _shapePolyPoints;
        internal EditTool? AnnotBarToolRef { get => _annotBarTool; set => _annotBarTool = value; }
        internal bool AnnotBarMinimizedRef { get => _annotBarMinimized; set => _annotBarMinimized = value; }
        internal List<FrameworkElement> AnnotBarDragInnersRef => _annotBarDragInners;

        // ── Save paths ───────────────────────────────────────────────────────────────────────
        internal void DrawAnnotationsOnDocumentExt(int? onlyPage = null) => DrawAnnotationsOnDocument(onlyPage);
        internal void WriteFormValuesToDocumentExt() => WriteFormValuesToDocument();

        // ── Handlers bound from MainWindow.xaml ──────────────────────────────────────────────
        // WPF resolves Click="X" against the XAML root's code-behind, which is still MainWindow, so
        // these keep working only because MainWindowViewerStubs.cs re-declares each name and points
        // it here. Moving them without that would throw XamlParseException at startup.
        internal void PageJumpBoxKeyDownExt(object sender, KeyEventArgs e) => PageJumpBox_KeyDown(sender, e);
        internal void PageJumpBoxGotFocusExt(object sender, RoutedEventArgs e) => PageJumpBox_GotFocus(sender, e);
        internal void PageListSelectionChangedExt(object sender, SelectionChangedEventArgs e)
            => PageList_SelectionChanged(sender, e);
        internal void ShortcutHelpClickExt(object sender, RoutedEventArgs e) => ShortcutHelp_Click(sender, e);
        internal void ShortcutOverlayMouseDownExt(object sender, MouseButtonEventArgs e)
            => ShortcutOverlay_MouseLeftButtonDown(sender, e);
        internal void ShortcutOverlayCardMouseDownExt(object sender, MouseButtonEventArgs e)
            => ShortcutOverlayCard_MouseLeftButtonDown(sender, e);
        internal void ShortcutOverlayCloseClickExt(object sender, RoutedEventArgs e)
            => ShortcutOverlayClose_Click(sender, e);
        internal void HyperlinkRequestNavigateExt(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
            => Hyperlink_RequestNavigate(sender, e);
    }
}
