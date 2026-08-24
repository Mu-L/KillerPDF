namespace KillerPdf.Engine.Authoring;

/// <summary>Standard numbering styles for tagged list structure elements.</summary>
public enum PdfListNumbering
{
    None,
    Disc,
    Circle,
    Square,
    Decimal,
    UpperRoman,
    LowerRoman,
    UpperAlpha,
    LowerAlpha
}

internal static class PdfListNumberingNames
{
    internal static string Name(PdfListNumbering value) => value switch
    {
        PdfListNumbering.None => "None",
        PdfListNumbering.Disc => "Disc",
        PdfListNumbering.Circle => "Circle",
        PdfListNumbering.Square => "Square",
        PdfListNumbering.Decimal => "Decimal",
        PdfListNumbering.UpperRoman => "UpperRoman",
        PdfListNumbering.LowerRoman => "LowerRoman",
        PdfListNumbering.UpperAlpha => "UpperAlpha",
        PdfListNumbering.LowerAlpha => "LowerAlpha",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
