namespace KillerPdf.Engine.Authoring;

/// <summary>Behavior shared by combo boxes and list boxes.</summary>
public sealed record PdfChoiceFieldOptions
{
    public bool SortOptions { get; init; }
    public bool DoNotSpellCheck { get; init; }
    public bool CommitOnSelectionChange { get; init; }
    public PdfTextFieldAlignment Alignment { get; init; }
    public IReadOnlyList<string>? DefaultSelectedExportValues { get; init; }
    public PdfFormFieldAppearanceStyle? AppearanceStyle { get; init; }
}
