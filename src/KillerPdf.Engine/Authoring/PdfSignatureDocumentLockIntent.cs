namespace KillerPdf.Engine.Authoring;

/// <summary>Whether the signing interface should lock the document after signing.</summary>
public enum PdfSignatureDocumentLockIntent
{
    Automatic,
    Lock,
    DoNotLock
}
