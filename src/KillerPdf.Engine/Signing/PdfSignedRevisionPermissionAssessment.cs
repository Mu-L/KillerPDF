namespace KillerPdf.Engine.Signing;

/// <summary>Conservative DocMDP assessment for changes after a signed revision.</summary>
public enum PdfSignedRevisionPermissionAssessment
{
    NotCertified,
    NoLaterChanges,
    Prohibited,
    RequiresSemanticReview
}
