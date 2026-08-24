using System.Collections;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;

namespace KillerPdf.Engine.CrossReference;

/// <summary>
/// The merged cross-reference view of every incremental revision, with the newest definition of
/// an object taking precedence over older definitions.
/// </summary>
public sealed class PdfCrossReferenceTable : IReadOnlyDictionary<int, PdfCrossReferenceEntry>
{
    public const int MaximumRevisionCount = 1_024;
    private static readonly PdfName SizeName = new("Size"u8);
    private static readonly PdfName IdName = new("ID"u8);
    private static readonly PdfName EncryptName = new("Encrypt"u8);

    private readonly Dictionary<int, PdfCrossReferenceEntry> _entries;
    private readonly IReadOnlyList<Revision> _revisions;

    private PdfCrossReferenceTable(
        PdfHeader header,
        PdfStartXref startXref,
        List<Revision> revisions,
        Dictionary<int, PdfCrossReferenceEntry> entries)
    {
        Header = header;
        StartXref = startXref;
        _revisions = revisions;
        _entries = entries;
    }

    public PdfHeader Header { get; }
    public PdfStartXref StartXref { get; }
    public IReadOnlyList<PdfCrossReferenceSection> Sections =>
        _revisions.Select(revision => revision.Primary).ToArray();
    internal IEnumerable<PdfCrossReferenceSection> AllSections =>
        _revisions.SelectMany(revision => revision.Hybrid is null
            ? [revision.Primary] : new[] { revision.Primary, revision.Hybrid });

    internal HashSet<(int ObjectNumber, int Index)> RegisteredHeadersForCurrentObjectStream(
        int streamNumber)
    {
        if (!_entries.TryGetValue(streamNumber, out PdfCrossReferenceEntry current)
            || current.Type != PdfCrossReferenceEntryType.InUse)
            return [];
        var result = new HashSet<(int ObjectNumber, int Index)>();
        bool currentVersionActive = false;
        for (int index = _revisions.Count - 1; index >= 0; index--)
        {
            Revision revision = _revisions[index];
            PdfCrossReferenceEntry? streamEntry = null;
            if (revision.Primary.TryGetValue(streamNumber, out PdfCrossReferenceEntry primary))
                streamEntry = primary;
            if (revision.Hybrid is not null
                && revision.Hybrid.TryGetValue(streamNumber, out PdfCrossReferenceEntry hybrid))
                streamEntry = hybrid;
            if (streamEntry.HasValue)
                currentVersionActive = streamEntry.Value.Type == PdfCrossReferenceEntryType.InUse
                    && streamEntry.Value.Field1 == current.Field1
                    && streamEntry.Value.Field2 == current.Field2;
            if (!currentVersionActive)
                continue;
            AddRegistrations(revision.Primary);
            if (revision.Hybrid is not null)
                AddRegistrations(revision.Hybrid);
        }
        return result;

        void AddRegistrations(PdfCrossReferenceSection section)
        {
            foreach (PdfCrossReferenceEntry candidate in section.Values)
                if (candidate.Type == PdfCrossReferenceEntryType.Compressed
                    && candidate.Field1 == streamNumber)
                    result.Add((candidate.ObjectNumber, candidate.Field2));
        }
    }

    public PdfDictionary LatestTrailer => _revisions[0].Primary.Trailer;
    /// <summary>
    /// Returns the effective trailer dictionary across the revision chain, choosing the newest
    /// occurrence of each key while retaining extension-defined entries from older revisions.
    /// </summary>
    public PdfDictionary MergedTrailer
    {
        get
        {
            var entries = new Dictionary<PdfName, PdfObject>();
            foreach (Revision revision in _revisions)
            {
                foreach (var entry in revision.Primary.Trailer)
                    entries.TryAdd(entry.Key, entry.Value);
                if (revision.Hybrid is not null)
                    foreach (var entry in revision.Hybrid.Trailer)
                        entries.TryAdd(entry.Key, entry.Value);
            }
            return new PdfDictionary(entries);
        }
    }

    public int Count => _entries.Count;
    public IEnumerable<int> Keys => _entries.Keys;
    public IEnumerable<PdfCrossReferenceEntry> Values => _entries.Values;
    public PdfCrossReferenceEntry this[int key] => _entries[key];

