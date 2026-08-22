using System.Linq;

namespace KillerPDF
{
    // ============================================================
    // THE shortcut table. One source of truth for both views of the shortcuts overlay: the list
    // (Shell/ShortcutsOverlay.cs) and the visual keyboard (Shell/KeyboardMapOverlay.cs).
    //
    // This was two hand maintained tables until 1.7.5 and they had drifted apart: Home and End read
    // "first / last page" on the list but carried separate captions on the map, Alt+M was missing
    // from the map entirely, and Ctrl+B was documented as bold in one section and as the sidebar in
    // another while the code did neither consistently. Deriving both views from one array means a
    // binding cannot be described two ways, and ShortcutTableTests holds that.
    //
    // Deliberately free of WPF and of MainWindow, so KillerPDF.Tests can link the file the way it
    // links OcrCatalog.cs. Anything that needs a Brush or a Control belongs in the overlay files.
    // ============================================================

    /// <summary>Which modifier layer of the keyboard map a cap sits on.</summary>
    internal enum KbLayer { Base, Ctrl, CtrlShift, Shift, Alt }

    /// <summary>
    /// One physical key the binding lights on the map. LabelKey overrides the row's description for
    /// this cap's hover caption, because a row and a cap are different granularities: the row reads
    /// "Home / End" while hovering Home should say "first page". Empty means inherit the row's.
    /// </summary>
    internal readonly record struct KsCap(KbLayer Layer, string Id, string LabelKey);

    /// <summary>One row of the list, plus the caps it lights on the map.</summary>
    internal readonly record struct KsBinding(string Keys, string LabelKey, string Cat, KsCap[] Caps);

    internal static class ShortcutTable
    {
        /// <summary>Authoring helper: "F3", "Shift:F3", or either with a per-cap hover label.
        /// Cap ids match KbRows in KeyboardMapOverlay.cs; bare ids are the base layer.</summary>
        internal static KsCap Cap(string id, string labelKey = "")
        {
            int colon = id.IndexOf(':');
            if (colon < 0) return new KsCap(KbLayer.Base, id, labelKey);
            string layer = id.Substring(0, colon);
            return new KsCap(
                layer switch
                {
                    "Ctrl" => KbLayer.Ctrl,
                    "CtrlShift" => KbLayer.CtrlShift,
                    "Shift" => KbLayer.Shift,
                    "Alt" => KbLayer.Alt,
                    _ => KbLayer.Base,
                },
                id.Substring(colon + 1), labelKey);
        }

        private static KsBinding B(string keys, string labelKey, string cat, params KsCap[] caps)
            => new(keys, labelKey, cat, caps);

