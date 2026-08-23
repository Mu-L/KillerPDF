using System.Security.Cryptography;
using System.Text;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Filters;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Security;
using KillerPdf.Engine.Writing;
using Xunit;

namespace KillerPdf.Engine.Tests.Security;

public sealed class PdfEncryptionTests
{
    [Theory]
    [InlineData("new-user")]
    [InlineData("new-owner")]
    public void Authoring_CreatesRevision6Aes256Document(string password)
    {
        byte[] bytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "new-user",
                OwnerPassword = "new-owner"
            })
            .AddPage(612, 792, "BT (private authored text) Tj ET"u8.ToArray())
            .Build();

        PdfDocument document = PdfDocument.Open(bytes, password);

        Assert.True(document.IsEncrypted);
        Assert.True(document.IsDecrypted);
        Assert.Equal(-1, bytes.AsSpan().IndexOf("private authored text"u8));
        Assert.Throws<CryptographicException>(() => PdfDocument.Open(bytes, "wrong"));
    }

    [Fact]
    public void Authoring_RejectsEncryptionWithPdfAConformance()
    {
        var builder = new PdfDocumentBuilder()
            .EnablePdfA4Conformance()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "user",
                OwnerPassword = "owner"
            });

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Authoring_WritesTypedPermissionBits()
    {
        byte[] bytes = new PdfDocumentBuilder()
            .SetPasswordEncryption(new PdfPasswordEncryptionOptions
            {
                UserPassword = "user",
                OwnerPassword = "owner",
                AllowContentCopying = false,
                AllowDocumentModification = false,
                AllowAnnotationModification = false,
                AllowFormFilling = false,
                AllowDocumentAssembly = false,
                AllowHighQualityPrinting = false
            }).AddBlankPage().Build();
        PdfDocument document = PdfDocument.Open(bytes);
        Assert.True(document.CrossReferences.TryGetTrailerValue(
            new PdfName("Encrypt"u8), out PdfObject encryptionReference));
        PdfDictionary encryption = Assert.IsType<PdfDictionary>(
            document.Resolve(Assert.IsType<PdfIndirectReference>(encryptionReference)));
        int permissions = checked((int)Assert.IsType<PdfInteger>(
            encryption[new PdfName("P"u8)]).Value);

        Assert.NotEqual(0, permissions & (1 << 2));
        Assert.Equal(0, permissions & (1 << 3));
        Assert.Equal(0, permissions & (1 << 4));
        Assert.Equal(0, permissions & (1 << 5));
        Assert.Equal(0, permissions & (1 << 8));
        Assert.NotEqual(0, permissions & (1 << 9));
        Assert.Equal(0, permissions & (1 << 10));
        Assert.Equal(0, permissions & (1 << 11));
    }

    [Theory]
    [InlineData("user-password")]
    [InlineData("owner-password")]
    public void Open_AuthenticatesAndDecryptsRevision6Aes256Streams(string password)
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture(), password);
        PdfStream stream = Assert.IsType<PdfStream>(document.Resolve(4));

        Assert.True(document.IsEncrypted);
        Assert.True(document.IsDecrypted);
        Assert.Equal(
            "q\n0.9 0.2 0.4 rg\n72 72 200 100 re\nf\nQ\n",
            Encoding.ASCII.GetString(PdfStreamDecoder.Decode(stream)));
    }

    [Fact]
    public void Open_RejectsWrongRevision6Password()
    {
        Assert.Throws<CryptographicException>(() =>
            PdfDocument.Open(Revision6Fixture(), "wrong-password"));
    }

    [Fact]
    public void Open_AuthenticatesLegacyRc4AndAes128SecurityHandlers()
    {
        foreach (byte[] fixture in new[] { Revision2Fixture(), Revision4Fixture() })
        {
            foreach (string password in new[] { "user-password", "owner-password" })
            {
                PdfDocument document = PdfDocument.Open(fixture, password);
                PdfStream stream = Assert.IsType<PdfStream>(document.Resolve(4));
                Assert.Equal(
                    "q\n0.9 0.2 0.4 rg\n72 72 200 100 re\nf\nQ\n",
                    Encoding.ASCII.GetString(PdfStreamDecoder.Decode(stream)));
            }
        }
    }

    [Fact]
    public void Open_ReportsEncryptedDocumentWhenNoPasswordWasSupplied()
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture());

        Assert.True(document.IsEncrypted);
        Assert.False(document.IsDecrypted);
    }

    [Fact]
    public void IncrementalUpdate_EncryptsNewStringsAndStreams()
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture(), "user-password");
        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference stringReference = update.AddObject(
            new PdfString("secret"u8, PdfStringForm.Literal));
        PdfIndirectReference streamReference = update.AddObject(
            new PdfStream(new PdfDictionary([]), "payload"u8));

        byte[] bytes = update.Build();
        PdfDocument reopened = PdfDocument.Open(bytes, "owner-password");

        Assert.Equal("secret", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(reopened.Resolve(stringReference)).Bytes.Span));
        Assert.Equal("payload"u8.ToArray(),
            Assert.IsType<PdfStream>(reopened.Resolve(streamReference)).EncodedData.ToArray());
        Assert.Equal(-1, bytes.AsSpan().IndexOf("secret"u8));
        Assert.Equal(-1, bytes.AsSpan().IndexOf("payload"u8));
    }

    [Fact]
    public void IncrementalCrossReferenceStream_RemainsReadableAndEncryptsNewObjects()
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture(), "user-password");
        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference stringReference = update.AddObject(
            new PdfString("stream secret"u8, PdfStringForm.Literal));
        PdfIndirectReference streamReference = update.AddObject(
            new PdfStream(new PdfDictionary([]), "stream payload"u8));

        byte[] bytes = update.Build(new PdfIncrementalUpdateWriteOptions
        {
            CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
            CompressCrossReferenceStream = true
        });
        PdfDocument reopened = PdfDocument.Open(bytes, "owner-password");

        Assert.True(reopened.CrossReferences.Sections[0].IsStream);
        Assert.Equal("stream secret", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(reopened.Resolve(stringReference)).Bytes.Span));
        Assert.Equal("stream payload"u8.ToArray(),
            Assert.IsType<PdfStream>(reopened.Resolve(streamReference)).EncodedData.ToArray());
        Assert.Equal(-1, bytes.AsSpan().IndexOf("stream secret"u8));
        Assert.Equal(-1, bytes.AsSpan().IndexOf("stream payload"u8));
    }

    [Fact]
    public void IncrementalEncryptedObjectStream_DecryptsPackedObjects()
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture(), "owner-password");
        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference reference = update.AddObject(
            new PdfString("packed secret"u8, PdfStringForm.Literal));

        byte[] bytes = update.Build(new PdfIncrementalUpdateWriteOptions
        {
            CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
            UseObjectStreams = true,
            CompressObjectStreams = true,
            CompressCrossReferenceStream = true
        });
        PdfDocument reopened = PdfDocument.Open(bytes, "user-password");

        Assert.Equal(PdfCrossReferenceEntryType.Compressed,
            reopened.CrossReferences[reference.ObjectNumber].Type);
        Assert.Equal("packed secret", Encoding.ASCII.GetString(
            Assert.IsType<PdfString>(reopened.Resolve(reference)).Bytes.Span));
        Assert.Equal(-1, bytes.AsSpan().IndexOf("packed secret"u8));
    }

    [Theory]
    [InlineData("Identity", true)]
    [InlineData("StdCF", false)]
    public void ExplicitCryptFilter_SelectsIdentityOrNamedEncryption(
        string cryptFilter, bool remainsCleartext)
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture(), "owner-password");
        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference reference = update.AddObject(new PdfStream(new PdfDictionary([
            new(new PdfName("Filter"u8), new PdfName("Crypt"u8)),
            new(new PdfName("DecodeParms"u8), new PdfDictionary([
                new(new PdfName("Name"u8),
                    new PdfName(Encoding.ASCII.GetBytes(cryptFilter)))
            ]))
        ]), "explicit crypt payload"u8));

        byte[] updated = update.Build();
        PdfDocument reopened = PdfDocument.Open(updated, "user-password");
        PdfStream stream = Assert.IsType<PdfStream>(reopened.Resolve(reference));
        byte[] rewritten = PdfDocumentWriter.Write(reopened);
        PdfDocument rewrittenDocument = PdfDocument.Open(rewritten, "owner-password");
        PdfStream rewrittenStream = Assert.IsType<PdfStream>(
            rewrittenDocument.Resolve(reference.ObjectNumber));

        Assert.Equal("explicit crypt payload",
            Encoding.ASCII.GetString(PdfStreamDecoder.Decode(stream)));
        Assert.Equal("explicit crypt payload",
            Encoding.ASCII.GetString(PdfStreamDecoder.Decode(rewrittenStream)));
        Assert.Equal(remainsCleartext,
            updated.AsSpan().IndexOf("explicit crypt payload"u8) >= 0);
        Assert.Equal(remainsCleartext,
            rewritten.AsSpan().IndexOf("explicit crypt payload"u8) >= 0);
    }

    [Fact]
    public void ExplicitCryptFilter_MustBeFirstInStreamPipeline()
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture(), "owner-password");
        var update = new PdfIncrementalUpdateBuilder(document);
        update.AddObject(new PdfStream(new PdfDictionary([
            new(new PdfName("Filter"u8), new PdfArray([
                new PdfName("FlateDecode"u8), new PdfName("Crypt"u8)
            ]))
        ]), "payload"u8));

        Assert.Throws<InvalidOperationException>(() => update.Build());
    }

    [Fact]
    public void FullRewrite_PreservesAes256EncryptionAndDecryptedPageContent()
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture(), "owner-password");

        byte[] rewritten = PdfDocumentWriter.Write(document);
        PdfDocument reopened = PdfDocument.Open(rewritten, "user-password");
        PdfStream stream = Assert.IsType<PdfStream>(reopened.Resolve(4));

        Assert.True(reopened.IsEncrypted);
        Assert.Equal(
            "q\n0.9 0.2 0.4 rg\n72 72 200 100 re\nf\nQ\n",
            Encoding.ASCII.GetString(PdfStreamDecoder.Decode(stream)));
    }

    [Fact]
    public void FullRewrite_LeavesCrossReferenceStreamUnencrypted()
    {
        PdfDocument document = PdfDocument.Open(Revision6Fixture(), "owner-password");

        byte[] rewritten = PdfDocumentWriter.Write(document, new PdfDocumentWriteOptions
        {
            CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
            UseObjectStreams = true,
            CompressStructuralStreams = true
        });
        PdfDocument reopened = PdfDocument.Open(rewritten, "user-password");
        PdfStream stream = Assert.IsType<PdfStream>(reopened.Resolve(4));

        Assert.True(reopened.IsDecrypted);
        Assert.Equal(
            "q\n0.9 0.2 0.4 rg\n72 72 200 100 re\nf\nQ\n",
            Encoding.ASCII.GetString(PdfStreamDecoder.Decode(stream)));
    }

    internal static byte[] Revision6Fixture() => Convert.FromBase64String(
        "JVBERi0yLjAKJb/3ov4KMSAwIG9iago8PCAvUGFnZXMgMiAwIFIgL1R5cGUgL0NhdGFsb2cgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL0NvdW50IDEgL0tpZHMgWyAzIDAgUiBdIC9UeXBlIC9QYWdlcyA+PgplbmRvYmoKMyAwIG9iago8PCAvQ29udGVudHMgNCAwIFIgL01lZGlhQm94IFsgMCAwIDYxMiA3OTIgXSAvUGFyZW50IDIgMCBSIC9SZXNvdXJjZXMgPDwgPj4gL1R5cGUgL1BhZ2UgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCA2NCAvRmlsdGVyIC9GbGF0ZURlY29kZSA+PgpzdHJlYW0KYboOYzqGzywU4FQmfTIt96Axp9pkyFnOR1NeFIbac5yT14ig3iLyXR73If8yc+G9ntENo2/UKApMYIblUlmey2VuZHN0cmVhbQplbmRvYmoKNSAwIG9iago8PCAvQ0YgPDwgL1N0ZENGIDw8IC9BdXRoRXZlbnQgL0RvY09wZW4gL0NGTSAvQUVTVjMgL0xlbmd0aCAzMiA+PiA+PiAvRmlsdGVyIC9TdGFuZGFyZCAvTGVuZ3RoIDI1NiAvTyA8ZjNkNjA4M2JlMDQyMDNlNTlkNWJjNjViZjhhNWU3ZWFhOGM5MzYxNmZkMDllNTY0MzRmMjdjYzdiZTdkNzlmZTUyZGVmYjg5MDE2ZjdmOGJhZTJlNmE3YmEwYTIwYjg4PiAvT0UgPGYxNDk0NTAzMzI1NWVjODAwYmE2Mjc2MWMwNDlmZmViYjc3NDViY2MxZWNjZTAyZjRiYWY4NWQ1YzU2OGUxMTk+IC9QIC00IC9QZXJtcyA8YzQ0NjEzMGQ5ZTFkOGRhODI4MTNkNTUyNTFlODI5Mjk+IC9SIDYgL1N0bUYgL1N0ZENGIC9TdHJGIC9TdGRDRiAvVSA8ZDM2NjE1MjIzZDhlOTMxODU4Yzg0NmIxZDkxNDNiNzY4YmI1M2FkZWJkNmIxZjE1MWZkYzY0ZjgzZmE1NzEzODgyYmEyNTY0YmU5M2U1MzcxOGI2NzllMzBmNTJiYjgwPiAvVUUgPGZhNTc1MWY2YTdhYjE3MzdjZjI5NTU3YWY4NjE4ZmY2NzA0Y2U1ZDFkMTYxOTUxNWYxODc0MWJmMjRjYmYyOTQ+IC9WIDUgPj4KZW5kb2JqCnhyZWYKMCA2CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDAxNSAwMDAwMCBuIAowMDAwMDAwMDY0IDAwMDAwIG4gCjAwMDAwMDAxMjMgMDAwMDAgbiAKMDAwMDAwMDIyOSAwMDAwMCBuIAowMDAwMDAwMzYzIDAwMDAwIG4gCnRyYWlsZXIgPDwgL1Jvb3QgMSAwIFIgL1NpemUgNiAvSUQgWzw0YjhjNjg5ZWU5YTIxMmUwZWU5NGQxYzZhZGYxNmE1OT48ZDViMTI5YmM5ZjFhNzc1NmUwMjhkMzQxODY2MzNhMjQ+XSAvRW5jcnlwdCA1IDAgUiA+PgpzdGFydHhyZWYKOTEwCiUlRU9GCg==");

    private static byte[] Revision4Fixture() => Convert.FromBase64String(
        "JVBERi0yLjAKJb/3ov4KMSAwIG9iago8PCAvUGFnZXMgMiAwIFIgL1R5cGUgL0NhdGFsb2cgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL0NvdW50IDEgL0tpZHMgWyAzIDAgUiBdIC9UeXBlIC9QYWdlcyA+PgplbmRvYmoKMyAwIG9iago8PCAvQ29udGVudHMgNCAwIFIgL01lZGlhQm94IFsgMCAwIDYxMiA3OTIgXSAvUGFyZW50IDIgMCBSIC9SZXNvdXJjZXMgPDwgPj4gL1R5cGUgL1BhZ2UgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCA2NCAvRmlsdGVyIC9GbGF0ZURlY29kZSA+PgpzdHJlYW0KSecG3kRpaOSia/ml8moYxg5UAbhcuHXETddfMkbVaiLNqBIvOAbsW/taa+++E9SDkYUAvilCEhW/u1gABF0i+2VuZHN0cmVhbQplbmRvYmoKNSAwIG9iago8PCAvQ0YgPDwgL1N0ZENGIDw8IC9BdXRoRXZlbnQgL0RvY09wZW4gL0NGTSAvQUVTVjIgL0xlbmd0aCAxNiA+PiA+PiAvRmlsdGVyIC9TdGFuZGFyZCAvTGVuZ3RoIDEyOCAvTyA8ZmQ0YmUyZjAyYWI2YzMzOTUyYTg2NDBlYzFlZmFkZjRlY2Y3MTM4NTgyZDE0MTIzMmY0MDdjYmNiYzhmZDMwMz4gL09FIDw+IC9QIC00IC9SIDQgL1N0bUYgL1N0ZENGIC9TdHJGIC9TdGRDRiAvVSA8MmI0YzdmMDg0ODJmMTRhNzI0ZWNmY2I2OTg4YjRhZTYwMDIxNDQ2OTkwYjllNDExNDA3MWE0ZDkxMDQ5ODRjMT4gL1VFIDw+IC9WIDQgPj4KZW5kb2JqCnhyZWYKMCA2CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDAxNSAwMDAwMCBuIAowMDAwMDAwMDY0IDAwMDAwIG4gCjAwMDAwMDAxMjMgMDAwMDAgbiAKMDAwMDAwMDIyOSAwMDAwMCBuIAowMDAwMDAwMzYzIDAwMDAwIG4gCnRyYWlsZXIgPDwgL1Jvb3QgMSAwIFIgL1NpemUgNiAvSUQgWzw0YjhjNjg5ZWU5YTIxMmUwZWU5NGQxYzZhZGYxNmE1OT48OTI2YTkzNGFjNWM2MmEwYTA4MTgwMjdkOWQ5YTEyYzI+XSAvRW5jcnlwdCA1IDAgUiA+PgpzdGFydHhyZWYKNjc2CiUlRU9GCg==");

    private static byte[] Revision2Fixture() => Convert.FromBase64String(
        "JVBERi0yLjAKJb/3ov4KMSAwIG9iago8PCAvUGFnZXMgMiAwIFIgL1R5cGUgL0NhdGFsb2cgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL0NvdW50IDEgL0tpZHMgWyAzIDAgUiBdIC9UeXBlIC9QYWdlcyA+PgplbmRvYmoKMyAwIG9iago8PCAvQ29udGVudHMgNCAwIFIgL01lZGlhQm94IFsgMCAwIDYxMiA3OTIgXSAvUGFyZW50IDIgMCBSIC9SZXNvdXJjZXMgPDwgPj4gL1R5cGUgL1BhZ2UgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCA0MSAvRmlsdGVyIC9GbGF0ZURlY29kZSA+PgpzdHJlYW0KZaBGAVdUcOQTTqKiDmfiA7SZnHAUZxFG/VQF8F5tnO4kjPqx1cmsYkdlbmRzdHJlYW0KZW5kb2JqCjUgMCBvYmoKPDwgL0ZpbHRlciAvU3RhbmRhcmQgL0xlbmd0aCA0MCAvTyA8M2Q0YzFmYjdlOWE3Nzc3ODI3ZTZmNmRjZDMxZmEwMzQ4ZTg1NDExODliYWYwMGZlZTJiMjZlNmNlN2QyNzIzZD4gL1AgLTQgL1IgMiAvVSA8ODhiOWI0NjdkNjkwODk1OTNmMjIxOWY5YTZlNWZiMTc3NTZhNjkwMGMxMDcyMjY4NDcxZDM1NDdmOTRhZDBkYz4gL1YgMSA+PgplbmRvYmoKeHJlZgowIDYKMDAwMDAwMDAwMCA2NTUzNSBmIAowMDAwMDAwMDE1IDAwMDAwIG4gCjAwMDAwMDAwNjQgMDAwMDAgbiAKMDAwMDAwMDEyMyAwMDAwMCBuIAowMDAwMDAwMjI5IDAwMDAwIG4gCjAwMDAwMDAzNDAgMDAwMDAgbiAKdHJhaWxlciA8PCAvUm9vdCAxIDAgUiAvU2l6ZSA2IC9JRCBbPDRiOGM2ODllZTlhMjEyZTBlZTk0ZDFjNmFkZjE2YTU5PjwyYWFiOTA2NjI2ODFlY2Y5NTAwZmY3ZGU5ZjFmMjM2Mz5dIC9FbmNyeXB0IDUgMCBSID4+CnN0YXJ0eHJlZgo1NDYKJSVFT0YK");
}
