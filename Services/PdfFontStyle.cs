using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KillerPDF.Services
{
    internal readonly record struct DetectedPdfFontStyle(string Family, bool Bold, bool Italic);

    internal static class PdfFontStyle
    {
        // #187: PDF font resources carry POSTSCRIPT names, which are not Windows family names.
        // "ArialMT", "TimesNewRomanPSMT" and "Helvetica" resolve to no installed family, so WPF
        // silently fell back and the save path landed on the default font - the detected face was
        // right but the family never applied, which read as "all formatting lost". Keyed on the
        // name with separators removed, lowercase.
        private static readonly Dictionary<string, string> PsNameMap = new(StringComparer.Ordinal)
        {
            ["helvetica"]         = "Arial",
            ["helveticaneue"]     = "Arial",
            ["arial"]             = "Arial",
            ["arialmt"]           = "Arial",
            ["arialnarrow"]       = "Arial Narrow",
            ["times"]             = "Times New Roman",
            ["timesnewroman"]     = "Times New Roman",
            ["timesnewromanps"]   = "Times New Roman",
            ["timesnewromanpsmt"] = "Times New Roman",
            ["courier"]           = "Courier New",
            ["couriernew"]        = "Courier New",
            ["couriernewps"]      = "Courier New",
            ["couriernewpsmt"]    = "Courier New",
            ["symbol"]            = "Symbol",
            ["zapfdingbats"]      = "Wingdings",
            ["segoeui"]           = "Segoe UI",
        };

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

            family = NormalizePsFamily(family);

            if (string.IsNullOrWhiteSpace(family)) family = "Segoe UI";
            return new DetectedPdfFontStyle(family, bold, italic);
        }

        // Maps a face-stripped PostScript family to the Windows family it means (#187). Unknown
        // names get their trailing PS/MT foundry tags dropped and CamelCase split into words
        // ("BookAntiqua" -> "Book Antiqua"), which is how PostScript names encode the family.
        private static string NormalizePsFamily(string family)
        {
            if (string.IsNullOrWhiteSpace(family)) return family;

            string key = Regex.Replace(family, @"[-_, ]", "").ToLowerInvariant();
            if (PsNameMap.TryGetValue(key, out var mapped)) return mapped;

            // Trailing foundry tags: TimesNewRomanPSMT-style names that are not in the map.
            string trimmed = Regex.Replace(family, @"(?:PSMT|PS|MT)$", string.Empty);
            if (trimmed.Length > 0 && trimmed != family)
            {
                key = Regex.Replace(trimmed, @"[-_, ]", "").ToLowerInvariant();
                if (PsNameMap.TryGetValue(key, out mapped)) return mapped;
                family = trimmed;
            }

            // CamelCase -> spaced words, only when the name has no separators already.
            if (!family.Contains(' ') && !family.Contains('-') && !family.Contains('_'))
                family = Regex.Replace(family, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Za-z])(?=\d)", " ");

            return family;
        }
    }
}
