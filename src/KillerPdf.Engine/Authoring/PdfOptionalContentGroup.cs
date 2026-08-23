namespace KillerPdf.Engine.Authoring;

/// <summary>A named PDF layer whose initial viewer visibility can be configured.</summary>
public sealed class PdfOptionalContentGroup
{
    public PdfOptionalContentGroup(string name, bool initiallyVisible = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An optional-content group name cannot be empty.", nameof(name));
        Name = name;
        InitiallyVisible = initiallyVisible;
    }

    public string Name { get; }
    public bool InitiallyVisible { get; }
}
