using System.Collections.Generic;
using System.Windows.Controls;

namespace KillerPDF
{
    public partial class MainWindow
    {
        /// <summary>
        /// Everything ONE document view owns. Split pane needs two of these; the window holds
        /// exactly one today, so this pass changes no behavior.
        ///
        /// This is deliberately the PER-VIEW cut, not the per-document one. Per-document state
        /// (annotations, undo, form values, search hits) already travels in DocumentSession, which
        /// tab switching swaps by reference - see the comment above _annotations in
        /// MainWindow.xaml.cs. A second pane needs its own live visual maps and its own view mode
        /// and zoom; it does NOT need a second copy of the per-document machinery, because each
        /// pane will simply own its own set of sessions.
        ///
        /// Nested inside MainWindow so it sits with the window's own types. Note ViewMode had to
        /// become internal for this: CS0052 compares DECLARED accessibility, so a field cannot be
        /// more accessible than its type even when both are inside MainWindow. Nesting does not
        /// exempt it. The enum stays nested, so nothing outside the assembly gains reach.
        ///
        /// MIGRATION NOTE: the window's fields are forwarding properties onto this object, so all
        /// ~500 existing call sites keep compiling untouched and the tree builds after every edit.
        /// Call sites get repointed at a viewer instance gradually in later stages; do not attempt
        /// that as one change. (BACKLOG.md "Split pane (F10)", stage 1.)
        /// </summary>
        internal sealed class ViewerState
        {
            /// <summary>Unified page -> overlay map covering EVERY rendered page, the primary
            /// included. The single source of truth the canvas accessors read from.</summary>
            public readonly Dictionary<int, Canvas> Pages = [];

            /// <summary>Per-page overlay canvases for the multi-page tile systems (continuous
            /// overlays, or grid / two-page secondaries). Holds only secondary tiles and is driven
            /// by the tile-recycling machinery.</summary>
            public readonly Dictionary<int, Canvas> ContinuousCanvases = [];

            /// <summary>Current view mode for this view.</summary>
            public ViewMode Mode = ViewMode.Continuous;

            /// <summary>Mode a fade is transitioning to, if one is in flight. Reads that need the
            /// destination rather than the current mode use `Pending ?? Mode` - the fade takes
            /// ~90ms and Mode lags behind it, which is what made wheel-cycling need several
            /// notches before it was fixed.</summary>
            public ViewMode? Pending;

            // ── Zoom / fit ──────────────────────────────────────────────────────────────────
            public double ZoomLevel = 1.0;
            /// <summary>Zoom the current bitmaps were rasterized at, so the re-sharpen pass knows
            /// whether what is on screen is still crisp enough.</summary>
            public double LastRenderZoom = 1.0;
            /// <summary>Primary (spread-left) page currently rasterized.</summary>
            public int RenderedPrimaryPage = -1;
            public FitMode Fit = FitMode.None;

            // ── In-flight render work ───────────────────────────────────────────────────────
            // Each view cancels and reschedules its own rendering, so two panes must not share
            // these - one pane's mode switch would otherwise cancel the other's render.
            public System.Windows.Threading.DispatcherTimer? RerenderTimer;
            public System.Threading.CancellationTokenSource? SecondaryRenderCts;
            public System.Threading.CancellationTokenSource? ContinuousRenderCts;
            /// <summary>#85 visible-page re-sharpen.</summary>
            public System.Threading.CancellationTokenSource? ContinuousSharpenCts;

            // ── Continuous-view bookkeeping ─────────────────────────────────────────────────
            /// <summary>Slots currently holding a hi-res bitmap.</summary>
            public readonly HashSet<int> ContinuousSharpPages = [];
            /// <summary>Budget those slots were sharpened at.</summary>
            public int ContinuousSharpW;
            public readonly List<double> ContinuousTops = [];
            /// <summary>Page to scroll to once its grid tile streams in (-1 = none).</summary>
            public int GridScrollToPage = -1;
            /// <summary>Re-scroll here once its true height is known.</summary>
            public int ContinuousScrollTarget = -1;
            public double ContinuousPageW;

            // ── Gesture routing ─────────────────────────────────────────────────────────────
            /// <summary>The page surface a pointer gesture started on, captured on mouse-down.
            /// Kept separate from the active canvas because RenderAllAnnotations reuses that as its
            /// render target, and in Grid view tiles stream in asynchronously and re-point it
            /// mid-gesture - which committed annotations to the wrong page.</summary>
            public Canvas? GestureCanvas;
            public int GesturePage = -1;

            // ── Visual hosts ────────────────────────────────────────────────────────────────
            // References only - the window still creates and owns the actual elements. Today
            // ContinuousPanel / PageContentPanel / PageContentGrid come from FindName in the
            // window ctor and are the ONE window's XAML; AnnotationCanvas / PageImage are the
            // code-built primary tile (Viewport.BuildPrimaryTile) and ActiveCanvas is re-pointed
            // on mouse-down. Holding them here is what lets the next stage hand each viewer its
            // own tile tree without touching any of the ~250 call sites that use them.
            public StackPanel ContinuousPanel = null!;
            public WrapPanel PageContentPanel = null!;
            public Grid PageContentGrid = null!;
            /// <summary>The hardcoded primary tile's overlay, shown in Single/Grid/TwoPage.</summary>
            public Canvas AnnotationCanvas = null!;
            public Image PageImage = null!;
            /// <summary>Active annotation surface. Single view: always AnnotationCanvas.
            /// Continuous: set on mouse-down to the clicked page's overlay.</summary>
            public Canvas ActiveCanvas = null!;
        }
    }
}
