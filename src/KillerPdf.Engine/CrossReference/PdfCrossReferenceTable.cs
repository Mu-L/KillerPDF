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

    public PdfDictionary LatestTrailer => _revisions[0].Primary.Trailer;

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
            PdfCrossReferenceSection? hybrid = null;
            if (primary.HybridStreamOffset.HasValue)
            {
                long hybridOffset = primary.HybridStreamOffset.Value;
                if (!visitedOffsets.Add(hybridOffset))
                    throw new PdfSyntaxException("The hybrid cross-reference chain reuses an offset", (int)hybridOffset);
                hybrid = PdfCrossReferenceReader.ReadSection(source, hybridOffset);
                if (!hybrid.IsStream)
                    throw new PdfSyntaxException("Trailer /XRefStm must point to a cross-reference stream", (int)hybridOffset);
            }

            revisions.Add(new Revision(primary, hybrid));
            currentOffset = primary.PreviousOffset;
        }

        var entries = new Dictionary<int, PdfCrossReferenceEntry>();
        foreach (Revision revision in revisions)
        {
            // In a hybrid revision, stream entries supply compressed-object information absent
            // from the classic table and take precedence if a producer emitted both.
            if (revision.Hybrid is not null)
                AddNewest(entries, revision.Hybrid.Values);
            AddNewest(entries, revision.Primary.Values);
        }

        return new PdfCrossReferenceTable(header, startXref, revisions, entries);
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
}
