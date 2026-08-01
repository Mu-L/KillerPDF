namespace KillerPDF
{
    // How a document view lays its pages out, and how it fits them to the viewport.
    //
    // Both of these were nested inside MainWindow and marked internal, because ViewerState has
    // fields of them and CS0052 compares DECLARED accessibility - a field cannot be more
    // accessible than its type, even when both sit inside the same class.
    //
    // Split pane stage 3 lifted them out here. The viewer is a UserControl in KillerPDF.Controls
    // now, and from there a type nested in MainWindow only spells as MainWindow.ViewMode - which
    // would have meant rewriting 91 references for no gain. As top-level types in KillerPDF they
    // resolve unqualified from KillerPDF.Controls too (a namespace declaration puts its parent
    // namespaces in scope), so every existing call site kept compiling untouched.
    //
    // internal, not public: nothing outside the assembly has any business with either.

    /// <summary>Page layout for a document view. RenderPage is Single/TwoPage/Grid only and is
    /// guarded to no-op in Continuous - see the render pipeline's notes on why the two pipelines
    /// cannot be mixed.</summary>
    internal enum ViewMode { Single, Continuous, TwoPage, Grid }

    /// <summary>Automatic fit applied on resize, or None when the user has set a zoom.</summary>
    internal enum FitMode { None, Width, Page }
}