    public static PdfCrossReferenceTable Read(ReadOnlyMemory<byte> source)
    {
        PdfHeader header = PdfHeader.Parse(source.Span);
        PdfStartXref startXref = PdfStartXref.Find(source.Span);
        var revisions = new List<Revision>();
        var visitedOffsets = new HashSet<long>();
        long? currentOffset = startXref.Offset;

        while (currentOffset.HasValue)
        {
            if (revisions.Count >= MaximumRevisionCount)
                throw new PdfSyntaxException("The PDF contains too many incremental revisions", (int)currentOffset.Value);
            if (!visitedOffsets.Add(currentOffset.Value))
                throw new PdfSyntaxException("The cross-reference revision chain contains a cycle", (int)currentOffset.Value);

            PdfCrossReferenceSection primary = PdfCrossReferenceReader.ReadSection(source, currentOffset.Value);
            if (primary.PreviousOffset > currentOffset.Value)
                throw new PdfSyntaxException(
                    "Trailer /Prev must point to an earlier cross-reference section",
                    ClampOffset(primary.PreviousOffset.Value));
            PdfCrossReferenceSection? hybrid = null;
            if (primary.HybridStreamOffset.HasValue)
            {
                long hybridOffset = primary.HybridStreamOffset.Value;
                if (!visitedOffsets.Add(hybridOffset))
                    throw new PdfSyntaxException("The hybrid cross-reference chain reuses an offset", (int)hybridOffset);
                if (hybridOffset > currentOffset.Value)
                    throw new PdfSyntaxException(
                        "Trailer /XRefStm must point to an earlier cross-reference stream",
                        ClampOffset(hybridOffset));
                hybrid = PdfCrossReferenceReader.ReadSection(source, hybridOffset);
                if (!hybrid.IsStream)
                    throw new PdfSyntaxException("Trailer /XRefStm must point to a cross-reference stream", (int)hybridOffset);
                if (hybrid.PreviousOffset.HasValue)
                    throw new PdfSyntaxException(
                        "A hybrid cross-reference stream cannot contain /Prev",
                        (int)hybridOffset);
            }

            revisions.Add(new Revision(primary, hybrid));
            currentOffset = primary.PreviousOffset;
        }

        ValidateRevisionSizes(revisions, startXref.Offset);
        ValidateRevisionGenerations(revisions, startXref.Offset);
        ValidateStructuralStreamEntries(revisions, startXref.Offset);
        ValidatePermanentIdentifiers(revisions, startXref.Offset);
        ValidateEncryptionIntroduction(revisions, startXref.Offset);

        var entries = new Dictionary<int, PdfCrossReferenceEntry>();
        foreach (Revision revision in revisions)
        {
            // In a hybrid revision, stream entries supply compressed-object information absent
            // from the classic table and take precedence if a producer emitted both.
            if (revision.Hybrid is not null)
                AddNewest(entries, revision.Hybrid.Values);
            AddNewest(entries, revision.Primary.Values);
        }
        ValidateFreeList(entries, startXref.Offset);

        return new PdfCrossReferenceTable(header, startXref, revisions, entries);
    }

    private static void ValidateRevisionSizes(IReadOnlyList<Revision> revisions, long offset)
    {
        long previousSize = 0;
        for (int index = revisions.Count - 1; index >= 0; index--)
        {
            Revision revision = revisions[index];
            long size = ((PdfInteger)revision.Primary.Trailer[SizeName]).Value;
            if (size < previousSize)
                throw new PdfSyntaxException(
                    "Trailer /Size cannot decrease across incremental revisions",
                    ClampOffset(offset));
            if (revision.Hybrid is not null)
            {
                long hybridSize = ((PdfInteger)
                    revision.Hybrid.Trailer[SizeName]).Value;
                if (hybridSize != size)
                    throw new PdfSyntaxException(
                        "A hybrid cross-reference stream /Size must match its trailer /Size",
                        ClampOffset(revision.Hybrid.Offset));
            }
            previousSize = size;
        }
    }

    private static void ValidateRevisionGenerations(
        IReadOnlyList<Revision> revisions, long offset)
    {
        var states = new Dictionary<int, (bool IsFree, int Generation)>();
        for (int index = revisions.Count - 1; index >= 0; index--)
        {
            Revision revision = revisions[index];
            var entries = revision.Primary.ToDictionary(entry => entry.Key, entry => entry.Value);
            if (revision.Hybrid is not null)
                foreach ((int objectNumber, PdfCrossReferenceEntry entry) in revision.Hybrid)
                    entries[objectNumber] = entry;

            foreach (PdfCrossReferenceEntry entry in entries.Values)
            {
                int? generation = entry.Type switch
                {
                    PdfCrossReferenceEntryType.InUse or PdfCrossReferenceEntryType.Free => entry.Field2,
                    PdfCrossReferenceEntryType.Compressed => 0,
                    _ => null
                };
                if (!generation.HasValue)
                {
                    states.Remove(entry.ObjectNumber);
                    continue;
                }
                bool isFree = entry.Type == PdfCrossReferenceEntryType.Free;
                if (states.TryGetValue(entry.ObjectNumber, out var previous))
                {
                    int requiredGeneration = !previous.IsFree && isFree
                        ? Math.Min(previous.Generation + 1, 65_535)
                        : previous.Generation;
                    if (generation.Value != requiredGeneration)
                    {
                        string reason = generation.Value < previous.Generation
                            ? "generation cannot decrease"
                            : "has an invalid generation transition";
                        throw new PdfSyntaxException(
                            $"Cross-reference object {entry.ObjectNumber} {reason} across incremental revisions",
                            ClampOffset(offset));
                    }
                }
                states[entry.ObjectNumber] = (isFree, generation.Value);
            }
        }
    }

