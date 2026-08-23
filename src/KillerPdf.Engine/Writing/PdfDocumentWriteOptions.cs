using KillerPdf.Engine.Syntax;

namespace KillerPdf.Engine.Writing;

public enum PdfMetadataPolicy
{
    Preserve,
    RemoveDocumentInformation
}

public enum PdfCrossReferenceFormat
{
    Table,
    Stream
}

/// <summary>Explicit policy choices for a deterministic full rewrite.</summary>
public sealed class PdfDocumentWriteOptions
{
    /// <summary>
    /// Permits a full rewrite of a document containing signed signature fields. Full rewrites
    /// necessarily invalidate their byte ranges, so this must be selected explicitly.
    /// </summary>
    public bool AllowSignatureInvalidation { get; init; }

    /// <summary>Null preserves the source header version. Rewrites may upgrade but not downgrade.</summary>
    public PdfVersion? TargetVersion { get; init; }

    public PdfMetadataPolicy MetadataPolicy { get; init; } = PdfMetadataPolicy.Preserve;

    /// <summary>Preserves the trailer /ID pair independently from descriptive document information.</summary>
    public bool PreserveDocumentIdentifiers { get; init; } = true;

    /// <summary>Controls whether a full rewrite ends with a classic table or a PDF 1.5+ cross-reference stream.</summary>
    public PdfCrossReferenceFormat CrossReferenceFormat { get; init; } = PdfCrossReferenceFormat.Table;

    /// <summary>Packs eligible generation-zero non-stream objects into one deterministic object stream.</summary>
    public bool UseObjectStreams { get; init; }

    /// <summary>Applies deterministic Flate compression to emitted cross-reference and object streams.</summary>
    public bool CompressStructuralStreams { get; init; }
}
