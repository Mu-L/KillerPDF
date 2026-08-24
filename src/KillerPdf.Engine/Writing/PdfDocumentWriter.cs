using System.Globalization;
using System.IO.Compression;
using System.Text;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Security;
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
    private static readonly PdfName MetadataName = new("Metadata"u8);
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
        if (!Enum.IsDefined(options.MetadataPolicy))
            throw new ArgumentOutOfRangeException(nameof(options),
                "The metadata policy is not defined.");
        if (!Enum.IsDefined(options.CrossReferenceFormat))
            throw new ArgumentOutOfRangeException(nameof(options),
                "The cross-reference format is not defined.");
        PdfVersion outputVersion = options.TargetVersion ?? document.Header.Version;
        if (!PdfVersion.IsDefined(outputVersion.Major, outputVersion.Minor))
            throw new ArgumentOutOfRangeException(nameof(options),
                "The target PDF version is not defined.");
        if (outputVersion.CompareTo(document.Header.Version) < 0)
            throw new NotSupportedException("A full rewrite cannot downgrade the source PDF version without feature analysis.");
        if (document.IsEncrypted && !document.IsDecrypted)
            throw new InvalidOperationException(
                "An encrypted PDF must be opened with a password before it can be rewritten.");
        if (document.PasswordAuthenticationRole == PdfPasswordAuthenticationRole.User
            && (document.DeclaredPermissions is not PdfDocumentPermissions permissions
                || !permissions.AllowDocumentModification))
            throw new InvalidOperationException(
                "The PDF user password does not permit a full document rewrite.");
        if (!document.CrossReferences.TryGetTrailerValue(RootName, out PdfObject root))
            throw new InvalidOperationException("A full rewrite requires a trailer /Root reference.");
        if (root is not PdfIndirectReference rootReference)
            throw new InvalidOperationException(
                "A full rewrite requires trailer /Root to be an indirect reference.");
        if (ResolveValue(document, rootReference,
                "The trailer /Root value") is not PdfDictionary rootCatalog
            || !rootCatalog.TryGetValue(TypeName, out PdfObject rootType)
            || ResolveValue(document, rootType,
                "The catalog /Type value") is not PdfName rootTypeName
            || rootTypeName.ValueAsLatin1() != "Catalog")
            throw new InvalidOperationException(
                "A full rewrite requires trailer /Root to resolve to a catalog dictionary.");
        if (options.MetadataPolicy == PdfMetadataPolicy.Preserve
            && document.CrossReferences.TryGetTrailerValue(InfoName, out PdfObject info))
        {
            if (info is not PdfIndirectReference infoReference)
                throw new InvalidOperationException(
                    "A full rewrite requires trailer /Info to be an indirect reference.");
            if (ResolveValue(document, infoReference,
                    "The trailer /Info value") is not PdfDictionary informationDictionary)
                throw new InvalidOperationException(
                    "A full rewrite requires trailer /Info to resolve to a dictionary.");
            ValidateDocumentInformation(informationDictionary,
                value => ResolveValue(document, value,
                    "A document-information value"));
            ValidateDocumentInformationGraph(document, informationDictionary);
        }
        bool preservesIdentifiers = options.PreserveDocumentIdentifiers || document.IsEncrypted;
        bool hasIdentifiers = document.CrossReferences.TryGetTrailerValue(
            IdName, out PdfObject identifiers);
        if (preservesIdentifiers && hasIdentifiers
            && (identifiers is not PdfArray identifierArray
                || identifierArray.Count != 2
                || identifierArray.Any(item => item is not PdfString)))
            throw new InvalidOperationException(
                "A full rewrite requires trailer /ID to be an array of two strings.");
        if (document.IsEncrypted && !hasIdentifiers)
            throw new InvalidOperationException(
                "An encrypted full rewrite requires trailer /ID document identifiers.");
        ValidateApplicationTrailerGraphs(document);
        if (!options.AllowSignatureInvalidation)
        {
            bool hasCertification =
                PdfSignatureReader.ReadCertificationPermission(document).HasValue;
            if (hasCertification
                || PdfSignatureReader.Read(document).Any(signature => signature.IsSigned))
                throw new InvalidOperationException(
                    "A full rewrite invalidates existing signatures. Set AllowSignatureInvalidation explicitly to proceed.");
        }
        PdfVersion effectiveOutputVersion = EffectiveVersion(document, root, outputVersion);
        if (options.UseObjectStreams && options.CrossReferenceFormat != PdfCrossReferenceFormat.Stream)
            throw new InvalidOperationException("Object streams require the cross-reference-stream format.");
        if (options.CompressStructuralStreams
            && options.CrossReferenceFormat != PdfCrossReferenceFormat.Stream)
            throw new InvalidOperationException(
                "Structural-stream compression requires the cross-reference-stream format.");

        List<WritableObject> objects = ReadCurrentObjects(document);
        root = RemoveCatalogMetadata(document, root, objects, options.MetadataPolicy);
        RemoveDocumentInformationObject(document, root, objects, options.MetadataPolicy);
        ValidateCatalogGraph(root, objects);
        ValidateOutputTrailerGraphs(document, objects, options);
        ValidateWritableObjectGraphs(objects);
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
        if (policy == PdfMetadataPolicy.Preserve
            || !document.CrossReferences.TryGetTrailerValue(InfoName, out PdfObject infoValue)
            || infoValue is not PdfIndirectReference infoReference)
            return;
        var infoIdentity = (infoReference.ObjectNumber, infoReference.Generation);
        if (root is PdfIndirectReference rootReference
            && (rootReference.ObjectNumber, rootReference.Generation) == infoIdentity)
            return;
        if (document.CrossReferences.TryGetTrailerValue(EncryptName, out PdfObject encryptValue)
            && encryptValue is PdfIndirectReference encryptReference
            && (encryptReference.ObjectNumber, encryptReference.Generation) == infoIdentity)
            return;
        if (objects.Any(item => (item.ObjectNumber, item.Generation) != infoIdentity
                && ReferencesObject(item.Value, infoIdentity))
            || document.CrossReferences.MergedTrailer.Any(entry => !entry.Key.Equals(InfoName)
                && ReferencesObject(entry.Value, infoIdentity)))
            throw new NotSupportedException(
                "The document information object is shared outside trailer /Info and cannot be removed safely.");
        objects.RemoveAll(item => (item.ObjectNumber, item.Generation) == infoIdentity);
    }

    private static PdfObject RemoveCatalogMetadata(
        PdfDocument document, PdfObject root, List<WritableObject> objects,
        PdfMetadataPolicy policy)
    {
        if (policy != PdfMetadataPolicy.RemoveDocumentInformationAndXmp)
            return root;
        PdfDictionary catalog = ResolveValue(document, root,
                "The trailer /Root value") as PdfDictionary
            ?? throw new InvalidOperationException(
                "The trailer /Root value is not a catalog dictionary.");
        if (!catalog.TryGetValue(MetadataName, out PdfObject metadataValue))
            return root;
        var replacement = new PdfDictionary(catalog.Where(entry =>
            !entry.Key.Equals(MetadataName)));
        if (metadataValue is PdfIndirectReference metadataReference)
        {
            var metadataIdentity =
                (metadataReference.ObjectNumber, metadataReference.Generation);
            if (root is PdfIndirectReference catalogReference
                && (catalogReference.ObjectNumber, catalogReference.Generation) == metadataIdentity)
                throw new NotSupportedException(
                    "The catalog metadata reference points to the catalog and cannot be removed safely.");
            if (ReferencesObject(replacement, metadataIdentity)
                || objects.Any(item => (item.ObjectNumber, item.Generation) != metadataIdentity
                    && (root is not PdfIndirectReference catalogReference
                        || (item.ObjectNumber, item.Generation) !=
                            (catalogReference.ObjectNumber, catalogReference.Generation))
                    && ReferencesObject(item.Value, metadataIdentity))
                || document.CrossReferences.MergedTrailer.Any(entry => !entry.Key.Equals(RootName)
                    && ReferencesObject(entry.Value, metadataIdentity)))
                throw new NotSupportedException(
                    "The catalog metadata object is shared and cannot be removed safely.");
            objects.RemoveAll(item => (item.ObjectNumber, item.Generation) == metadataIdentity);
        }
        if (root is not PdfIndirectReference reference)
            return replacement;
        int index = objects.FindIndex(item =>
            (item.ObjectNumber, item.Generation) ==
            (reference.ObjectNumber, reference.Generation));
        if (index < 0)
            throw new InvalidOperationException("The catalog object is unavailable for metadata removal.");
        objects[index] = objects[index] with { Value = replacement };
        return root;
    }

    private static bool ReferencesObject(PdfObject value,
        (int ObjectNumber, int Generation) identity) => value switch
    {
        PdfIndirectReference reference =>
            (reference.ObjectNumber, reference.Generation) == identity,
        PdfArray array => array.Any(item => ReferencesObject(item, identity)),
        PdfDictionary dictionary =>
            dictionary.Any(entry => ReferencesObject(entry.Value, identity)),
        PdfStream stream => ReferencesObject(stream.Dictionary, identity),
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
        PdfObject root = ResolveValue(document, rootValue,
            "The catalog root value");
        if (root is not PdfDictionary catalog
            || !catalog.TryGetValue(VersionName, out PdfObject versionValue))
            return headerVersion;
        PdfObject resolvedVersion = ResolveValue(document, versionValue,
            "The catalog /Version value");
        if (resolvedVersion is not PdfName versionName)
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

    private static PdfObject ResolveValue(
        PdfDocument document, PdfObject value, string description)
    {
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        for (int depth = 0; value is PdfIndirectReference reference; depth++)
        {
            if (depth > 32)
                throw new InvalidOperationException(
                    $"{description} is too deeply indirect.");
            if (!visited.Add((reference.ObjectNumber, reference.Generation)))
                throw new InvalidOperationException(
                    $"{description} contains an indirect-reference cycle.");
            value = document.Resolve(reference);
        }
        return value;
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
        foreach ((PdfName name, PdfObject value) in document.CrossReferences.MergedTrailer)
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

    private static void ValidateApplicationTrailerGraphs(PdfDocument document)
    {
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        foreach ((PdfName name, PdfObject value) in document.CrossReferences.MergedTrailer)
            if (!StructuralTrailerNames.Contains(name))
                Validate(value, $"Trailer /{name.ValueAsLatin1()} value", 0,
                    maximumDepth: 64);

        void Validate(
            PdfObject value, string description, int depth, int maximumDepth)
        {
            if (depth > maximumDepth)
                throw new NotSupportedException(
                    "A preserved trailer graph is too deeply nested.");
            if (value is PdfIndirectReference reference)
            {
                if (!IsLive(reference))
                    throw new InvalidOperationException(
                        $"{description} contains a stale indirect reference.");
                if (!visited.Add((reference.ObjectNumber, reference.Generation))) return;
                Validate(document.Resolve(reference), description, depth + 1,
                    maximumDepth);
                return;
            }
            if (value is PdfArray array)
            {
                foreach (PdfObject item in array)
                    Validate(item, description, depth + 1, maximumDepth);
                return;
            }
            PdfDictionary? dictionary = value switch
            {
                PdfDictionary directDictionary => directDictionary,
                PdfStream stream => stream.Dictionary,
                _ => null
            };
            if (dictionary is null) return;
            foreach (var entry in dictionary)
                Validate(entry.Value, description, depth + 1, maximumDepth);
        }

        bool IsLive(PdfIndirectReference reference)
        {
            if (!document.CrossReferences.TryGetValue(
                    reference.ObjectNumber, out PdfCrossReferenceEntry entry))
                return false;
            return entry.Type switch
            {
                PdfCrossReferenceEntryType.InUse => entry.Field2 == reference.Generation,
                PdfCrossReferenceEntryType.Compressed => reference.Generation == 0,
                _ => false
            };
        }
    }

    private static void ValidateCatalogGraph(
        PdfObject root, IReadOnlyList<WritableObject> objects)
    {
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        Dictionary<(int ObjectNumber, int Generation), PdfObject> currentObjects =
            objects.ToDictionary(
                item => (item.ObjectNumber, item.Generation), item => item.Value);
        Validate(root, 0);

        void Validate(PdfObject value, int depth)
        {
            if (depth > 256)
                throw new NotSupportedException(
                    "The preserved catalog graph is too deeply nested.");
            if (value is PdfIndirectReference reference)
            {
                if (!IsLive(reference))
                    throw new InvalidOperationException(
                        "Trailer /Root value contains a stale indirect reference.");
                if (!visited.Add((reference.ObjectNumber, reference.Generation))) return;
                Validate(currentObjects[(reference.ObjectNumber, reference.Generation)],
                    depth + 1);
                return;
            }
            if (value is PdfArray array)
            {
                foreach (PdfObject item in array)
                    Validate(item, depth + 1);
                return;
            }
            PdfDictionary? dictionary = value switch
            {
                PdfDictionary directDictionary => directDictionary,
                PdfStream stream => stream.Dictionary,
                _ => null
            };
            if (dictionary is null) return;
            foreach (KeyValuePair<PdfName, PdfObject> entry in dictionary)
                Validate(entry.Value, depth + 1);
        }

        bool IsLive(PdfIndirectReference reference)
            => currentObjects.ContainsKey(
                (reference.ObjectNumber, reference.Generation));
    }

    private static void ValidateOutputTrailerGraphs(
        PdfDocument document, IReadOnlyList<WritableObject> objects,
        PdfDocumentWriteOptions options)
    {
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        Dictionary<(int ObjectNumber, int Generation), PdfObject> currentObjects =
            objects.ToDictionary(
                item => (item.ObjectNumber, item.Generation), item => item.Value);
        foreach ((PdfName name, PdfObject value) in document.CrossReferences.MergedTrailer)
            if (!StructuralTrailerNames.Contains(name))
                Validate(value, $"Trailer /{name.ValueAsLatin1()} value", 0);
        if (options.MetadataPolicy == PdfMetadataPolicy.Preserve
            && document.CrossReferences.TryGetTrailerValue(InfoName, out PdfObject info))
            Validate(info, "Trailer /Info value", 0);
        if (document.IsEncrypted
            && document.CrossReferences.TryGetTrailerValue(
                EncryptName, out PdfObject encryption))
            Validate(encryption, "Trailer /Encrypt value", 0);

        void Validate(PdfObject value, string description, int depth)
        {
            if (depth > 64)
                throw new NotSupportedException(
                    "A preserved trailer graph is too deeply nested.");
            if (value is PdfIndirectReference reference)
            {
                var identity = (reference.ObjectNumber, reference.Generation);
                if (!currentObjects.TryGetValue(identity, out PdfObject? resolved))
                    throw new InvalidOperationException(
                        $"{description} contains a reference to an object omitted from the full rewrite.");
                if (!visited.Add(identity)) return;
                Validate(resolved, description, depth + 1);
                return;
            }
            if (value is PdfArray array)
            {
                foreach (PdfObject item in array)
                    Validate(item, description, depth + 1);
                return;
            }
            PdfDictionary? dictionary = value switch
            {
                PdfDictionary directDictionary => directDictionary,
                PdfStream stream => stream.Dictionary,
                _ => null
            };
            if (dictionary is null) return;
            foreach (var entry in dictionary)
                Validate(entry.Value, description, depth + 1);
        }
    }

    private static void ValidateWritableObjectGraphs(
        IReadOnlyList<WritableObject> objects)
    {
        Dictionary<(int ObjectNumber, int Generation), PdfObject> currentObjects =
            objects.ToDictionary(
                item => (item.ObjectNumber, item.Generation), item => item.Value);
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        foreach (WritableObject item in objects)
            Validate(item.Value,
                $"Object {item.ObjectNumber} {item.Generation}", 0);

        void Validate(PdfObject value, string description, int depth)
        {
            if (depth > 256)
                throw new NotSupportedException(
                    "A writable object graph is too deeply nested.");
            if (value is PdfIndirectReference reference)
            {
                var identity = (reference.ObjectNumber, reference.Generation);
                if (!currentObjects.TryGetValue(identity, out PdfObject? resolved))
                    throw new InvalidOperationException(
                        $"{description} contains a reference to an object omitted from the full rewrite.");
                if (!visited.Add(identity)) return;
                Validate(resolved, description, depth + 1);
                return;
            }
            if (value is PdfArray array)
            {
                foreach (PdfObject item in array)
                    Validate(item, description, depth + 1);
                return;
            }
            PdfDictionary? dictionary = value switch
            {
                PdfDictionary directDictionary => directDictionary,
                PdfStream stream => stream.Dictionary,
                _ => null
            };
            if (dictionary is null) return;
            foreach (var entry in dictionary)
                Validate(entry.Value, description, depth + 1);
        }
    }

    private static void ValidateDocumentInformation(
        PdfDictionary information, Func<PdfObject, PdfObject> resolve)
    {
        foreach (string key in new[]
            { "Title", "Author", "Subject", "Keywords", "Creator", "Producer",
              "CreationDate", "ModDate" })
            if (information.TryGetValue(new PdfName(Encoding.ASCII.GetBytes(key)),
                    out PdfObject value)
                && resolve(value) is not PdfString)
                throw new InvalidOperationException(
                    $"Trailer /Info /{key} value is not a string.");
        foreach (string key in new[] { "CreationDate", "ModDate" })
        {
            PdfName name = new(Encoding.ASCII.GetBytes(key));
            if (information.TryGetValue(name, out PdfObject value)
                && resolve(value) is PdfString date && !PdfDateStringValidator.IsValid(date))
                throw new InvalidOperationException(
                    $"Trailer /Info /{key} value is not a valid PDF date string.");
        }
        PdfName trappedName = new("Trapped"u8);
        if (!information.TryGetValue(trappedName, out PdfObject trapped)) return;
        string state = (resolve(trapped) as PdfName)?.ValueAsLatin1()
            ?? throw new InvalidOperationException(
                "Trailer /Info /Trapped value is not a name.");
        if (state is not ("True" or "False" or "Unknown"))
            throw new InvalidOperationException(
                $"Trailer /Info /Trapped value /{state} is not defined.");
    }

    private static void ValidateDocumentInformationGraph(
        PdfDocument document, PdfDictionary information)
    {
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        Validate(information, 0);

        void Validate(PdfObject value, int depth)
        {
            if (depth > 64)
                throw new NotSupportedException(
                    "The preserved document-information graph is too deeply nested.");
            if (value is PdfIndirectReference reference)
            {
                if (!document.CrossReferences.TryGetValue(reference.ObjectNumber,
                        out PdfCrossReferenceEntry entry)
                    || entry.Type == PdfCrossReferenceEntryType.InUse
                        && entry.Field2 != reference.Generation
                    || entry.Type == PdfCrossReferenceEntryType.Compressed
                        && reference.Generation != 0
                    || entry.Type is not (PdfCrossReferenceEntryType.InUse
                        or PdfCrossReferenceEntryType.Compressed))
                    throw new InvalidOperationException(
                        "Trailer /Info value contains a stale indirect reference.");
                if (!visited.Add((reference.ObjectNumber, reference.Generation))) return;
                Validate(document.Resolve(reference), depth + 1);
                return;
            }
            if (value is PdfArray array)
            {
                foreach (PdfObject item in array) Validate(item, depth + 1);
                return;
            }
            PdfDictionary? dictionary = value switch
            {
                PdfDictionary directDictionary => directDictionary,
                PdfStream stream => stream.Dictionary,
                _ => null
            };
            if (dictionary is null) return;
            foreach (var entry in dictionary) Validate(entry.Value, depth + 1);
        }
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
