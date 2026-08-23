using System.Buffers.Binary;
using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfOutputIntentTests
{
    [Theory]
    [InlineData("GRAY", 1)]
    [InlineData("RGB ", 3)]
    [InlineData("CMYK", 4)]
    public void IccProfile_ReadsSupportedComponentCounts(string colorSpace, int expectedComponents)
    {
        PdfIccProfile profile = PdfIccProfile.Load(BuildProfile(colorSpace));

        Assert.Equal(expectedComponents, profile.ComponentCount);
        Assert.Equal(colorSpace.TrimEnd(), profile.ColorSpace);
    }

    [Fact]
    public void SetOutputIntent_WritesCatalogIntentAndEmbeddedProfile()
    {
        byte[] bytes = BuildProfile("RGB ");
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .SetOutputIntent(PdfIccProfile.Load(bytes), "sRGB IEC61966-2.1",
                registryName: "http://www.color.org")
            .AddBlankPage()
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var intents = Assert.IsType<PdfArray>(catalog[Name("OutputIntents")]);
        var intent = ResolveDictionary(document, intents[0]);
        var profile = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(intent[Name("DestOutputProfile")])));

        Assert.Equal("GTS_PDFA1", Assert.IsType<PdfName>(intent[Name("S")]).ValueAsLatin1());
        Assert.Equal(3, Assert.IsType<PdfInteger>(profile.Dictionary[Name("N")]).Value);
        Assert.Equal(bytes, profile.EncodedData.ToArray());
    }

    [Fact]
    public void IccProfile_RejectsMissingSignatureAndUnsupportedColourSpace()
    {
        byte[] missing = BuildProfile("RGB ");
        missing.AsSpan(36, 4).Clear();

        Assert.Throws<FormatException>(() => PdfIccProfile.Load(missing));
        Assert.Throws<NotSupportedException>(() => PdfIccProfile.Load(BuildProfile("LAB ")));
    }

    [Fact]
    public void PdfA4Mode_WritesIdentificationXmpAndOmitsInformationDictionary()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata { Title = "PDF/A-4" })
            .SetOutputIntent(PdfIccProfile.Load(BuildProfile("RGB ")), "Test RGB")
            .EnablePdfA4Conformance()
            .AddBlankPage()
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var metadata = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(catalog[Name("Metadata")])));
        string xmp = Encoding.UTF8.GetString(metadata.EncodedData.Span);

        Assert.False(document.Trailer.ContainsKey(Name("Info")));
        Assert.Contains("pdfaid:part", xmp);
        Assert.Contains(">4<", xmp);
        Assert.Contains("pdfaid:rev", xmp);
        Assert.Contains(">2020<", xmp);
    }

    [Fact]
    public void PdfA4Mode_RequiresMetadataAndOutputIntent()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PdfDocumentBuilder().EnablePdfA4Conformance().AddBlankPage().Build());
        Assert.Throws<InvalidOperationException>(() => new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata()).EnablePdfA4Conformance().AddBlankPage().Build());
    }

    [Fact]
    public void PdfA4Mode_RejectsKnownNonConformingAuthoringFeatures()
    {
        PdfDocumentBuilder Ready() => new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata())
            .SetOutputIntent(PdfIccProfile.Load(BuildProfile("RGB ")), "Test RGB")
            .EnablePdfA4Conformance()
            .AddBlankPage();

        Assert.Throws<InvalidOperationException>(() =>
            Ready().AddTextField(0, "name", 0, 0, 100, 20).Build());
        Assert.Throws<InvalidOperationException>(() =>
            Ready().AddAttachment("data.txt", ReadOnlyMemory<byte>.Empty).Build());
        Assert.Throws<InvalidOperationException>(() => new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata())
            .SetOutputIntent(PdfIccProfile.Load(BuildProfile("RGB ")), "Test RGB")
            .EnablePdfA4Conformance()
            .AddPage(100, 100, new PdfContentStreamBuilder()
                .BeginText().SetFont(PdfStandardFont.Helvetica, 12).ShowLatin1Text("No").EndText())
            .Build());
    }

    private static byte[] BuildProfile(string colorSpace)
    {
        byte[] result = new byte[128];
        BinaryPrimitives.WriteUInt32BigEndian(result, 128);
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(result, 16);
        "acsp"u8.CopyTo(result.AsSpan(36, 4));
        return result;
    }

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
