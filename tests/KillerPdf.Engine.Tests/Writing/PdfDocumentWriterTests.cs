using System.Text;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;
using KillerPdf.Engine.Syntax;
using Xunit;

namespace KillerPdf.Engine.Tests.Writing;

public sealed class PdfDocumentWriterTests
{
    [Fact]
    public void Write_ProducesACompletePdfThatTheEngineCanReopen()
    {
        PdfDocument original = PdfDocument.Open(SourcePdf());

        byte[] rewrittenBytes = PdfDocumentWriter.Write(original);
        PdfDocument rewritten = PdfDocument.Open(rewrittenBytes);

        Assert.Equal(original.Header.Version, rewritten.Header.Version);
        var catalog = Assert.IsType<PdfDictionary>(rewritten.Resolve(new PdfIndirectReference(1, 0)));
        Assert.Equal("Catalog", Assert.IsType<PdfName>(catalog[Name("Type")]).ValueAsLatin1());
        var stream = Assert.IsType<PdfStream>(rewritten.Resolve(2));
        Assert.Equal("Hello", Encoding.ASCII.GetString(stream.EncodedData.Span));
        Assert.Contains("\nxref\n", Encoding.Latin1.GetString(rewrittenBytes), StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", Encoding.Latin1.GetString(rewrittenBytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_IsByteStableAcrossRepeatedFullRewrites()
    {
        byte[] first = PdfDocumentWriter.Write(PdfDocument.Open(SourcePdf()));
        byte[] second = PdfDocumentWriter.Write(PdfDocument.Open(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_RefusesToEmitAnUnimplementedEncryptedRewrite()
    {
        PdfDocument document = PdfDocument.Open(SourcePdf(" /Encrypt 9 0 R"));

        Assert.Throws<NotSupportedException>(() => PdfDocumentWriter.Write(document));
    }

    [Fact]
    public void Write_AllowsVersionUpgradesButRefusesBlindDowngrades()
    {
        PdfDocument document = PdfDocument.Open(SourcePdf(version: "1.7"));

        byte[] upgraded = PdfDocumentWriter.Write(document, new PdfDocumentWriteOptions
        {
            TargetVersion = PdfVersion.Pdf20
        });

        Assert.Equal(PdfVersion.Pdf20, PdfDocument.Open(upgraded).Header.Version);
        Assert.Throws<NotSupportedException>(() => PdfDocumentWriter.Write(
            PdfDocument.Open(SourcePdf()),
            new PdfDocumentWriteOptions { TargetVersion = PdfVersion.Pdf17 }));
    }

    [Fact]
    public void Write_CanRemoveDocumentInformationAndIdentifiersIndependently()
    {
        PdfDocument document = PdfDocument.Open(SourcePdf(" /Info 1 0 R /ID [<01> <02>]"));
        byte[] rewritten = PdfDocumentWriter.Write(document, new PdfDocumentWriteOptions
        {
            MetadataPolicy = PdfMetadataPolicy.RemoveDocumentInformation,
            PreserveDocumentIdentifiers = false
        });
        PdfDictionary trailer = PdfDocument.Open(rewritten).Trailer;

        Assert.False(trailer.ContainsKey(Name("Info")));
        Assert.False(trailer.ContainsKey(Name("ID")));
    }

    private static byte[] SourcePdf(string extraTrailer = "", string version = "2.0")
    {
        var source = new StringBuilder($"%PDF-{version}\n");
        int catalogOffset = source.Length;
        source.Append("1 0 obj << /Type /Catalog /Data 2 0 R >> endobj\n");
        int streamOffset = source.Length;
        source.Append("2 0 obj << /Length 5 >> stream\nHello\nendstream endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 3\n0000000000 65535 f\n");
        source.Append($"{catalogOffset:0000000000} 00000 n\n");
        source.Append($"{streamOffset:0000000000} 00000 n\n");
        source.Append($"trailer << /Size 3 /Root 1 0 R{extraTrailer} >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
