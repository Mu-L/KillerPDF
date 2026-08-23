namespace KillerPdf.Engine.Signing;

/// <summary>Incremental revisions and object definitions added after a signature revision.</summary>
public sealed record PdfSignedRevisionAnalysis
{
    public long SignedRevisionLength { get; init; }
    public long CurrentDocumentLength { get; init; }
    public bool SignedRevisionIsValidPdf { get; init; }
    public int LaterRevisionCount { get; init; }
    public IReadOnlyList<int> ChangedObjectNumbers { get; init; } = [];
    public IReadOnlyList<int> AddedObjectNumbers { get; init; } = [];
    public IReadOnlyList<int> UpdatedObjectNumbers { get; init; } = [];
    public IReadOnlyList<int> FreedObjectNumbers { get; init; } = [];
    public bool HasLaterChanges => CurrentDocumentLength > SignedRevisionLength;
}
