using System.Globalization;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Filters;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Parsing;
using KillerPdf.Engine.Syntax;
using KillerPdf.Engine.Security;

namespace KillerPdf.Engine.Documents;

/// <summary>
/// A parsed PDF file whose indirect objects are loaded lazily through its merged cross-reference
/// table. Compressed objects are decoded and cached by object stream.
/// </summary>
public sealed class PdfDocument
{
    private static readonly PdfName TypeName = new("Type"u8);
    private static readonly PdfName ObjectStreamTypeName = new("ObjStm"u8);
    private static readonly PdfName ObjectCountName = new("N"u8);
    private static readonly PdfName FirstObjectOffsetName = new("First"u8);

    private readonly ReadOnlyMemory<byte> _source;
    private readonly Dictionary<int, PdfObject> _objects = [];
    private readonly Dictionary<int, ObjectStreamContents> _objectStreams = [];
    private readonly HashSet<int> _resolving = [];
    private PdfStandardSecurityHandler? _security;
    private int? _encryptionObjectNumber;

    private PdfDocument(ReadOnlyMemory<byte> source, PdfCrossReferenceTable crossReferences)
    {
        _source = source;
        CrossReferences = crossReferences;
    }

    public PdfCrossReferenceTable CrossReferences { get; }
    public PdfHeader Header => CrossReferences.Header;
    public PdfDictionary Trailer => CrossReferences.LatestTrailer;
    internal ReadOnlyMemory<byte> Source => _source;
    public bool IsEncrypted => CrossReferences.TryGetTrailerValue(new PdfName("Encrypt"u8), out _);
    public bool IsDecrypted => !IsEncrypted || _security is not null;
    internal int? EncryptionObjectNumber => _encryptionObjectNumber;
    internal PdfObject EncryptObject(int objectNumber, PdfObject value) =>
        _security is not null && objectNumber != _encryptionObjectNumber
            ? _security.Encrypt(value, objectNumber,
                CrossReferences.TryGetValue(objectNumber, out PdfCrossReferenceEntry entry)
                    && entry.Type == PdfCrossReferenceEntryType.InUse ? entry.Field2 : 0)
            : value;

    public static PdfDocument Open(ReadOnlyMemory<byte> source)
    {
        // Own the bytes so lazy resolution cannot observe caller mutations after validation.
        byte[] ownedSource = source.ToArray();
        return new PdfDocument(ownedSource, PdfCrossReferenceTable.Read(ownedSource));
    }

    /// <summary>Opens and authenticates a password-encrypted PDF.</summary>
    public static PdfDocument Open(ReadOnlyMemory<byte> source, string password)
    {
        PdfDocument document = Open(source);
        if (!document.CrossReferences.TryGetTrailerValue(
                new PdfName("Encrypt"u8), out PdfObject? encryptionValue))
            return document;
        PdfIndirectReference encryptionReference = encryptionValue as PdfIndirectReference
            ?? throw new InvalidOperationException("The trailer /Encrypt value is not indirect.");
        PdfDictionary encryption = document.Resolve(encryptionReference) as PdfDictionary
            ?? throw new InvalidOperationException("The trailer /Encrypt value is not a dictionary.");
        document._encryptionObjectNumber = encryptionReference.ObjectNumber;
        ReadOnlyMemory<byte> permanentIdentifier = ReadOnlyMemory<byte>.Empty;
        if (document.CrossReferences.TryGetTrailerValue(new PdfName("ID"u8), out PdfObject? idValue)
            && idValue is PdfArray { Count: > 0 } identifiers
            && identifiers[0] is PdfString identifier)
            permanentIdentifier = identifier.Bytes;
        document._security = PdfStandardSecurityHandler.Create(
            encryption, password, permanentIdentifier);
        document._objects.Clear();
        document._objectStreams.Clear();
        return document;
    }

    /// <summary>
    /// Resolves the current cross-reference entry for an object number. Free, absent, and
    /// future xref entry types resolve to the PDF null object.
    /// </summary>
    public PdfObject Resolve(int objectNumber)
    {
        if (objectNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(objectNumber));
        if (!CrossReferences.TryGetValue(objectNumber, out PdfCrossReferenceEntry entry))
            return PdfNull.Instance;
        return ResolveEntry(entry);
    }

