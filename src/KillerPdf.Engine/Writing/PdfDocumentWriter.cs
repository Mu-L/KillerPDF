using System.Globalization;
using System.IO.Compression;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;
using KillerPdf.Engine.Signing;

namespace KillerPdf.Engine.Writing;

/// <summary>Produces a deterministic full rewrite of the current merged document revision.</summary>
public static class PdfDocumentWriter
{
    private const int MaximumObjectsPerObjectStream = 100;
    private static readonly PdfName TypeName = new("Type"u8);
    private static readonly PdfName XRefName = new("XRef"u8);
    private static readonly PdfName ObjStmName = new("ObjStm"u8);
    private static readonly PdfName SizeName = new("Size"u8);
    private static readonly PdfName RootName = new("Root"u8);
    private static readonly PdfName InfoName = new("Info"u8);
    private static readonly PdfName IdName = new("ID"u8);
    private static readonly PdfName VersionName = new("Version"u8);
    private static readonly PdfName EncryptName = new("Encrypt"u8);
    private static readonly PdfName WName = new("W"u8);
    private static readonly PdfName LengthName = new("Length"u8);
    private static readonly PdfName NName = new("N"u8);
    private static readonly PdfName FirstName = new("First"u8);
    private static readonly PdfName FilterName = new("Filter"u8);
    private static readonly PdfName FlateDecodeName = new("FlateDecode"u8);
    private static readonly PdfName LinearizedName = new("Linearized"u8);
    private static readonly PdfName LinearizedLengthName = new("L"u8);
    private static readonly PdfName LinearizedHintsName = new("H"u8);
    private static readonly PdfName LinearizedFirstPageName = new("O"u8);
    private static readonly PdfName LinearizedEndName = new("E"u8);
    private static readonly PdfName LinearizedPageCountName = new("N"u8);
    private static readonly PdfName LinearizedXrefName = new("T"u8);
    private static readonly PdfName PrevName = new("Prev"u8);
    private static readonly PdfName XRefStmName = new("XRefStm"u8);
    private static readonly PdfName IndexName = new("Index"u8);
    private static readonly PdfName DecodeParmsName = new("DecodeParms"u8);
    private static readonly PdfName DlName = new("DL"u8);
    private static readonly PdfName DocChecksumName = new("DocChecksum"u8);
    private static readonly HashSet<PdfName> StructuralTrailerNames =
    [
        SizeName, PrevName, XRefStmName, RootName, InfoName, IdName, EncryptName,
        TypeName, WName, IndexName, LengthName, FilterName, DecodeParmsName, DlName,
        DocChecksumName
    ];

