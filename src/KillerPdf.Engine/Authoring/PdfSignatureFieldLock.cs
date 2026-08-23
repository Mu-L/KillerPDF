namespace KillerPdf.Engine.Authoring;

public enum PdfSignatureLockAction
{
    All,
    Include,
    Exclude
}

/// <summary>Document changes permitted after an approval signature is applied.</summary>
public enum PdfSignatureLockPermission
{
    NoChanges = 1,
    FormFillingAndSignatures = 2,
    FormFillingSignaturesAndAnnotations = 3
}

/// <summary>Fields that become locked when a signature field is signed.</summary>
public sealed record PdfSignatureFieldLock(
    PdfSignatureLockAction Action,
    IReadOnlyList<string>? Fields = null,
    PdfSignatureLockPermission? Permission = null);
