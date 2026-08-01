using Docnet.Core.Models;

namespace KillerPDF.Services
{
    // ============================================================
    // Shared page-rasterization flags (#141).
    //
    // PDFium does not draw a file's own annotations - sticky notes, highlights,
    // stamps, ink made in another app - unless it is asked to. Docnet's
    // parameterless GetImage() passes flags 0, so KillerPDF rendered pages
    // WITHOUT them: they were in the file and simply never painted, which is
    // why Firefox and SumatraPDF showed markup that KillerPDF did not.
    //
    // This mattered beyond the screen. Flatten and image export rasterize the
    // page and build a NEW document from the result, so annotations the source
    // carried were silently dropped from the output, and printing omitted them.
    // Every path that turns a page into pixels therefore uses this flag.
    //
    // NOT used by the OCR paths: those rasterize to recognize the page's own
    // text, and a reviewer's sticky note is not page content.
    //
    // Note that KillerPDF's OWN annotations are burned into the page content
    // stream on save (PdfBurn), not added as annotation objects, so nothing is
    // ever drawn twice by enabling this.
    // ============================================================
    internal static class PdfRender
    {
        /// <summary>Render the page WITH the annotations the file carries. Docnet also draws form
        /// field appearances under this flag (it gates FPDFFFLDraw on it), which is correct: those
        /// are page furniture too, and the app's interactive field overlays sit above them.</summary>
        internal const RenderFlags WithAnnotations = RenderFlags.RenderAnnotations;
    }
}