    public static byte[] Write(PdfDocument document, PdfDocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new PdfDocumentWriteOptions();
        PdfVersion outputVersion = options.TargetVersion ?? document.Header.Version;
        if (outputVersion.CompareTo(document.Header.Version) < 0)
            throw new NotSupportedException("A full rewrite cannot downgrade the source PDF version without feature analysis.");
        if (document.IsEncrypted && !document.IsDecrypted)
            throw new InvalidOperationException(
                "An encrypted PDF must be opened with a password before it can be rewritten.");
        if (!options.AllowSignatureInvalidation)
        {
            bool hasCertification =
                PdfSignatureReader.ReadCertificationPermission(document).HasValue;
            if (hasCertification
                || PdfSignatureReader.Read(document).Any(signature => signature.IsSigned))
                throw new InvalidOperationException(
                    "A full rewrite invalidates existing signatures. Set AllowSignatureInvalidation explicitly to proceed.");
        }
        if (!document.CrossReferences.TryGetTrailerValue(RootName, out PdfObject root))
            throw new InvalidOperationException("A full rewrite requires a trailer /Root reference.");
        PdfVersion effectiveOutputVersion = options.CrossReferenceFormat == PdfCrossReferenceFormat.Stream
            ? EffectiveVersion(document, root, outputVersion) : outputVersion;
        if (options.UseObjectStreams && options.CrossReferenceFormat != PdfCrossReferenceFormat.Stream)
            throw new InvalidOperationException("Object streams require the cross-reference-stream format.");
        if (options.CompressStructuralStreams
            && options.CrossReferenceFormat != PdfCrossReferenceFormat.Stream)
            throw new InvalidOperationException(
                "Structural-stream compression requires the cross-reference-stream format.");

        List<WritableObject> objects = ReadCurrentObjects(document);
        RemoveDocumentInformationObject(document, root, objects, options.MetadataPolicy);
        int maximumObjectNumber = Math.Max(
            Math.Max(
                objects.Select(item => item.ObjectNumber).DefaultIfEmpty(0).Max(),
                document.CrossReferences.Values
                    .Where(entry => entry.Type == PdfCrossReferenceEntryType.Free)
                    .Select(entry => entry.ObjectNumber).DefaultIfEmpty(0).Max()),
            DeclaredSparseHighWater(document));
        if (maximumObjectNumber == int.MaxValue)
            throw new NotSupportedException("The PDF object-number range is too large to rewrite.");

        using var output = new MemoryStream();
        WriteAscii(output, $"%PDF-{outputVersion}\n");
        output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        int? encryptionObjectNumber = document.CrossReferences.TryGetTrailerValue(
            EncryptName, out PdfObject encryptionValue)
            && encryptionValue is PdfIndirectReference encryptionReference
                ? encryptionReference.ObjectNumber : null;
        List<WritableObject> packed = options.UseObjectStreams
            ? objects.Where(item => item.Generation == 0 && item.Value is not PdfStream
                && item.ObjectNumber != encryptionObjectNumber).ToList()
            : [];
        var packedNumbers = packed.Select(item => item.ObjectNumber).ToHashSet();
        int objectStreamCount = packed.Count == 0
            ? 0 : (packed.Count - 1) / MaximumObjectsPerObjectStream + 1;
        if (maximumObjectNumber > int.MaxValue - objectStreamCount - 1)
            throw new NotSupportedException(
                "The PDF object-number range has no room for structural streams.");
        List<ObjectStreamChunk> objectStreams = packed.Chunk(MaximumObjectsPerObjectStream)
            .Select((items, index) => new ObjectStreamChunk(
                checked(maximumObjectNumber + index + 1), items))
            .ToList();
        int xrefObjectNumber = checked(maximumObjectNumber + objectStreams.Count + 1);

        var offsets = new List<WrittenOffset>(objects.Count);
        foreach (WritableObject item in objects.Where(item => !packedNumbers.Contains(item.ObjectNumber)))
        {
            int offset = checked((int)output.Position);
            offsets.Add(new WrittenOffset(item.ObjectNumber, item.Generation, offset));
            PdfObject value = document.EncryptObject(item.ObjectNumber, item.Value);
            PdfObjectWriter.Write(
                output,
                new PdfIndirectObject(item.ObjectNumber, item.Generation, value, offset));
        }
        foreach (ObjectStreamChunk chunk in objectStreams)
        {
            int offset = checked((int)output.Position);
            offsets.Add(new WrittenOffset(chunk.ObjectNumber, 0, offset));
            PdfStream objectStream = BuildObjectStream(
                chunk.Objects, options.CompressStructuralStreams);
            PdfObjectWriter.Write(output, new PdfIndirectObject(chunk.ObjectNumber, 0,
                document.EncryptObject(chunk.ObjectNumber, objectStream), offset));
        }

        int xrefOffset = checked((int)output.Position);
        if (options.CrossReferenceFormat == PdfCrossReferenceFormat.Stream)
        {
            if (effectiveOutputVersion.CompareTo(new PdfVersion(1, 5)) < 0)
                throw new InvalidOperationException("Cross-reference streams require PDF 1.5 or later.");
            WriteCrossReferenceStream(output, document, offsets,
                xrefObjectNumber, xrefOffset, root, options,
                objectStreams);
        }
        else
        {
            WriteClassicCrossReferenceTable(
                output, document, offsets, maximumObjectNumber + 1);

            output.Write("trailer\n"u8);
            PdfObjectWriter.Write(output, BuildTrailer(document, maximumObjectNumber + 1, root, options));
            output.WriteByte((byte)'\n');
        }
        output.Write("startxref\n"u8);
        WriteAscii(output, xrefOffset.ToString(CultureInfo.InvariantCulture));
        output.Write("\n%%EOF\n"u8);
        return output.ToArray();
    }

