namespace KillerPdf.Engine.Writing;

/// <summary>File-structure choices for an appended incremental revision.</summary>
public sealed class PdfIncrementalUpdateWriteOptions
{
    public PdfCrossReferenceFormat CrossReferenceFormat { get; init; } =
        PdfCrossReferenceFormat.Table;

    /// <summary>Applies deterministic Flate compression to an emitted cross-reference stream.</summary>
    public bool CompressCrossReferenceStream { get; init; }

    /// <summary>Packs eligible generation-zero non-stream updates into a bounded object stream.</summary>
    public bool UseObjectStreams { get; init; }

    /// <summary>Applies deterministic Flate compression to emitted object streams.</summary>
    public bool CompressObjectStreams { get; init; }
}
