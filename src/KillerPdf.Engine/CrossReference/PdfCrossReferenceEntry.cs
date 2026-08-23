namespace KillerPdf.Engine.CrossReference;

public enum PdfCrossReferenceEntryType
{
    Free,
    InUse,
    Compressed,
    /// <summary>An unknown future xref-stream type, which PDF requires readers to treat as null.</summary>
    Null
}

/// <summary>
/// One cross-reference entry. Field1 is the next free object, byte offset, or object-stream
/// number; Field2 is the generation or object-stream index, according to <see cref="Type"/>.
/// </summary>
public readonly record struct PdfCrossReferenceEntry(
    int ObjectNumber,
    PdfCrossReferenceEntryType Type,
    long Field1,
    int Field2);
