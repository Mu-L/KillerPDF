using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests
{
    public class PdfFontStyleTests
    {
        [Theory]
        [InlineData("ABCDEF+Helvetica-Bold", "Helvetica", true, false)]
        [InlineData("Helvetica-Oblique", "Helvetica", false, true)]
        [InlineData("TimesNewRomanPS-BoldItalicMT", "TimesNewRomanPS", true, true)]
        [InlineData("Arial-Regular", "Arial", false, false)]
        public void DetectsFaceStyleFromPdfFontName(string source, string family, bool bold, bool italic)
        {
            var detected = PdfFontStyle.FromPdfName(source);

            Assert.Equal(family, detected.Family);
            Assert.Equal(bold, detected.Bold);
            Assert.Equal(italic, detected.Italic);
        }
    }
}
