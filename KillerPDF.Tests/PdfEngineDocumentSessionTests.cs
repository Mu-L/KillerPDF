using KillerPDF.Services;
using KillerPdf.Engine.Authoring;
using System.IO;
using Xunit;

namespace KillerPDF.Tests;

public sealed class PdfEngineDocumentSessionTests
{
    [Fact]
    public void Open_OwnsImmutableBytesAndCachesPageGeometry()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-session-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] original = new PdfDocumentBuilder().AddBlankPage(320, 480).Build();
            File.WriteAllBytes(path, original);
            PdfEngineDocumentSession session = PdfEngineDocumentSession.Open(path);

            File.WriteAllBytes(path, new PdfDocumentBuilder().AddBlankPage(612, 792).Build());

            Assert.Equal(path, session.Path);
            Assert.Equal(original, session.Source.ToArray());
            var page = Assert.Single(session.Pages);
            Assert.Equal(320, page.Width);
            Assert.Equal(480, page.Height);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
