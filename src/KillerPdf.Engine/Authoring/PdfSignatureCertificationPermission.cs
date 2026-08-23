namespace KillerPdf.Engine.Authoring;

/// <summary>The certification behavior requested by a signature seed value.</summary>
public enum PdfSignatureCertificationPermission
{
    ApprovalSignature = 0,
    NoChanges = 1,
    FormFillingAndSignatures = 2,
    FormFillingSignaturesAndAnnotations = 3
}
