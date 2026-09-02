using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using Xunit;

namespace KillerPdf.Engine.Tests.Documents;

public sealed class PdfPageRasterInformationTests
{
    [Fact]
    public void ReadBitonalImagePageHints_DistinguishesBitonalAndColorPages()
    {
        PdfImage bitonal = PdfImage.FromBitonal(2, 1, new byte[] { 0, 255 });
        PdfImage color = PdfImage.FromRgb(1, 1, new byte[] { 255, 0, 0 });
        byte[] source = new PdfDocumentBuilder()
            .AddPage(100, 100,
                new PdfContentStreamBuilder().DrawImage(bitonal, 0, 0, 100, 100))
            .AddPage(100, 100,
                new PdfContentStreamBuilder().DrawImage(color, 0, 0, 100, 100))
            .AddPage(100, 100, new PdfContentStreamBuilder())
            .Build();

        Assert.Equal([true, false, false],
            PdfPageRasterInformation.ReadBitonalImagePageHints(PdfDocument.Open(source)));
    }
}
