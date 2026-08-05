using System;
using System.Text.RegularExpressions;

namespace KillerPDF.Services
{
    internal readonly record struct DetectedPdfFontStyle(string Family, bool Bold, bool Italic);

    internal static class PdfFontStyle
    {
        // PDF font resources commonly carry face styling in their PostScript names rather than
        // separate metadata. Keep that styling when a source line is lifted into the text editor.
        internal static DetectedPdfFontStyle FromPdfName(string rawName)
        {
            string name = rawName?.Trim() ?? string.Empty;
            int subset = name.IndexOf('+');
            if (subset >= 0 && subset + 1 < name.Length) name = name[(subset + 1)..];

            bool bold = Regex.IsMatch(name, @"(?i)(bold|semibold|demibold|black|heavy|[-_,]bd(?:mt)?$)");
            bool italic = Regex.IsMatch(name, @"(?i)(italic|oblique|[-_,](?:it|obl)(?:mt)?$)");

            // Remove only trailing face tokens. A style word that is genuinely part of a family
            // name elsewhere in the string is left alone.
            string family = Regex.Replace(name,
                @"(?i)(?:[-_, ]?(?:bolditalic|boldoblique|semibolditalic|demibolditalic|bold|semibold|demibold|black|heavy|italic|oblique|regular|roman|bd|it|obl)(?:mt)?)$",
                string.Empty).Trim(' ', '-', '_', ',');

            if (string.IsNullOrWhiteSpace(family)) family = "Segoe UI";
            return new DetectedPdfFontStyle(family, bold, italic);
        }
    }
}
