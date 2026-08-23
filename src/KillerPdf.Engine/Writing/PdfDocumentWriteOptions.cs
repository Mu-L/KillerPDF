using KillerPdf.Engine.Syntax;

namespace KillerPdf.Engine.Writing;

public enum PdfMetadataPolicy
{
    Preserve,
    RemoveDocumentInformation
}

/// <summary>Explicit policy choices for a deterministic full rewrite.</summary>
public sealed class PdfDocumentWriteOptions
{
    /// <summary>Null preserves the source header version. Rewrites may upgrade but not downgrade.</summary>
    public PdfVersion? TargetVersion { get; init; }

    public PdfMetadataPolicy MetadataPolicy { get; init; } = PdfMetadataPolicy.Preserve;

    /// <summary>Preserves the trailer /ID pair independently from descriptive document information.</summary>
    public bool PreserveDocumentIdentifiers { get; init; } = true;
}
