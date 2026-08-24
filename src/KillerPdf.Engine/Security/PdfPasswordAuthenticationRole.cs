namespace KillerPdf.Engine.Security;

/// <summary>Identifies which password class authenticated an encrypted PDF.</summary>
public enum PdfPasswordAuthenticationRole
{
    None,
    User,
    Owner
}
