using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KillerPDF.Controls
{
    /// <summary>
    /// One document view. Split pane stage 2 - the card and its contents lifted out of
    /// MainWindow.xaml so a second instance can exist.
    ///
    /// Every handler here is a ONE-LINE FORWARD to Owner, which is KillerShell's FilePane idiom
    /// (see FilePane.xaml.cs, `=> Owner.Overflow_Click(this)`). Nothing is reimplemented: the real
    /// bodies stay on MainWindow and move into this class wholesale in stages 3-4. Doing it this
    /// way means the XAML move and the code move are separate, independently verifiable steps
    /// rather than one change that either works or does not.
    ///
    /// The 11 forwards below are exactly the handlers the moved markup binds - enumerated from the
    /// XAML rather than remembered.
    /// </summary>
    public partial class PdfViewer : UserControl
    {
        /// <summary>The window that owns this viewer. Set immediately after construction; every
        /// handler below is inert until it is.</summary>
        internal MainWindow? Owner { get; set; }

        public PdfViewer() => InitializeComponent();

        // ---- Element access for the window --------------------------------------------------
        // A UserControl is its OWN NAMESCOPE, so the window's FindName can no longer reach any of
        // these - and it fails SILENTLY, returning null rather than throwing. That is the trap in
        // this move. The window ctor now assigns _view.X from these instead, which works because
        // stage 1 already turned its fields into forwarding properties onto ViewerState.
        internal Border PaneShadowBorder => PaneShadow;
        internal Border PaneCardBorder => PaneBorder;
        internal Grid ContentHost => DocPaneContent;
        internal StackPanel ContinuousHost => ContinuousPanel;
        internal WrapPanel PageHost => PageContentPanel;
        internal Grid PageGrid => PageContentGrid;
        internal ScrollViewer PreviewScroller => PagePreviewPanel;
        internal Border DropSurface => DropZone;
        internal Border RecentBox => RecentFilesBox;
        internal ItemsControl RecentList => RecentFilesList;
        internal Canvas Marquee => MarqueeLayer;
        internal Border SurfacePad => DocSurfacePad;
        internal System.Windows.Media.ImageBrush Grain => GrainBrush;

        // ---- Forwards to the owning window ----------------------------------------------------
        // MainWindow's copies were private; they are internal now purely so these can reach them.

        private void DocPane_SizeChanged(object s, SizeChangedEventArgs e) => Owner?.DocPane_SizeChanged(s, e);

        private void DropZone_Drop(object s, DragEventArgs e) => Owner?.DropZone_Drop(s, e);
        private void DropZone_DragOver(object s, DragEventArgs e) => Owner?.DropZone_DragOver(s, e);
        private void DropZone_Click(object s, MouseButtonEventArgs e) => Owner?.DropZone_Click(s, e);

        private void RecentClearAll_Click(object s, MouseButtonEventArgs e) => Owner?.RecentClearAll_Click(s, e);

        private void PagePreview_PreviewMouseWheel(object s, MouseWheelEventArgs e) => Owner?.PagePreview_PreviewMouseWheel(s, e);
        private void PagePreviewPanel_SizeChanged(object s, SizeChangedEventArgs e) => Owner?.PagePreviewPanel_SizeChanged(s, e);
        private void PagePreviewPanel_PreviewMouseDown(object s, MouseButtonEventArgs e) => Owner?.PagePreviewPanel_PreviewMouseDown(s, e);
        private void PagePreviewPanel_PreviewMouseMove(object s, MouseEventArgs e) => Owner?.PagePreviewPanel_PreviewMouseMove(s, e);
        private void PagePreviewPanel_PreviewMouseUp(object s, MouseButtonEventArgs e) => Owner?.PagePreviewPanel_PreviewMouseUp(s, e);
        private void DocPaneBackground_RightClick(object s, MouseButtonEventArgs e) => Owner?.DocPaneBackground_RightClick(s, e);
    }
}
