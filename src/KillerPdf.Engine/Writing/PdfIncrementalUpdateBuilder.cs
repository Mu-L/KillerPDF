using System.Globalization;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using System.Security.Cryptography;

namespace KillerPdf.Engine.Writing;

/// <summary>
/// Appends a deterministic incremental revision while preserving every byte of the source PDF.
/// Existing objects may be superseded and new indirect objects may be reserved before their
/// values are known, allowing callers to construct mutually referring object graphs.
/// </summary>
public sealed class PdfIncrementalUpdateBuilder
{
    private static readonly PdfName SizeName = new("Size"u8);
    private static readonly PdfName PrevName = new("Prev"u8);
    private static readonly PdfName XRefStmName = new("XRefStm"u8);
    private static readonly PdfName RootName = new("Root"u8);
    private static readonly PdfName InfoName = new("Info"u8);
    private static readonly PdfName IdName = new("ID"u8);
    private static readonly PdfName EncryptName = new("Encrypt"u8);
    private static readonly HashSet<PdfName> XrefStreamOnlyNames =
    [
        new("Type"u8), new("W"u8), new("Index"u8), new("Length"u8),
        new("Filter"u8), new("DecodeParms"u8), new("DL"u8)
    ];

    private readonly PdfDocument _document;
    private readonly SortedDictionary<int, PendingObject> _objects = [];
    private readonly HashSet<int> _reserved = [];
    private int _nextObjectNumber;

    public PdfIncrementalUpdateBuilder(PdfDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        if (document.CrossReferences.TryGetTrailerValue(EncryptName, out _))
            throw new NotSupportedException(
                "Encrypted PDF incremental updates require the encryption writer milestone.");
        _nextObjectNumber = InitialSize(document);
    }

    /// <summary>Reserves a generation-zero object number for a value supplied later.</summary>
    public PdfIndirectReference ReserveObject()
    {
        if (_nextObjectNumber == int.MaxValue)
            throw new NotSupportedException("The PDF object-number range is exhausted.");
        int objectNumber = _nextObjectNumber++;
        _reserved.Add(objectNumber);
        return new PdfIndirectReference(objectNumber, 0);
    }

    /// <summary>Adds a new indirect object and returns its reference.</summary>
    public PdfIndirectReference AddObject(PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        PdfIndirectReference reference = ReserveObject();
        SetObject(reference, value);
        return reference;
    }

    /// <summary>Assigns the value of an object previously returned by <see cref="ReserveObject"/>.</summary>
    public PdfIncrementalUpdateBuilder SetObject(PdfIndirectReference reference, PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(value);
        if (reference.Generation != 0 || !_reserved.Contains(reference.ObjectNumber))
            throw new ArgumentException("The reference was not reserved by this update builder.", nameof(reference));
        if (!_objects.TryAdd(reference.ObjectNumber, new PendingObject(reference.ObjectNumber, 0, value)))
            throw new InvalidOperationException($"Object {reference.ObjectNumber} already has a value.");
        return this;
    }

    /// <summary>Supersedes the current value of an existing in-use or compressed object.</summary>
    public PdfIncrementalUpdateBuilder ReplaceObject(int objectNumber, PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (objectNumber <= 0) throw new ArgumentOutOfRangeException(nameof(objectNumber));
        if (!_document.CrossReferences.TryGetValue(objectNumber, out PdfCrossReferenceEntry entry)
            || entry.Type is not (PdfCrossReferenceEntryType.InUse or PdfCrossReferenceEntryType.Compressed))
            throw new ArgumentException($"Object {objectNumber} is not currently in use.", nameof(objectNumber));
        int generation = entry.Type == PdfCrossReferenceEntryType.InUse ? entry.Field2 : 0;
        if (!_objects.TryAdd(objectNumber, new PendingObject(objectNumber, generation, value)))
            throw new InvalidOperationException($"Object {objectNumber} is already part of this update.");
        return this;
    }