    private static void WriteClassicCrossReferenceTable(
        Stream output, PdfDocument document,
        IReadOnlyList<WrittenOffset> offsets, int size)
    {
        var occupied = offsets.ToDictionary(item => item.ObjectNumber);
        int[] free = Enumerable.Range(1, size - 1)
            .Where(number => !occupied.ContainsKey(number)).ToArray();
        var nextFree = free.Select((number, index) => new
            {
                Number = number,
                Next = index + 1 < free.Length ? free[index + 1] : 0
            })
            .ToDictionary(item => item.Number, item => item.Next);
        output.Write("xref\n"u8);
        WriteAscii(output, $"0 {size}\n");
        WriteAscii(output, $"{free.FirstOrDefault():0000000000} 65535 f \n");
        for (int number = 1; number < size; number++)
        {
            if (occupied.TryGetValue(number, out WrittenOffset? item))
                WriteAscii(output, $"{item.Offset:0000000000} {item.Generation:00000} n \n");
            else
                WriteAscii(output,
                    $"{nextFree[number]:0000000000} {FreeGeneration(document, number):00000} f \n");
        }
    }

    private static void WriteCrossReferenceStream(Stream output, PdfDocument document,
        IReadOnlyList<WrittenOffset> offsets, int objectNumber, int xrefOffset,
        PdfObject root, PdfDocumentWriteOptions options,
        IReadOnlyList<ObjectStreamChunk> objectStreams)
    {
        int size = checked(objectNumber + 1);
        var rows = new byte[checked(size * 9)];
        var occupied = new HashSet<int> { objectNumber };
        occupied.UnionWith(offsets.Select(item => item.ObjectNumber));
        foreach (ObjectStreamChunk chunk in objectStreams)
            occupied.UnionWith(chunk.Objects.Select(item => item.ObjectNumber));
        int[] free = Enumerable.Range(1, size - 1)
            .Where(number => !occupied.Contains(number)).ToArray();
        WriteXrefRow(rows, 0, 0, free.FirstOrDefault(), 65535);
        for (int index = 0; index < free.Length; index++)
            WriteXrefRow(rows, free[index], 0,
                index + 1 < free.Length ? free[index + 1] : 0,
                FreeGeneration(document, free[index]));
        foreach (WrittenOffset item in offsets)
            WriteXrefRow(rows, item.ObjectNumber, 1, item.Offset, item.Generation);
        foreach (ObjectStreamChunk chunk in objectStreams)
            for (int index = 0; index < chunk.Objects.Count; index++)
                WriteXrefRow(rows, chunk.Objects[index].ObjectNumber, 2,
                    chunk.ObjectNumber, index);
        WriteXrefRow(rows, objectNumber, 1, xrefOffset, 0);

        var entries = BuildTrailer(document, size, root, options).ToList();
        entries.Add(new(TypeName, XRefName));
        entries.Add(new(WName, new PdfArray([
            new PdfInteger(1), new PdfInteger(4), new PdfInteger(4)])));
        entries.Add(new(LengthName, new PdfInteger(rows.Length)));
        byte[] encodedRows = options.CompressStructuralStreams ? Compress(rows) : rows;
        if (options.CompressStructuralStreams)
            entries.Add(new(FilterName, FlateDecodeName));
        entries.RemoveAll(entry => entry.Key.Equals(LengthName));
        entries.Add(new(LengthName, new PdfInteger(encodedRows.Length)));
        var stream = new PdfStream(new PdfDictionary(entries), encodedRows);
        PdfObjectWriter.Write(output,
            new PdfIndirectObject(objectNumber, 0, stream, xrefOffset));
    }

