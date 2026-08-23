namespace KillerPdf.Engine.Authoring;

/// <summary>A standard semantic identity for a stamp annotation's custom appearance.</summary>
public enum PdfStampIcon
{
    Image,
    Approved,
    Experimental,
    NotApproved,
    AsIs,
    Expired,
    NotForPublicRelease,
    Confidential,
    Final,
    Sold,
    Departmental,
    ForComment,
    TopSecret,
    Draft,
    ForPublicRelease
}

internal static class PdfStampIconNames
{
    internal static string Name(PdfStampIcon value) => value switch
    {
        PdfStampIcon.Image => "Image",
        PdfStampIcon.Approved => "Approved",
        PdfStampIcon.Experimental => "Experimental",
        PdfStampIcon.NotApproved => "NotApproved",
        PdfStampIcon.AsIs => "AsIs",
        PdfStampIcon.Expired => "Expired",
        PdfStampIcon.NotForPublicRelease => "NotForPublicRelease",
        PdfStampIcon.Confidential => "Confidential",
        PdfStampIcon.Final => "Final",
        PdfStampIcon.Sold => "Sold",
        PdfStampIcon.Departmental => "Departmental",
        PdfStampIcon.ForComment => "ForComment",
        PdfStampIcon.TopSecret => "TopSecret",
        PdfStampIcon.Draft => "Draft",
        PdfStampIcon.ForPublicRelease => "ForPublicRelease",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