    public byte[] Build()
    {
        int[] unassigned = _reserved.Where(number => !_objects.ContainsKey(number)).Order().ToArray();
        if (unassigned.Length > 0)
            throw new InvalidOperationException(
                $"Reserved object {unassigned[0]} has not been assigned a value.");
        if (_objects.Count == 0)
            throw new InvalidOperationException("An incremental update must contain at least one object.");

        using var output = new MemoryStream();
        output.Write(_document.Source.Span);
        ReadOnlySpan<byte> source = _document.Source.Span;
        if (source.Length > 0 && source[^1] is not ((byte)'\r') and not ((byte)'\n'))
            output.WriteByte((byte)'\n');

        var written = new List<WrittenObject>(_objects.Count);
        foreach (PendingObject pending in _objects.Values)
        {
            if (output.Position > 9_999_999_999L)
                throw new NotSupportedException("Classic cross-reference offsets cannot exceed ten digits.");
            int offset = checked((int)output.Position);
            written.Add(new WrittenObject(pending.ObjectNumber, pending.Generation, offset));
            PdfObjectWriter.Write(output,
                new PdfIndirectObject(pending.ObjectNumber, pending.Generation, pending.Value, offset));
        }

        byte[] revisionIdentifier = SHA256.HashData(
            output.GetBuffer().AsSpan(0, checked((int)output.Length)))[..16];

        int xrefOffset = checked((int)output.Position);
        output.Write("xref\n"u8);
        foreach (WrittenObject item in written)
        {
            WriteAscii(output, $"{item.ObjectNumber} 1\n");
            WriteAscii(output, $"{item.Offset:0000000000} {item.Generation:00000} n \n");
        }

        output.Write("trailer\n"u8);
        PdfObjectWriter.Write(output, BuildTrailer(revisionIdentifier));
        output.Write("\nstartxref\n"u8);
        WriteAscii(output, xrefOffset.ToString(CultureInfo.InvariantCulture));
        output.Write("\n%%EOF\n"u8);
        return output.ToArray();
    }

    private PdfDictionary BuildTrailer(byte[] revisionIdentifier)
    {
        var entries = new List<KeyValuePair<PdfName, PdfObject>>();
        foreach ((PdfName name, PdfObject value) in _document.Trailer)
        {
            if (name.Equals(SizeName) || name.Equals(PrevName) || name.Equals(XRefStmName)
                || name.Equals(IdName))
                continue;
            if (_document.CrossReferences.Sections[0].IsStream && XrefStreamOnlyNames.Contains(name))
                continue;
            entries.Add(new KeyValuePair<PdfName, PdfObject>(name, value));
        }
        AddInheritedIfMissing(entries, RootName);
        AddInheritedIfMissing(entries, InfoName);
        AddUpdatedIdentifier(entries, revisionIdentifier);
        entries.Add(new KeyValuePair<PdfName, PdfObject>(SizeName, new PdfInteger(_nextObjectNumber)));
        entries.Add(new KeyValuePair<PdfName, PdfObject>(
            PrevName, new PdfInteger(_document.CrossReferences.StartXref.Offset)));
        return new PdfDictionary(entries);
    }

    private void AddUpdatedIdentifier(
        ICollection<KeyValuePair<PdfName, PdfObject>> entries, byte[] revisionIdentifier)
    {
        if (!_document.CrossReferences.TryGetTrailerValue(IdName, out PdfObject value)
            || value is not PdfArray identifiers
            || identifiers.Count == 0
            || identifiers[0] is not PdfString permanentIdentifier)
            return;
        entries.Add(new KeyValuePair<PdfName, PdfObject>(IdName, new PdfArray([
            permanentIdentifier,
            new PdfString(revisionIdentifier, PdfStringForm.Hexadecimal)])));
    }

    private void AddInheritedIfMissing(
        ICollection<KeyValuePair<PdfName, PdfObject>> entries, PdfName name)
    {
        if (entries.Any(entry => entry.Key.Equals(name)))
            return;
        if (_document.CrossReferences.TryGetTrailerValue(name, out PdfObject value))
            entries.Add(new KeyValuePair<PdfName, PdfObject>(name, value));
    }

    private static int InitialSize(PdfDocument document)
    {
        long declaredSize = 0;
        if (document.CrossReferences.TryGetTrailerValue(SizeName, out PdfObject sizeValue)
            && sizeValue is PdfInteger integer)
            declaredSize = integer.Value;
        int highestEntry = document.CrossReferences.Keys.DefaultIfEmpty(0).Max();
        long result = Math.Max(declaredSize, (long)highestEntry + 1);
        if (result is <= 0 or > int.MaxValue)
            throw new NotSupportedException("The PDF trailer /Size is outside the supported object-number range.");
        return (int)result;
    }

    private static void WriteAscii(Stream output, string value)
    {
        foreach (char character in value)
            output.WriteByte(checked((byte)character));
    }

    private sealed record PendingObject(int ObjectNumber, int Generation, PdfObject Value);
    private sealed record WrittenObject(int ObjectNumber, int Generation, int Offset);
}
