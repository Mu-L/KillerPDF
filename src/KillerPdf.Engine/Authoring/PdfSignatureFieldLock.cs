namespace KillerPdf.Engine.Authoring;

public enum PdfSignatureLockAction
{
    All,
    Include,
    Exclude
}

/// <summary>Fields that become locked when a signature field is signed.</summary>
public sealed record PdfSignatureFieldLock(
    PdfSignatureLockAction Action,
    IReadOnlyList<string>? Fields = null);