    /// <summary>
    /// Resolves an indirect reference. A stale generation number resolves to null as required
    /// for a reference that no longer identifies the current in-use object.
    /// </summary>
    public PdfObject Resolve(PdfIndirectReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!CrossReferences.TryGetValue(reference.ObjectNumber, out PdfCrossReferenceEntry entry))
            return PdfNull.Instance;

        int currentGeneration = entry.Type == PdfCrossReferenceEntryType.InUse ? entry.Field2 : 0;
        if (entry.Type is PdfCrossReferenceEntryType.InUse or PdfCrossReferenceEntryType.Compressed
            && reference.Generation != currentGeneration)
            return PdfNull.Instance;
        return ResolveEntry(entry);
    }

    private PdfObject ResolveEntry(PdfCrossReferenceEntry entry)
    {
        if (entry.Type is PdfCrossReferenceEntryType.Free or PdfCrossReferenceEntryType.Null)
            return PdfNull.Instance;
        if (_objects.TryGetValue(entry.ObjectNumber, out PdfObject? cached))
            return cached;
        if (!_resolving.Add(entry.ObjectNumber))
            throw Error($"Resolving object {entry.ObjectNumber} forms a cycle", EntryOffset(entry));

        try
        {
            PdfObject value = entry.Type switch
            {
                PdfCrossReferenceEntryType.InUse => ReadIndirectObject(entry),
                PdfCrossReferenceEntryType.Compressed => ReadCompressedObject(entry),
                _ => PdfNull.Instance
            };
            _objects.Add(entry.ObjectNumber, value);
            return value;
        }
        finally
        {
            _resolving.Remove(entry.ObjectNumber);
        }
    }

    private PdfObject ReadIndirectObject(PdfCrossReferenceEntry entry)
    {
        var parser = new PdfObjectParser(_source, checked((int)entry.Field1), ResolveStreamLength);
        PdfIndirectObject indirect = parser.ParseIndirectObject();
        if (indirect.ObjectNumber != entry.ObjectNumber || indirect.Generation != entry.Field2)
        {
            throw Error(
                $"Cross-reference entry {entry.ObjectNumber} {entry.Field2} points to " +
                $"object {indirect.ObjectNumber} {indirect.Generation}",
                checked((int)entry.Field1));
        }
        return _security is not null && entry.ObjectNumber != _encryptionObjectNumber
            ? _security.Decrypt(indirect.Value, entry.ObjectNumber, entry.Field2)
            : indirect.Value;
    }

    private long ResolveStreamLength(PdfIndirectReference reference)
    {
        PdfObject value = Resolve(reference);
        if (value is not PdfInteger integer)
            throw Error($"Stream Length reference {reference.ObjectNumber} {reference.Generation} is not an integer", 0);
        return integer.Value;
    }

    private PdfObject ReadCompressedObject(PdfCrossReferenceEntry entry)
    {
        int streamNumber = checked((int)entry.Field1);
        ObjectStreamContents contents = ReadObjectStream(streamNumber);
        if (entry.Field2 < 0 || entry.Field2 >= contents.OrderedObjects.Count)
            throw Error($"Compressed object {entry.ObjectNumber} has an invalid object-stream index", streamNumber);

        ObjectStreamItem item = contents.OrderedObjects[entry.Field2];
        if (item.ObjectNumber != entry.ObjectNumber)
        {
            throw Error(
                $"Object-stream index {entry.Field2} names object {item.ObjectNumber}, not {entry.ObjectNumber}",
                streamNumber);
        }
        return item.Value;
    }

    private ObjectStreamContents ReadObjectStream(int streamNumber)
    {
        if (_objectStreams.TryGetValue(streamNumber, out ObjectStreamContents? cached))
            return cached;
        if (!CrossReferences.TryGetValue(streamNumber, out PdfCrossReferenceEntry entry)
            || entry.Type != PdfCrossReferenceEntryType.InUse)
            throw Error($"Object stream {streamNumber} is not an uncompressed in-use object", streamNumber);

        PdfObject resolved = ResolveEntry(entry);
        if (resolved is not PdfStream stream)
            throw Error($"Object stream {streamNumber} does not contain a stream", EntryOffset(entry));
        RequireObjectStreamDictionary(stream.Dictionary, EntryOffset(entry));

        int objectCount = RequiredNonNegativeInt(stream.Dictionary, ObjectCountName, EntryOffset(entry));
        int firstObjectOffset = RequiredNonNegativeInt(stream.Dictionary, FirstObjectOffsetName, EntryOffset(entry));
        byte[] decoded = PdfStreamDecoder.Decode(stream);
        if (firstObjectOffset > decoded.Length)
            throw Error("Object stream /First points beyond the decoded stream", EntryOffset(entry));

        List<ObjectHeader> headers = ReadObjectHeaders(decoded, objectCount, firstObjectOffset, EntryOffset(entry));
        var items = new List<ObjectStreamItem>(objectCount);
        var objectNumbers = new HashSet<int>();
        for (int index = 0; index < headers.Count; index++)
        {
            ObjectHeader header = headers[index];
            if (!objectNumbers.Add(header.ObjectNumber))
                throw Error($"Object stream contains object {header.ObjectNumber} more than once", EntryOffset(entry));

            int start = checked(firstObjectOffset + header.RelativeOffset);
            int end = index + 1 < headers.Count
                ? checked(firstObjectOffset + headers[index + 1].RelativeOffset)
                : decoded.Length;
            if (start >= end || end > decoded.Length)
                throw Error("Object stream offsets do not define non-empty objects in ascending order", EntryOffset(entry));

            PdfObject value = new PdfObjectParser(decoded.AsMemory(start, end - start)).ParseSingleObject();
            items.Add(new ObjectStreamItem(header.ObjectNumber, value));
        }

        var contents = new ObjectStreamContents(items);
        _objectStreams.Add(streamNumber, contents);
        return contents;
    }

    private static List<ObjectHeader> ReadObjectHeaders(
        byte[] decoded,
        int objectCount,
        int firstObjectOffset,
        int sourceOffset)
    {
        var tokenizer = new PdfTokenizer(decoded.AsMemory(0, firstObjectOffset));
        var headers = new List<ObjectHeader>(objectCount);
        int previousOffset = -1;
        for (int index = 0; index < objectCount; index++)
        {
            int objectNumber = RequiredHeaderInteger(tokenizer.Read(), "object number", sourceOffset);
            int relativeOffset = RequiredHeaderInteger(tokenizer.Read(), "object offset", sourceOffset);
            if (objectNumber == 0)
                throw Error("Object stream object numbers must be greater than zero", sourceOffset);
            if (relativeOffset <= previousOffset || relativeOffset >= decoded.Length - firstObjectOffset)
                throw Error("Object stream offsets must be ascending and point inside the object data", sourceOffset);
            previousOffset = relativeOffset;
            headers.Add(new ObjectHeader(objectNumber, relativeOffset));
        }

        PdfToken trailing = tokenizer.Read();
        if (trailing.Kind != PdfTokenKind.EndOfInput)
            throw Error("Object stream header contains more entries than /N declares", sourceOffset);
        return headers;
    }

    private static void RequireObjectStreamDictionary(PdfDictionary dictionary, int offset)
    {
        if (!dictionary.TryGetValue(TypeName, out PdfObject type)
            || type is not PdfName name
            || !name.Equals(ObjectStreamTypeName))
            throw Error("A compressed object must be stored in a /Type /ObjStm stream", offset);
    }

    private static int RequiredNonNegativeInt(PdfDictionary dictionary, PdfName name, int offset)
    {
        if (!dictionary.TryGetValue(name, out PdfObject value)
            || value is not PdfInteger integer
            || integer.Value is < 0 or > int.MaxValue)
            throw Error($"Object stream {name} must be a non-negative 32-bit integer", offset);
        return (int)integer.Value;
    }

    private static int RequiredHeaderInteger(PdfToken token, string description, int sourceOffset)
    {
        if (token.Kind != PdfTokenKind.Integer
            || !int.TryParse(token.Value.Span, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            || value < 0)
            throw Error($"Object stream header {description} must be a non-negative integer", sourceOffset);
        return value;
    }

    private static int EntryOffset(PdfCrossReferenceEntry entry) =>
        entry.Type == PdfCrossReferenceEntryType.InUse ? checked((int)entry.Field1) : 0;

    private static PdfSyntaxException Error(string message, int offset) => new(message, Math.Max(offset, 0));

    private readonly record struct ObjectHeader(int ObjectNumber, int RelativeOffset);
    private sealed record ObjectStreamItem(int ObjectNumber, PdfObject Value);
    private sealed record ObjectStreamContents(IReadOnlyList<ObjectStreamItem> OrderedObjects);
}