    private static PdfStream BuildObjectStream(
        IReadOnlyList<WritableObject> objects, bool compress)
    {
        using var body = new MemoryStream();
        var offsets = new int[objects.Count];
        for (int index = 0; index < objects.Count; index++)
        {
            offsets[index] = checked((int)body.Position);
            PdfObjectWriter.Write(body, objects[index].Value);
            body.WriteByte((byte)'\n');
        }
        using var header = new MemoryStream();
        for (int index = 0; index < objects.Count; index++)
            WriteAscii(header, $"{objects[index].ObjectNumber} {offsets[index]} ");
        int first = checked((int)header.Length);
        header.Write(body.ToArray());
        byte[] data = header.ToArray();
        var entries = new List<KeyValuePair<PdfName, PdfObject>>
        {
            new(TypeName, ObjStmName),
            new(NName, new PdfInteger(objects.Count)),
            new(FirstName, new PdfInteger(first))
        };
        if (compress)
        {
            data = Compress(data);
            entries.Add(new(FilterName, FlateDecodeName));
        }
        return new PdfStream(new PdfDictionary(entries), data);
    }

    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    private static void WriteXrefRow(byte[] rows, int objectNumber, byte type, int field1, int field2)
    {
        int offset = checked(objectNumber * 9);
        rows[offset] = type;
        rows[offset + 1] = (byte)(field1 >> 24);
        rows[offset + 2] = (byte)(field1 >> 16);
        rows[offset + 3] = (byte)(field1 >> 8);
        rows[offset + 4] = (byte)field1;
        rows[offset + 5] = (byte)(field2 >> 24);
        rows[offset + 6] = (byte)(field2 >> 16);
        rows[offset + 7] = (byte)(field2 >> 8);
        rows[offset + 8] = (byte)field2;
    }

    private static int FreeGeneration(PdfDocument document, int objectNumber)
    {
        if (!document.CrossReferences.TryGetValue(objectNumber, out PdfCrossReferenceEntry entry))
            return 0;
        return entry.Type switch
        {
            PdfCrossReferenceEntryType.Free => entry.Field2,
            PdfCrossReferenceEntryType.InUse => Math.Min(65_535, checked(entry.Field2 + 1)),
            PdfCrossReferenceEntryType.Compressed => 1,
            _ => 0
        };
    }

    private static int DeclaredSparseHighWater(PdfDocument document)
    {
        if (!document.CrossReferences.TryGetTrailerValue(SizeName, out PdfObject sizeValue)
            || sizeValue is not PdfInteger size
            || size.Value is <= 1 or > int.MaxValue)
            return 0;
        int high = checked((int)size.Value - 1);
        if (!document.CrossReferences.TryGetValue(high, out PdfCrossReferenceEntry entry))
            return high;
        if (entry.Type is PdfCrossReferenceEntryType.Free or PdfCrossReferenceEntryType.Null)
            return high;
        PdfObject value = document.Resolve(high);
        return IsObsoleteStructuralObject(value) ? 0 : high;
    }

    private static List<WritableObject> ReadCurrentObjects(PdfDocument document)
    {
        var result = new List<WritableObject>();
        foreach (PdfCrossReferenceEntry entry in document.CrossReferences.Values
                     .Where(entry => entry.Type is PdfCrossReferenceEntryType.InUse or PdfCrossReferenceEntryType.Compressed)
                     .OrderBy(entry => entry.ObjectNumber))
        {
            PdfObject value = document.Resolve(entry.ObjectNumber);
            if (IsObsoleteStructuralObject(value))
                continue;
            int generation = entry.Type == PdfCrossReferenceEntryType.InUse ? entry.Field2 : 0;
            result.Add(new WritableObject(entry.ObjectNumber, generation, value));
        }
        return result;
    }

    private static void RemoveDocumentInformationObject(
        PdfDocument document, PdfObject root, List<WritableObject> objects,
        PdfMetadataPolicy policy)
    {
        if (policy != PdfMetadataPolicy.RemoveDocumentInformation
            || !document.CrossReferences.TryGetTrailerValue(InfoName, out PdfObject infoValue)
            || infoValue is not PdfIndirectReference infoReference)
            return;
        if (root is PdfIndirectReference rootReference
            && rootReference.ObjectNumber == infoReference.ObjectNumber)
            return;
        if (document.CrossReferences.TryGetTrailerValue(EncryptName, out PdfObject encryptValue)
            && encryptValue is PdfIndirectReference encryptReference
            && encryptReference.ObjectNumber == infoReference.ObjectNumber)
            return;
        if (objects.Any(item => item.ObjectNumber != infoReference.ObjectNumber
                && ReferencesObject(item.Value, infoReference.ObjectNumber))
            || document.Trailer.Any(entry => !entry.Key.Equals(InfoName)
                && ReferencesObject(entry.Value, infoReference.ObjectNumber)))
            throw new NotSupportedException(
                "The document information object is shared outside trailer /Info and cannot be removed safely.");
        objects.RemoveAll(item => item.ObjectNumber == infoReference.ObjectNumber);
    }

