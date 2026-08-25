using PdfSharpCore.Pdf;

namespace KillerPDF.Services
{
    // ============================================================
    // Pre-save document scrubs - pure functions over a PdfDocument,
    // no window state. Split out of FileOperations.cs and Links.cs
    // (KillerUI refactor); shared by the GUI save paths, TempReload,
    // the CLI runner and the batch runner.
    // ============================================================
    internal static class PdfScrub
    {
        /// <summary>
        /// Dereferences a PdfItem if it is an indirect reference (PdfReference is internal to
        /// PdfSharpCore; we detect it by looking for a public "Value" property returning
        /// PdfObject). Null-tolerant: absent dictionary keys arrive here as null and mean
        /// "not there".
        /// </summary>
        internal static PdfItem? DerefItemStatic(PdfItem? item)
        {
            // Absent dictionary keys arrive here as null (Elements["/X"] on a fresh document is
            // null for /AcroForm, /Kids, ...). The scrubs' pattern matches treat null as "not
            // there", which is correct - dereferencing it here just tripped an NRE first.
            if (item is null) return null;
            var valueProp = item.GetType().GetProperty("Value",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (valueProp?.GetValue(item) is PdfObject resolved) return resolved;
            return item;
        }

        internal static double RectNum(PdfItem item) =>
            item is PdfReal r ? r.Value : item is PdfInteger n ? n.Value : 0;

        /// <summary>
        /// Returns the PDF object number of a PdfItem that is an indirect reference, or -1.
        /// Handles the internal PdfReference type via reflection, like DerefItemStatic above.
        /// </summary>
        internal static int GetObjectNumber(PdfItem? item)
        {
            if (item is null) return -1;
            var prop = item.GetType().GetProperty("ObjectNumber",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return prop?.GetValue(item) is int n2 ? n2 : -1;
        }

        /// <summary>
        /// Strips visual styling (border, color, appearance stream) from all Link annotations
        /// in the document so they render as invisible clickable areas rather than colored
        /// rectangles that can look like strikethroughs in other PDF viewers.
        /// </summary>
        internal static void StripLinkAnnotationBorders(PdfDocument doc)
        {
            foreach (var pdfPage in doc.Pages)
            {
                var annotsArr = pdfPage.Elements.GetArray("/Annots");
                if (annotsArr is null) continue;
                for (int i = 0; i < annotsArr.Elements.Count; i++)
                {
                    PdfItem? elem = annotsArr.Elements[i];
                    PdfDictionary? ann = elem as PdfDictionary ?? DerefItemStatic(elem) as PdfDictionary;
                    if (ann is null) continue;

                    // Dereference subtype in case it is an indirect name.
                    var subtypeItem = ann.Elements["/Subtype"];
                    var subtype = (subtypeItem as PdfDictionary ?? DerefItemStatic(subtypeItem) as PdfDictionary) is null
                        ? subtypeItem?.ToString() ?? ""
                        : "";
                    if (!subtype.Contains("Link")) continue;

                    // Remove appearance stream and color.
                    ann.Elements.Remove("/AP");
                    ann.Elements.Remove("/C");

                    // /BS (border style dict) takes precedence over /Border in PDF spec;
                    // set W=0 explicitly.  Also set /Border [0 0 0] for older viewers.
                    var bs = new PdfDictionary();
                    bs.Elements["/W"] = new PdfInteger(0);
                    ann.Elements["/BS"] = bs;

                    var borderArr = new PdfArray();
                    borderArr.Elements.Add(new PdfInteger(0));
                    borderArr.Elements.Add(new PdfInteger(0));
                    borderArr.Elements.Add(new PdfInteger(0));
                    ann.Elements["/Border"] = borderArr;
                }
            }
        }
    }
}
