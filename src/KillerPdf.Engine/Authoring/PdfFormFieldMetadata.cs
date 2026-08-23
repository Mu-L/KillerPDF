namespace KillerPdf.Engine.Authoring;

/// <summary>Optional user-facing and export names for an AcroForm field.</summary>
public sealed record PdfFormFieldMetadata
{
    public string? Tooltip { get; init; }
    public string? MappingName { get; init; }
}