    private static void ValidateStructuralStreamEntries(
        IReadOnlyList<Revision> revisions, long offset)
    {
        foreach (Revision revision in revisions)
        {
            if (revision.Primary.IsStream
                && !HasSelfEntry(revision.Primary, revision.Primary))
                throw new PdfSyntaxException(
                    "A cross-reference stream must contain an in-use entry for itself",
                    ClampOffset(offset));
            if (revision.Hybrid is not null
                && !HasSelfEntry(revision.Primary, revision.Hybrid)
                && !HasSelfEntry(revision.Hybrid, revision.Hybrid))
                throw new PdfSyntaxException(
                    "A hybrid cross-reference stream must have an in-use entry in its revision",
                    ClampOffset(revision.Hybrid.Offset));
        }

        static bool HasSelfEntry(
            PdfCrossReferenceSection entries, PdfCrossReferenceSection stream)
        {
            return stream.StreamObjectNumber.HasValue
                && entries.TryGetValue(stream.StreamObjectNumber.Value,
                    out PdfCrossReferenceEntry entry)
                && entry.Type == PdfCrossReferenceEntryType.InUse
                && entry.Field1 == stream.Offset
                && entry.Field2 == 0;
        }
    }

    private static void ValidatePermanentIdentifiers(
        IReadOnlyList<Revision> revisions, long offset)
    {
        ReadOnlyMemory<byte>? permanentIdentifier = null;
        for (int index = revisions.Count - 1; index >= 0; index--)
        {
            Revision revision = revisions[index];
            PdfObject? value = revision.Primary.Trailer.TryGetValue(IdName, out PdfObject primary)
                ? primary
                : revision.Hybrid is not null
                    && revision.Hybrid.Trailer.TryGetValue(IdName, out PdfObject hybrid)
                        ? hybrid : null;
            if (value is not PdfArray { Count: 2 } identifiers
                || identifiers[0] is not PdfString first
                || identifiers[1] is not PdfString)
                continue;
            if (permanentIdentifier.HasValue
                && !permanentIdentifier.Value.Span.SequenceEqual(first.Bytes.Span))
                throw new PdfSyntaxException(
                    "The first trailer /ID value cannot change across incremental revisions",
                    ClampOffset(offset));
            permanentIdentifier = first.Bytes;
        }
    }

    private static void ValidateEncryptionIntroduction(
        IReadOnlyList<Revision> revisions, long offset)
    {
        bool oldestRevision = true;
        bool encryptionWasInitiallyPresent = false;
        for (int index = revisions.Count - 1; index >= 0; index--)
        {
            Revision revision = revisions[index];
            bool present = revision.Primary.Trailer.TryGetValue(
                EncryptName, out _);
            if (!present && revision.Hybrid is not null)
                present = revision.Hybrid.Trailer.TryGetValue(EncryptName, out _);
            if (oldestRevision)
            {
                encryptionWasInitiallyPresent = present;
                oldestRevision = false;
            }
            else if (present && !encryptionWasInitiallyPresent)
                throw new PdfSyntaxException(
                    "Trailer /Encrypt cannot be introduced by an incremental revision",
                    ClampOffset(offset));
        }
    }

    private static void ValidateFreeList(
        IReadOnlyDictionary<int, PdfCrossReferenceEntry> entries, long offset)
    {
        if (!entries.TryGetValue(0, out PdfCrossReferenceEntry zero)
            || zero.Type != PdfCrossReferenceEntryType.Free
            || zero.Field2 != 65_535)
            throw new PdfSyntaxException(
                "The merged cross-reference table must define object 0 as free with generation 65,535",
                ClampOffset(offset));
        int next = checked((int)zero.Field1);
        var visited = new HashSet<int> { 0 };
        while (next != 0)
        {
            if (!visited.Add(next))
                throw new PdfSyntaxException(
                    "The cross-reference free-list chain contains a cycle",
                    ClampOffset(offset));
            if (!entries.TryGetValue(next, out PdfCrossReferenceEntry entry)
                || entry.Type != PdfCrossReferenceEntryType.Free)
                throw new PdfSyntaxException(
                    $"The cross-reference free-list points to active or missing object {next}",
                    ClampOffset(offset));
            next = checked((int)entry.Field1);
        }
    }

    public bool TryGetTrailerValue(PdfName name, out PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (Revision revision in _revisions)
        {
            if (revision.Primary.Trailer.TryGetValue(name, out value!))
                return true;
            if (revision.Hybrid is not null && revision.Hybrid.Trailer.TryGetValue(name, out value!))
                return true;
        }

        value = null!;
        return false;
    }

    public bool ContainsKey(int key) => _entries.ContainsKey(key);
    public bool TryGetValue(int key, out PdfCrossReferenceEntry value) => _entries.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<int, PdfCrossReferenceEntry>> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static void AddNewest(
        Dictionary<int, PdfCrossReferenceEntry> destination,
        IEnumerable<PdfCrossReferenceEntry> source)
    {
        foreach (PdfCrossReferenceEntry entry in source)
            destination.TryAdd(entry.ObjectNumber, entry);
    }

    private sealed record Revision(
        PdfCrossReferenceSection Primary,
        PdfCrossReferenceSection? Hybrid);

    private static int ClampOffset(long offset) => offset switch
    {
        < 0 => 0,
        > int.MaxValue => int.MaxValue,
        _ => (int)offset
    };
}
