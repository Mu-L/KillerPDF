using System.Diagnostics;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace KillerPDF.Tests
{
    /// <summary>
    /// Regression cover for bookmarks whose /Dest is a *named destination* rather than a literal
    /// array. wkhtmltopdf writes /Dest /__WKANCHOR_n plus a flat catalog /Dests dictionary, and
    /// since most HTML-to-PDF invoice generators are wkhtmltopdf underneath, that shape is common.
    /// PdfSharpCore's PdfOutline.Initialize() used to hit Debug.Assert(false, "See what to do when
    /// this happened.") on it - a modal dialog in Debug, a silently dead bookmark in Release.
    /// </summary>
    public class OutlineDestinationTests
    {
        const string DestName = "/__WKANCHOR_2";

        /// <summary>
        /// Debug.Assert writes to a trace listener and shows a modal box - it never throws, so an
        /// assert regression would HANG this test rather than fail it. Swap in a listener that
        /// turns Fail into an exception.
        /// </summary>
        sealed class ThrowingListener : TraceListener
        {
            public override void Write(string? message) { }
            public override void WriteLine(string? message) { }
            public override void Fail(string? message) => throw new Xunit.Sdk.XunitException("Debug.Assert fired: " + message);
            public override void Fail(string? message, string? detail) => throw new Xunit.Sdk.XunitException("Debug.Assert fired: " + message + " | " + detail);
        }

        [Fact]
        public void OpenOutlines_NamedDestination_DoesNotAssert()
        {
            var dir  = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, "named_dest.pdf");

            var original = new TraceListener[Trace.Listeners.Count];
            Trace.Listeners.CopyTo(original, 0);
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new ThrowingListener());

            try
            {
                WriteNamedDestPdf(file);

                var doc = PdfReader.Open(file, PdfDocumentOpenMode.Modify);
                var outlines = doc.Outlines;   // threw before the fix

                Assert.Single(outlines);
                Assert.Equal("Tax Invoice", outlines[0].Title);

                // Guard the fixture itself: if a future PdfSharpCore writer rewrote the name into a
                // literal array on save, this test would still pass while no longer covering the
                // regression at all.
                Assert.IsType<PdfName>(outlines[0].Elements.GetValue("/Dest"));
            }
            finally
            {
                Trace.Listeners.Clear();
                Trace.Listeners.AddRange(original);
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// Builds a one-page PDF with a single bookmark pointing at a named destination.
        /// ponytail: the /Dests entry holds the destination array DIRECTLY; the invoice that
        /// surfaced this held it indirectly. Both take the same /Dest-is-a-PdfName branch, which is
        /// the regression under test. Make it indirect if that path ever breaks on its own.
        /// </summary>
        static void WriteNamedDestPdf(string path)
        {
            var doc = new PdfDocument();
            doc.AddPage();

            // [page /XYZ left top zoom] - integer page number, as wkhtmltopdf emits.
            var dest = new PdfArray(doc,
                new PdfInteger(0), new PdfName("/XYZ"),
                new PdfInteger(0), new PdfInteger(800), new PdfInteger(0));

            var dests = new PdfDictionary(doc);
            dests.Elements[DestName] = dest;
            doc.Internals.AddObject(dests);

            var root = new PdfDictionary(doc);
            root.Elements["/Type"] = new PdfName("/Outlines");
            doc.Internals.AddObject(root);

            var item = new PdfDictionary(doc);
            item.Elements["/Title"] = new PdfString("Tax Invoice");
            item.Elements["/Dest"]  = new PdfName(DestName);
            item.Elements.SetReference("/Parent", root);
            doc.Internals.AddObject(item);

            root.Elements.SetReference("/First", item);
            root.Elements.SetReference("/Last",  item);

            doc.Internals.Catalog.Elements.SetReference("/Outlines", root);
            doc.Internals.Catalog.Elements.SetReference("/Dests",    dests);

            doc.Save(path);
        }
    }
}
