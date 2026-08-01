using System.Windows.Controls;
using KillerPDF.Controls;

namespace KillerPDF
{
    // The element names MainWindow.xaml used to generate, now that the document pane is a control
    // (Controls/PdfViewer.xaml). Split pane stage 2.
    //
    // Same trick as stage 1's ViewerState forwarding: the names stay, the storage moves. Every
    // existing call site - DocPaneBorder.CornerRadius in Tabs.cs, DocPaneShadow.Visibility in
    // FullScreen.cs, PagePreviewPanel and MarqueeLayer across the render and annotation code -
    // compiles untouched.
    //
    // Get-only is correct here: callers mutate the ELEMENT (its margin, radius, visibility), never
    // rebind the reference.
    //
    // NOTE the one thing that could NOT be forwarded: the card's MARGIN. It lives on the control
    // now, not on PaneBorder, because the control is what the layout positions. ApplySidebarSide
    // and ApplyFullScreen set Viewer.Margin directly.
    public partial class MainWindow
    {
        private Border DocPaneShadow => Viewer.PaneShadowBorder;
        private Border DocPaneBorder => Viewer.PaneCardBorder;
        private Grid DocPaneContent => Viewer.ContentHost;
        // 7 bare uses in Tabs.cs and Viewport.cs. Note this is a SEPARATE member from the
        // underscore-prefixed _pageContentGrid, which forwards to ViewerState - the code uses both
        // spellings, so both have to resolve.
        private Grid PageContentGrid => Viewer.PageGrid;
        private ScrollViewer PagePreviewPanel => Viewer.PreviewScroller;
        private Border DropZone => Viewer.DropSurface;
        private Border RecentFilesBox => Viewer.RecentBox;
        private ItemsControl RecentFilesList => Viewer.RecentList;
        private Canvas MarqueeLayer => Viewer.Marquee;
        private Border DocSurfacePad => Viewer.SurfacePad;
        private System.Windows.Media.ImageBrush GrainBrush => Viewer.Grain;
    }
}
