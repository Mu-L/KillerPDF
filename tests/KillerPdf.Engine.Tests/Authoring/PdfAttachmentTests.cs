using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfAttachmentTests
{
    [Fact]
    public void AddAttachment_WritesNamesTreeAssociatedFileAndExactPayload()
    {
        byte[] payload = Encoding.UTF8.GetBytes("KillerPDF attachment");
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddAttachment("résumé.txt", payload, "text/plain", "Test data",
                PdfAssociatedFileRelationship.Data,
                new DateTimeOffset(2026, 8, 22, 20, 0, 0, TimeSpan.FromHours(-7)))
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var names = Assert.IsType<PdfDictionary>(catalog[Name("Names")]);
        var embeddedFiles = Assert.IsType<PdfDictionary>(names[Name("EmbeddedFiles")]);
        var nameArray = Assert.IsType<PdfArray>(embeddedFiles[Name("Names")]);
        var fileSpecReference = Assert.IsType<PdfIndirectReference>(nameArray[1]);
        var fileSpec = ResolveDictionary(document, fileSpecReference);
        var ef = Assert.IsType<PdfDictionary>(fileSpec[Name("EF")]);
        var embedded = Assert.IsType<PdfStream>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(ef[Name("UF")])));
        var associated = Assert.IsType<PdfArray>(catalog[Name("AF")]);

        Assert.Equal("résumé.txt", DecodeUnicode(Assert.IsType<PdfString>(nameArray[0])));
        Assert.Equal("résumé.txt", DecodeUnicode(Assert.IsType<PdfString>(fileSpec[Name("UF")])));
        Assert.Equal("Data", Assert.IsType<PdfName>(fileSpec[Name("AFRelationship")]).ValueAsLatin1());
        Assert.Equal("text/plain", Assert.IsType<PdfName>(embedded.Dictionary[Name("Subtype")]).ValueAsLatin1());
        Assert.Equal(payload, embedded.EncodedData.ToArray());
        Assert.Equal(fileSpecReference.ObjectNumber,
            Assert.IsType<PdfIndirectReference>(associated[0]).ObjectNumber);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("")]
    public void AddAttachment_RejectsInvalidFileNames(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new PdfDocumentBuilder().AddAttachment(name, ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void AddAttachment_RejectsDuplicateNamesIgnoringCase()
    {
        var builder = new PdfDocumentBuilder().AddAttachment("readme.txt", ReadOnlyMemory<byte>.Empty);

        Assert.Throws<ArgumentException>(() =>
            builder.AddAttachment("README.TXT", ReadOnlyMemory<byte>.Empty));
    }

    private static string DecodeUnicode(PdfString value) =>
        Encoding.BigEndianUnicode.GetString(value.Bytes.Span[2..]);
    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