        // Declaration order is display order. Column and section title come from KsGroups below.
        // A binding with no caps is a mouse gesture and correctly lights nothing on the board.
        internal static readonly KsBinding[] KsAll =
        [
            B("Ctrl+O",       "Str_KS_Open",        "File", Cap("Ctrl:O")),
            B("Ctrl+S",       "Str_Lbl_Save",       "File", Cap("Ctrl:S")),
            B("Ctrl+Shift+S", "Str_KS_SaveAs",      "File", Cap("CtrlShift:S")),
            B("Ctrl+W",       "Str_KS_CloseFile",   "File", Cap("Ctrl:W")),
            B("Ctrl+Shift+W", "Str_KS_CloseOthers", "File", Cap("CtrlShift:W")),
            B("Ctrl+Q",       "Str_KS_CloseAll",    "File", Cap("Ctrl:Q")),
            B("Ctrl+N",       "Str_KS_NewBlank",    "File", Cap("Ctrl:N")),
            B("Ctrl+P",       "Str_KS_Print",       "File", Cap("Ctrl:P")),
            B("Ctrl+D / F4",  "Str_KS_DocInfo",     "File", Cap("Ctrl:D"), Cap("F4")),
            B("Shift+F4",     "Str_KS_FileSize",    "File", Cap("Shift:F4")),

            B("V",               "Str_Lbl_Select",    "Tools", Cap("V")),
            B("1 (or T)",        "Str_Lbl_Text",      "Tools", Cap("D1"), Cap("T")),
            B("2 (or H)",        "Str_Lbl_Highlight", "Tools", Cap("D2"), Cap("H")),
            B("3 (or L or U)",   "Str_Lbl_Line",      "Tools", Cap("D3"), Cap("L"), Cap("U")),
            B("4",               "Str_Lbl_Shape",     "Tools", Cap("D4")),
            B("5 (or D)",        "Str_Lbl_Draw",      "Tools", Cap("D5"), Cap("D")),
            B("6 (or I)",        "Str_Lbl_Image",     "Tools", Cap("D6"), Cap("I")),
            B("7 (or G)",        "Str_Lbl_Signature", "Tools", Cap("D7"), Cap("G")),
            B("8 (or C)",        "Str_Lbl_Crop",      "Tools", Cap("D8"), Cap("C")),
            B("9 (or R)",        "Str_Lbl_Rotate",    "Tools", Cap("D9"), Cap("R")),
            B("0 (or S)",        "Str_TT_StampTool",  "Tools", Cap("D0"), Cap("S")),

            B("Ctrl+Z",           "Str_KS_Undo",        "Edit", Cap("Ctrl:Z")),
            B("Ctrl+Y",           "Str_Ctx_Redo",       "Edit", Cap("Ctrl:Y")),
            B("Ctrl+Shift+Z",     "Str_Ctx_Redo",       "Edit", Cap("CtrlShift:Z")),
            B("Ctrl+C",           "Str_KS_CopyText",    "Edit", Cap("Ctrl:C")),
            B("Ctrl+V",           "Str_KS_Paste",       "Edit", Cap("Ctrl:V")),
            // Real bindings as of 1.7.5. These were documented for years and never implemented;
            // Ctrl+B collapsed the sidebar instead, which is why the sidebar moved to F9.
            B("Ctrl+B / I / U",   "Str_KS_TextStyle",   "Edit", Cap("Ctrl:B", "Str_Lbl_Bold"),
                                                                Cap("Ctrl:I", "Str_Lbl_Italic"),
                                                                Cap("Ctrl:U", "Str_Lbl_Underline")),
            B("Delete",           "Str_KS_DeleteAnnot", "Edit", Cap("Del")),
            B("F2",               "Str_Ctx_BmRename",   "Edit", Cap("F2")),
            B("Enter / Escape",   "Str_KS_ConfirmCancel","Edit", Cap("Enter", "Str_Kb_Confirm"),
                                                                 Cap("Esc",   "Str_Kb_Cancel")),
            B("Menu / Shift+F10", "Str_KS_ContextMenu", "Edit", Cap("Menu"), Cap("Shift:F10")),

            B("F1 / Ctrl+?", "Str_KS_ThisList", "Help", Cap("F1"), Cap("Ctrl:Slash")),
            B("F12",         "Str_KS_About",    "Help", Cap("F12")),

            B("← / → or PgUp/PgDn", "Str_KS_PrevNext", "Nav", Cap("Left",  "Str_Kb_PrevPage"),
                                                              Cap("Right", "Str_Kb_NextPage"),
                                                              Cap("PgUp",  "Str_Kb_PrevPage"),
                                                              Cap("PgDn",  "Str_Kb_NextPage")),
            B("Home / End",    "Str_KS_FirstLast",   "Nav", Cap("Home", "Str_Kb_FirstPage"),
                                                            Cap("End",  "Str_Kb_LastPage")),
            B("Alt+← / Alt+→", "Str_KS_BackForward", "Nav", Cap("Alt:Left",  "Str_Kb_Back"),
                                                            Cap("Alt:Right", "Str_Kb_Forward")),
            B("↑ / ↓",         "Str_KS_ScrollView",  "Nav", Cap("Up"), Cap("Down")),
            B("Ctrl+Scroll",   "Str_KS_ZoomCursor",  "Nav"),
            B("Ctrl+%zin% / Ctrl+%zout%", "Str_KS_ZoomInOut", "Nav", Cap("Ctrl:Equals", "Str_Lbl_ZoomIn"),
                                                                     Cap("Ctrl:Minus",  "Str_Lbl_ZoomOut")),
            B("Ctrl+0",        "Str_KS_ResetZoom",   "Nav", Cap("Ctrl:D0")),
            B("Ctrl+1/2/3",    "Str_KS_ZoomPresets", "Nav", Cap("Ctrl:D1", "Str_Zoom_ActualSize"),
                                                            Cap("Ctrl:D2", "Str_Zoom_FitWidth"),
                                                            Cap("Ctrl:D3", "Str_Zoom_FitPage")),
            B("Middle drag",   "Str_KS_PanView",     "Nav"),
            B("Space + drag",  "Str_KS_PanView",     "Nav", Cap("Space")),
            B("F9",            "Str_KS_ToggleSidebar","Nav", Cap("F9")),
            B("Shift+F9",      "Str_KS_SidebarSide", "Nav", Cap("Shift:F9")),
            B("Ctrl+Tab",      "Str_KS_NextTab",     "Nav", Cap("Ctrl:Tab")),
            B("Ctrl+Shift+Tab","Str_KS_PrevTab",     "Nav", Cap("CtrlShift:Tab")),

            B("F5",            "Str_View_Continuous", "View", Cap("F5")),
            B("F6",            "Str_View_Single",     "View", Cap("F6")),
            B("F7",            "Str_View_TwoPage",    "View", Cap("F7")),
            B("B",             "Str_View_BookMode",   "View", Cap("B")),   // #193: Two-Page only
            B("F8",            "Str_View_Grid",       "View", Cap("F8")),
            // Cycling lost its F9 when the sidebar took the key. F5-F8 still reach every mode
            // directly, so the wheel gesture is the only thing that needed to survive.
            B("Wheel on view", "Str_KS_CycleView",    "View"),
            B("F10",           "Str_KS_SplitPane",    "View", Cap("F10")),
            // Esc belongs to Cancel on the base layer, so full screen only claims F11.
            B("F11 / Esc",     "Str_KS_FullScreen",   "View", Cap("F11")),
            B("Alt+M",         "Str_Toolbar_Hide",    "View", Cap("Alt:M")),
            B("N",             "Str_DocInvertSetting","View", Cap("N")),
            B("Shift+N",       "Str_InvertImagesToo", "View", Cap("Shift:N")),
            B("Ctrl+Shift+%zin% / %zout% / 0", "Str_KS_AppSize", "View", Cap("CtrlShift:Equals"),
                                                                         Cap("CtrlShift:Minus"),
                                                                         Cap("CtrlShift:D0")),
            B("Wheel on logo", "Str_KS_AppSize",      "View"),
            // The toolbar appearance six, mirroring the bar's right-click menu top to bottom.
            B("Ctrl+Shift+1..6", "Str_KS_ToolbarStyle", "View", Cap("CtrlShift:D1", "Str_Toolbar_SmallIcons"),
                                                                Cap("CtrlShift:D2", "Str_Toolbar_LargeIcons"),
                                                                Cap("CtrlShift:D3", "Str_Toolbar_TextNone"),
                                                                Cap("CtrlShift:D4", "Str_Toolbar_TextBeside"),
                                                                Cap("CtrlShift:D5", "Str_Toolbar_TextUnder"),
                                                                Cap("CtrlShift:D6", "Str_Toolbar_TextOnly")),

            B("Ctrl+Shift+O", "Str_Ctx_OcrPage", "Ocr", Cap("CtrlShift:O")),
            B("Ctrl+Shift+I", "Str_Ocr_Region",  "Ocr", Cap("CtrlShift:I")),

            B("Ctrl+F",              "Str_KS_Find",           "Search", Cap("Ctrl:F")),
            B("F3 / Shift+F3",       "Str_KS_NextPrevResult", "Search", Cap("F3",       "Str_Kb_NextResult"),
                                                                        Cap("Shift:F3", "Str_Kb_PrevResult")),
            B("Enter / Shift+Enter", "Str_KS_NextPrevResult", "Search", Cap("Shift:Enter", "Str_Kb_PrevResult")),
            B("Ctrl+A",              "Str_KS_SelectAll",      "Search", Cap("Ctrl:A")),
            B("Shift+Click",         "Str_KS_MultiSelect",    "Search"),
        ];

