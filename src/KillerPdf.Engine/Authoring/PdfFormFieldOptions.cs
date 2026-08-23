namespace KillerPdf.Engine.Authoring;

/// <summary>Behavior shared by AcroForm field types.</summary>
public sealed record PdfFormFieldOptions
{
    public bool ReadOnly { get; init; }
    public bool Required { get; init; }
    public bool NoExport { get; init; }
}