    private static bool ReferencesObject(PdfObject value, int objectNumber) => value switch
    {
        PdfIndirectReference reference => reference.ObjectNumber == objectNumber,
        PdfArray array => array.Any(item => ReferencesObject(item, objectNumber)),
        PdfDictionary dictionary => dictionary.Any(entry => ReferencesObject(entry.Value, objectNumber)),
        PdfStream stream => ReferencesObject(stream.Dictionary, objectNumber),
        _ => false
    };

    private static bool IsObsoleteStructuralObject(PdfObject value)
    {
        if (value is PdfDictionary dictionary
            && new[] { LinearizedName, LinearizedLengthName, LinearizedHintsName,
                LinearizedFirstPageName, LinearizedEndName, LinearizedPageCountName,
                LinearizedXrefName }.All(dictionary.ContainsKey))
            return true;
        if (value is not PdfStream stream
            || !stream.Dictionary.TryGetValue(TypeName, out PdfObject type)
            || type is not PdfName name)
            return false;
        return name.Equals(XRefName) || name.Equals(ObjStmName);
    }

    private static PdfVersion EffectiveVersion(
        PdfDocument document, PdfObject rootValue, PdfVersion headerVersion)
    {
        PdfObject root = rootValue is PdfIndirectReference reference
            ? document.Resolve(reference) : rootValue;
        if (root is not PdfDictionary catalog
            || !catalog.TryGetValue(VersionName, out PdfObject versionValue))
            return headerVersion;
        if (versionValue is not PdfName versionName)
            throw new InvalidOperationException("The catalog /Version value is not a name.");
        string text = versionName.ValueAsLatin1();
        if (text.Length != 3 || text[1] != '.'
            || text[0] is < '0' or > '9' || text[2] is < '0' or > '9')
            throw new InvalidOperationException("The catalog /Version value is not a PDF version.");
        int major = text[0] - '0';
        int minor = text[2] - '0';
        if (!PdfVersion.IsDefined(major, minor))
            throw new InvalidOperationException(
                $"The catalog /Version PDF {major}.{minor} is not defined.");
        PdfVersion declared = new(major, minor);
        return declared.CompareTo(headerVersion) > 0 ? declared : headerVersion;
    }

    private static PdfDictionary BuildTrailer(
        PdfDocument document,
        int size,
        PdfObject root,
        PdfDocumentWriteOptions options)
    {
        var entries = new List<KeyValuePair<PdfName, PdfObject>>
        {
            new(SizeName, new PdfInteger(size)),
            new(RootName, root)
        };
        foreach ((PdfName name, PdfObject value) in document.Trailer)
            if (!StructuralTrailerNames.Contains(name))
                entries.Add(new KeyValuePair<PdfName, PdfObject>(name, value));
        if (options.MetadataPolicy == PdfMetadataPolicy.Preserve)
            AddInherited(document, entries, InfoName);
        if (options.PreserveDocumentIdentifiers)
            AddInherited(document, entries, IdName);
        if (document.IsEncrypted)
        {
            if (!entries.Any(entry => entry.Key.Equals(IdName)))
                AddInherited(document, entries, IdName);
            AddInherited(document, entries, EncryptName);
        }
        return new PdfDictionary(entries);
    }

    private static void AddInherited(
        PdfDocument document,
        List<KeyValuePair<PdfName, PdfObject>> entries,
        PdfName name)
    {
        if (document.CrossReferences.TryGetTrailerValue(name, out PdfObject value))
            entries.Add(new KeyValuePair<PdfName, PdfObject>(name, value));
    }

    private static void WriteAscii(Stream output, string value)
    {
        foreach (char character in value)
            output.WriteByte(checked((byte)character));
    }

    private sealed record WritableObject(int ObjectNumber, int Generation, PdfObject Value);
    private sealed record WrittenOffset(int ObjectNumber, int Generation, int Offset);
    private sealed record ObjectStreamChunk(int ObjectNumber, IReadOnlyList<WritableObject> Objects);
}