        // Section title and column for each category. Order here is the order sections appear.
        internal static readonly (string Cat, string TitleKey, bool Right)[] KsGroups =
        [
            ("File",   "Str_KS_File",         false),
            ("Tools",  "Str_KS_Tools",        false),
            ("Edit",   "Str_KS_Editing",      false),
            ("Help",   "Str_KS_Help",         false),
            ("Nav",    "Str_KS_Navigation",   true),
            ("View",   "Str_KS_View",         true),
            ("Ocr",    "Str_KS_Ocr",          true),
            ("Search", "Str_KS_SearchSelect", true),
        ];

        /// <summary>The map's view of the table: layer -> cap id -> (category, label key). A cap's
        /// own LabelKey wins over its row's, which is how one row serves two captions.</summary>
        internal static Dictionary<KbLayer, Dictionary<string, (string Cat, string Label)>> BuildMap()
        {
            var map = new Dictionary<KbLayer, Dictionary<string, (string, string)>>();
            foreach (KbLayer layer in System.Enum.GetValues(typeof(KbLayer)))
                map[layer] = new Dictionary<string, (string, string)>();

            foreach (var binding in KsAll)
                foreach (var cap in binding.Caps)
                    map[cap.Layer][cap.Id] =
                        (binding.Cat, cap.LabelKey.Length > 0 ? cap.LabelKey : binding.LabelKey);
            return map;
        }

        /// <summary>Every (layer, cap) the table claims, as "Layer:Id", including duplicates.
        /// ShortcutTableTests uses this to prove no key is claimed twice, which is exactly how
        /// Ctrl+B managed to mean two things for so long.</summary>
        internal static IEnumerable<string> AllCapClaims() =>
            KsAll.SelectMany(b => b.Caps).Select(c => c.Layer + ":" + c.Id);
    }
}
