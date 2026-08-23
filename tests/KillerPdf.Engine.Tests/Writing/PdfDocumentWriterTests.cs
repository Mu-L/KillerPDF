using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;
using KillerPdf.Engine.Syntax;
using KillerPdf.Engine.Signing;
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
    public void Write_RequiresExplicitConsentToInvalidateExistingSignatures()
    {
        byte[] signedBytes = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build()),
            _ => [0x30, 0x00], new PdfSignatureOptions { ReservedSignatureSize = 16 });
        PdfDocument signed = PdfDocument.Open(signedBytes);

        Assert.Throws<InvalidOperationException>(() => PdfDocumentWriter.Write(signed));

        PdfDocument rewritten = PdfDocument.Open(PdfDocumentWriter.Write(signed,
            new PdfDocumentWriteOptions { AllowSignatureInvalidation = true }));
        PdfSignatureInfo signature = Assert.Single(PdfSignatureReader.Read(rewritten));
        Assert.True(signature.IsSigned);
        Assert.False(signature.HasValidByteRange);
    }

    [Fact]
    public void Write_RejectsCatalogCertificationEvenWithoutRegisteredField()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage().Build());
        PdfIndirectReference catalogReference = Assert.IsType<PdfIndirectReference>(
            document.Trailer[Name("Root")]);
        PdfDictionary catalog = Assert.IsType<PdfDictionary>(document.Resolve(catalogReference));
        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference parameters = update.AddObject(new PdfDictionary([
            new(Name("P"), new PdfInteger(1))
        ]));
        PdfIndirectReference transform = update.AddObject(new PdfDictionary([
            new(Name("TransformMethod"), Name("DocMDP")),
            new(Name("TransformParams"), parameters)
        ]));
        PdfIndirectReference signature = update.AddObject(new PdfDictionary([
            new(Name("Type"), Name("Sig")),
            new(Name("Reference"), new PdfArray([transform]))
        ]));
        update.ReplaceObject(catalogReference.ObjectNumber, new PdfDictionary(catalog.Append(
            new KeyValuePair<PdfName, PdfObject>(Name("Perms"), new PdfDictionary([
                new(Name("DocMDP"), signature)
            ])))));
        PdfDocument certified = PdfDocument.Open(update.Build());

        Assert.Throws<InvalidOperationException>(() => PdfDocumentWriter.Write(certified));
        Assert.NotEmpty(PdfDocumentWriter.Write(certified,
            new PdfDocumentWriteOptions { AllowSignatureInvalidation = true }));
    }

    [Fact]
    public void Write_IsByteStableAcrossRepeatedFullRewrites()
    {
        byte[] first = PdfDocumentWriter.Write(PdfDocument.Open(SourcePdf()));
        byte[] second = PdfDocumentWriter.Write(PdfDocument.Open(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_CanProduceAByteStableCrossReferenceStream()
    {
        var options = new PdfDocumentWriteOptions
        {
            CrossReferenceFormat = PdfCrossReferenceFormat.Stream
        };
        byte[] first = PdfDocumentWriter.Write(PdfDocument.Open(SourcePdf()), options);
        PdfDocument reopened = PdfDocument.Open(first);
        byte[] second = PdfDocumentWriter.Write(reopened, options);

        Assert.Equal(first, second);
        Assert.DoesNotContain("\nxref\n", Encoding.Latin1.GetString(first), StringComparison.Ordinal);
        Assert.Equal(PdfCrossReferenceEntryType.InUse,
            reopened.CrossReferences[3].Type);
        Assert.Equal("XRef", Assert.IsType<PdfName>(
            Assert.IsType<PdfStream>(reopened.Resolve(3)).Dictionary[Name("Type")]).ValueAsLatin1());
    }

    [Fact]
    public void Write_RejectsCrossReferenceStreamBeforePdf15()
    {
        PdfDocument document = PdfDocument.Open(SourcePdf(version: "1.4"));

        Assert.Throws<InvalidOperationException>(() => PdfDocumentWriter.Write(document,
            new PdfDocumentWriteOptions
            {
                CrossReferenceFormat = PdfCrossReferenceFormat.Stream
            }));
    }

    [Fact]
    public void Write_HonorsCatalogVersionOverrideForCrossReferenceStreams()
    {
        PdfDocument document = PdfDocument.Open(
            SourcePdf(version: "1.4", catalogVersion: "1.5"));

        PdfDocument reopened = PdfDocument.Open(PdfDocumentWriter.Write(document,
            new PdfDocumentWriteOptions
            {
                CrossReferenceFormat = PdfCrossReferenceFormat.Stream
            }));

        Assert.Equal(new PdfVersion(1, 4), reopened.Header.Version);
        Assert.True(reopened.CrossReferences.Sections[0].IsStream);
    }

    [Fact]
    public void Write_CanPackEligibleObjectsIntoAnObjectStream()
    {
        var options = new PdfDocumentWriteOptions
        {
            CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
            UseObjectStreams = true,
            CompressStructuralStreams = true
        };
        byte[] first = PdfDocumentWriter.Write(PdfDocument.Open(SourcePdf()), options);
        PdfDocument reopened = PdfDocument.Open(first);
        byte[] second = PdfDocumentWriter.Write(reopened, options);

        Assert.Equal(first, second);
        Assert.Equal(PdfCrossReferenceEntryType.Compressed,
            reopened.CrossReferences[1].Type);
        Assert.Equal("Catalog", Assert.IsType<PdfName>(
            Assert.IsType<PdfDictionary>(reopened.Resolve(1))[Name("Type")]).ValueAsLatin1());
        Assert.Equal("Hello", Encoding.ASCII.GetString(
            Assert.IsType<PdfStream>(reopened.Resolve(2)).EncodedData.Span));
        Assert.Equal("FlateDecode", Assert.IsType<PdfName>(
            Assert.IsType<PdfStream>(reopened.Resolve(3)).Dictionary[Name("Filter")]).ValueAsLatin1());
        Assert.Equal("FlateDecode", Assert.IsType<PdfName>(
            Assert.IsType<PdfStream>(reopened.Resolve(4)).Dictionary[Name("Filter")]).ValueAsLatin1());
    }

    [Fact]
    public void Write_RequiresCrossReferenceStreamWhenPackingObjects()
    {
        PdfDocument document = PdfDocument.Open(SourcePdf());

        Assert.Throws<InvalidOperationException>(() => PdfDocumentWriter.Write(document,
            new PdfDocumentWriteOptions { UseObjectStreams = true }));
    }

    [Fact]
    public void Write_RequiresCrossReferenceStreamWhenCompressingStructuralStreams()
    {
        PdfDocument document = PdfDocument.Open(SourcePdf());

        Assert.Throws<InvalidOperationException>(() => PdfDocumentWriter.Write(document,
            new PdfDocumentWriteOptions { CompressStructuralStreams = true }));
    }

    [Fact]
    public void Write_BoundsObjectStreamsToOneHundredObjects()
    {
        PdfDocument source = PdfDocument.Open(SourcePdf());
        var update = new PdfIncrementalUpdateBuilder(source);
        PdfIndirectReference[] added = Enumerable.Range(0, 101)
            .Select(index => update.AddObject(new PdfDictionary([
                new(Name("Value"), new PdfInteger(index))
            ]))).ToArray();
        PdfDocument expanded = PdfDocument.Open(update.Build());

        byte[] rewritten = PdfDocumentWriter.Write(expanded, new PdfDocumentWriteOptions
        {
            CrossReferenceFormat = PdfCrossReferenceFormat.Stream,
            UseObjectStreams = true,
            CompressStructuralStreams = true
        });
        PdfDocument reopened = PdfDocument.Open(rewritten);
        long[] streamNumbers = added.Select(reference =>
                reopened.CrossReferences[reference.ObjectNumber].Field1)
            .Distinct().ToArray();

        Assert.Equal(2, streamNumbers.Length);
        Assert.All(streamNumbers, number => Assert.Equal("ObjStm", Assert.IsType<PdfName>(
            Assert.IsType<PdfStream>(reopened.Resolve(checked((int)number)))
                .Dictionary[Name("Type")]).ValueAsLatin1()));
        Assert.Equal(100, Assert.IsType<PdfInteger>(
            Assert.IsType<PdfStream>(reopened.Resolve(checked((int)streamNumbers[0])))
                .Dictionary[Name("N")]).Value);
    }

    [Fact]
    public void Write_LinksFreeCrossReferenceStreamEntries()
    {
        PdfDocument source = PdfDocument.Open(SourcePdfWithObjectNumberGap());

        byte[] rewritten = PdfDocumentWriter.Write(source, new PdfDocumentWriteOptions
        {
            CrossReferenceFormat = PdfCrossReferenceFormat.Stream
        });
        PdfDocument reopened = PdfDocument.Open(rewritten);

        Assert.Equal(PdfCrossReferenceEntryType.Free, reopened.CrossReferences[0].Type);
        Assert.Equal(3, reopened.CrossReferences[0].Field1);
        for (int number = 3; number <= 8; number++)
            Assert.Equal(number + 1, reopened.CrossReferences[number].Field1);
        Assert.Equal(11, reopened.CrossReferences[9].Field1);
        Assert.Equal(0, reopened.CrossReferences[11].Field1);
        Assert.Equal(7, reopened.CrossReferences[5].Field2);
        Assert.Equal(65_535, reopened.CrossReferences[11].Field2);
        Assert.Equal(13, new PdfIncrementalUpdateBuilder(reopened).ReserveObject().ObjectNumber);
    }

    [Fact]
    public void Write_LinksFreeClassicCrossReferenceEntries()
    {
        PdfDocument source = PdfDocument.Open(SourcePdfWithObjectNumberGap());

        PdfDocument reopened = PdfDocument.Open(PdfDocumentWriter.Write(source));

        Assert.Equal(PdfCrossReferenceEntryType.Free, reopened.CrossReferences[0].Type);
        Assert.Equal(3, reopened.CrossReferences[0].Field1);
        for (int number = 3; number <= 8; number++)
            Assert.Equal(number + 1, reopened.CrossReferences[number].Field1);
        Assert.Equal(11, reopened.CrossReferences[9].Field1);
        Assert.Equal(0, reopened.CrossReferences[11].Field1);
        Assert.Equal(7, reopened.CrossReferences[5].Field2);
        Assert.Equal(65_535, reopened.CrossReferences[11].Field2);
        Assert.Equal(12, new PdfIncrementalUpdateBuilder(reopened).ReserveObject().ObjectNumber);
    }

    [Fact]
    public void Write_RequiresPasswordBeforeEncryptedRewrite()
    {
        PdfDocument document = PdfDocument.Open(SourcePdf(" /Encrypt 9 0 R"));

        Assert.Throws<InvalidOperationException>(() => PdfDocumentWriter.Write(document));
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

    [Fact]
    public void Write_PhysicallyRemovesDedicatedDocumentInformationObject()
    {
        PdfDocument source = PdfDocument.Open(SourcePdfWithDocumentInformation());

        byte[] rewritten = PdfDocumentWriter.Write(source, new PdfDocumentWriteOptions
        {
            MetadataPolicy = PdfMetadataPolicy.RemoveDocumentInformation
        });
        PdfDocument reopened = PdfDocument.Open(rewritten);

        Assert.False(reopened.Trailer.ContainsKey(Name("Info")));
        Assert.Equal(PdfCrossReferenceEntryType.Free, reopened.CrossReferences[3].Type);
        Assert.Equal(1, reopened.CrossReferences[3].Field2);
        Assert.DoesNotContain("private metadata", Encoding.Latin1.GetString(rewritten),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RejectsPhysicalInformationRemovalWhenObjectIsShared()
    {
        PdfDocument source = PdfDocument.Open(SourcePdfWithDocumentInformation(shared: true));

        Assert.Throws<NotSupportedException>(() => PdfDocumentWriter.Write(source,
            new PdfDocumentWriteOptions
            {
                MetadataPolicy = PdfMetadataPolicy.RemoveDocumentInformation
            }));
    }

    [Theory]
    [InlineData(PdfCrossReferenceFormat.Table)]
    [InlineData(PdfCrossReferenceFormat.Stream)]
    public void Write_PreservesNonStructuralTrailerEntriesAndDropsStaleChecksum(
        PdfCrossReferenceFormat format)
    {
        PdfDocument source = PdfDocument.Open(SourcePdf(
            " /PrivateState << /Enabled true >> /DocChecksum <01020304>"));

        PdfDocument reopened = PdfDocument.Open(PdfDocumentWriter.Write(source,
            new PdfDocumentWriteOptions { CrossReferenceFormat = format }));

        PdfDictionary state = Assert.IsType<PdfDictionary>(
            reopened.Trailer[Name("PrivateState")]);
        Assert.True(Assert.IsType<PdfBoolean>(state[Name("Enabled")]).Value);
        Assert.False(reopened.Trailer.ContainsKey(Name("DocChecksum")));
        Assert.False(reopened.Trailer.ContainsKey(Name("Prev")));
        Assert.False(reopened.Trailer.ContainsKey(Name("XRefStm")));
    }

    [Theory]
    [InlineData(PdfCrossReferenceFormat.Table, 20)]
    [InlineData(PdfCrossReferenceFormat.Stream, 21)]
    public void Write_PreservesSparseTrailerSizeHighWaterMark(
        PdfCrossReferenceFormat format, int nextObjectNumber)
    {
        PdfDocument source = PdfDocument.Open(SourcePdf(declaredSize: 20));

        PdfDocument reopened = PdfDocument.Open(PdfDocumentWriter.Write(source,
            new PdfDocumentWriteOptions { CrossReferenceFormat = format }));

        Assert.Equal(PdfCrossReferenceEntryType.Free, reopened.CrossReferences[19].Type);
        Assert.Equal(nextObjectNumber,
            new PdfIncrementalUpdateBuilder(reopened).ReserveObject().ObjectNumber);
    }

    [Fact]
    public void Write_RemovesObsoleteLinearizationDictionary()
    {
        PdfDocument document = PdfDocument.Open(SourcePdfWithLinearizationDictionary());

        byte[] rewritten = PdfDocumentWriter.Write(document);
        PdfDocument reopened = PdfDocument.Open(rewritten);

        Assert.False(reopened.CrossReferences.ContainsKey(3));
        Assert.DoesNotContain("/Linearized", Encoding.Latin1.GetString(rewritten),
            StringComparison.Ordinal);
    }

    private static byte[] SourcePdf(
        string extraTrailer = "", string version = "2.0", string? catalogVersion = null,
        int declaredSize = 3)
    {
        var source = new StringBuilder($"%PDF-{version}\n");
        int catalogOffset = source.Length;
        source.Append($"1 0 obj << /Type /Catalog /Data 2 0 R{(catalogVersion is null ? "" : $" /Version /{catalogVersion}")} >> endobj\n");
        int streamOffset = source.Length;
        source.Append("2 0 obj << /Length 5 >> stream\nHello\nendstream endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 3\n0000000000 65535 f\n");
        source.Append($"{catalogOffset:0000000000} 00000 n\n");
        source.Append($"{streamOffset:0000000000} 00000 n\n");
        source.Append($"trailer << /Size {declaredSize} /Root 1 0 R{extraTrailer} >>\n");
        source.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }

    private static byte[] SourcePdfWithLinearizationDictionary()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int catalogOffset = source.Length;
        source.Append("1 0 obj << /Type /Catalog /Data 2 0 R >> endobj\n");
        int streamOffset = source.Length;
        source.Append("2 0 obj << /Length 5 >> stream\nHello\nendstream endobj\n");
        int linearizationOffset = source.Length;
        source.Append("3 0 obj << /Linearized 1 /L 999 /H [0 0] /O 1 /E 0 /N 1 /T 0 >> endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 4\n0000000000 65535 f\n");
        source.Append($"{catalogOffset:0000000000} 00000 n\n");
        source.Append($"{streamOffset:0000000000} 00000 n\n");
        source.Append($"{linearizationOffset:0000000000} 00000 n\n");
        source.Append($"trailer << /Size 4 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }

    private static byte[] SourcePdfWithDocumentInformation(bool shared = false)
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int catalogOffset = source.Length;
        source.Append($"1 0 obj << /Type /Catalog{(shared ? " /SharedInfo 3 0 R" : "")} >> endobj\n");
        int pageDataOffset = source.Length;
        source.Append("2 0 obj << /Value 2 >> endobj\n");
        int infoOffset = source.Length;
        source.Append("3 0 obj << /Title (private metadata) >> endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 4\n0000000000 65535 f\n");
        source.Append($"{catalogOffset:0000000000} 00000 n\n");
        source.Append($"{pageDataOffset:0000000000} 00000 n\n");
        source.Append($"{infoOffset:0000000000} 00000 n\n");
        source.Append($"trailer << /Size 4 /Root 1 0 R /Info 3 0 R >>\n" +
            $"startxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }

    private static byte[] SourcePdfWithObjectNumberGap()
    {
        var source = new StringBuilder("%PDF-2.0\n");
        int catalogOffset = source.Length;
        source.Append("1 0 obj << /Type /Catalog /Data 2 0 R /Extra 10 0 R >> endobj\n");
        int streamOffset = source.Length;
        source.Append("2 0 obj << /Length 5 >> stream\nHello\nendstream endobj\n");
        int extraOffset = source.Length;
        source.Append("10 0 obj << /Value 10 >> endobj\n");
        int xrefOffset = source.Length;
        source.Append("xref\n0 3\n0000000000 65535 f\n");
        source.Append($"{catalogOffset:0000000000} 00000 n\n");
        source.Append($"{streamOffset:0000000000} 00000 n\n");
        source.Append("5 1\n0000000000 00007 f\n");
        source.Append("10 1\n");
        source.Append($"{extraOffset:0000000000} 00000 n\n");
        source.Append("11 1\n0000000000 65535 f\n");
        source.Append($"trailer << /Size 12 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(source.ToString());
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
