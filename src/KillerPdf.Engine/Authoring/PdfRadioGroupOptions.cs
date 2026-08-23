namespace KillerPdf.Engine.Authoring;

/// <summary>Behavior specific to an AcroForm radio-button group.</summary>
public sealed record PdfRadioGroupOptions
{
    public bool NoToggleToOff { get; init; }
    public bool RadiosInUnison { get; init; }
}
