namespace KillerPdf.Engine.Authoring;

public sealed record PdfTextFieldOptions
{
    public bool ReadOnly { get; init; }
    public bool Required { get; init; }
    public bool Multiline { get; init; }
    public bool Password { get; init; }
    public bool Comb { get; init; }
    public int? MaximumLength { get; init; }
}
