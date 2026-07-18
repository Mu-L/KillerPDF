using System.IO;
using UglyToad.PdfPig;

namespace KillerPDF.Services
{
    /// <summary>One selectable character on a page, in reading order. Coordinates are PDF space
    /// (points, bottom-left origin), matching SearchService and ExtractTextFromRegion.</summary>
    internal readonly struct RunChar
    {
        public readonly string Value;   // PdfPig letters can be multi-char (ligatures)
        public readonly double Left;
        public readonly double Right;
        public readonly int Word;       // ordinal of the word this char belongs to (for word counts / spacing)
        public readonly int Line;       // ordinal of the line this char belongs to

        public RunChar(string value, double left, double right, int word, int line)
        { Value = value; Left = left; Right = right; Word = word; Line = line; }
    }

    /// <summary>A visual line of text: a contiguous slice of the page's flattened char list plus its
    /// vertical band. Caret positions run 0..N over the flattened chars; a line's End caret is the
    /// next line's Start, so a selection ending at End stops cleanly at the line break.</summary>
    internal sealed class RunLine
    {
        public int Start;               // caret index of the line's first char
        public int Count;
        public double Top;              // PDF space: Top > Bottom
        public double Bottom;
        public double Left;
        public double Right;
        public int End => Start + Count;
    }

    /// <summary>Reading-order text geometry for one page.</summary>
    internal sealed class PageTextRuns
    {
        public double PdfWidth;
        public double PdfHeight;
        public List<RunChar> Chars = [];
        public List<RunLine> Lines = [];
    }

    /// <summary>
    /// Builds and caches per-page reading-order character runs for flowing text selection (#127).
    /// Word geometry comes from PdfPig's GetWords - the same source SearchService and the region
    /// text extractor use - so selection quads land exactly where search highlights do. Words are
    /// grouped into lines by vertical overlap and ordered top-to-bottom, left-to-right.
    /// Known shared limitation: like the search highlights, boxes ignore in-memory page rotation.
    /// Column note: line grouping is by vertical band, so side-by-side columns join into one line;
    /// good enough for v1, revisit with a segmenter if multi-column PDFs bite.
    /// </summary>
    internal sealed class TextRunService
    {
        // Keyed by (path, last-write ticks, page): a resave or temp-reload changes the key, so stale
        // geometry can never serve a newer file. Nulls are cached too - a file PdfPig cannot open
        // should not be re-parsed on every click.
        private readonly Dictionary<(string Path, long Ticks, int Page), PageTextRuns?> _cache = [];

        public PageTextRuns? GetPage(string path, int pageIdx)
        {
            if (string.IsNullOrEmpty(path) || pageIdx < 0) return null;
            long ticks;
            try { ticks = File.GetLastWriteTimeUtc(path).Ticks; }
            catch { return null; }

            var key = (path, ticks, pageIdx);
            if (_cache.TryGetValue(key, out var hit)) return hit;
            if (_cache.Count > 512) _cache.Clear();   // simple cap; entries are tiny but unbounded is unbounded

            PageTextRuns? runs = null;
            try
            {
                using var doc = PdfDocument.Open(path);
                if (pageIdx < doc.NumberOfPages)
                    runs = Build(doc.GetPage(pageIdx + 1));   // PdfPig is 1-based
            }
            catch { /* encrypted/broken: selection just is not offered on this page */ }

            _cache[key] = runs;
            return runs;
        }

        private static PageTextRuns Build(UglyToad.PdfPig.Content.Page page)
        {
            var result = new PageTextRuns { PdfWidth = page.Width, PdfHeight = page.Height };
            var words = page.GetWords().ToList();
            if (words.Count == 0) return result;

            // Group words into lines: a word joins a line when its vertical band overlaps the line's
            // band by at least half the smaller height. Bands grow as members join.
            var lineWords = new List<(List<UglyToad.PdfPig.Content.Word> Words, double Top, double Bottom)>();
            foreach (var w in words)
            {
                var bb = w.BoundingBox;
                double wTop = bb.Top, wBottom = bb.Bottom;
                int found = -1;
                for (int i = 0; i < lineWords.Count; i++)
                {
                    var (_, lTop, lBottom) = lineWords[i];
                    double overlap = Math.Min(lTop, wTop) - Math.Max(lBottom, wBottom);
                    double minH = Math.Min(lTop - lBottom, wTop - wBottom);
                    if (minH > 0 && overlap >= minH * 0.5) { found = i; break; }
                }
                if (found < 0)
                    lineWords.Add((new List<UglyToad.PdfPig.Content.Word> { w }, wTop, wBottom));
                else
                {
                    var entry = lineWords[found];
                    entry.Words.Add(w);
                    lineWords[found] = (entry.Words, Math.Max(entry.Top, wTop), Math.Min(entry.Bottom, wBottom));
                }
            }

            // Reading order: lines top-to-bottom (PDF Y grows upward, so larger Top first),
            // words left-to-right inside each line.
            lineWords.Sort((a, b) => b.Top.CompareTo(a.Top));

            int wordOrdinal = 0;
            for (int li = 0; li < lineWords.Count; li++)
            {
                var (ws, top, bottom) = lineWords[li];
                ws.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

                var line = new RunLine { Start = result.Chars.Count, Top = top, Bottom = bottom };
                foreach (var w in ws)
                {
                    foreach (var letter in w.Letters)
                    {
                        var g = letter.BoundingBox;
                        result.Chars.Add(new RunChar(letter.Value, g.Left, g.Right, wordOrdinal, li));
                    }
                    wordOrdinal++;
                }
                line.Count = result.Chars.Count - line.Start;
                if (line.Count == 0) continue;
                line.Left = result.Chars[line.Start].Left;
                line.Right = result.Chars[line.End - 1].Right;
                result.Lines.Add(line);
            }
            return result;
        }

        /// <summary>True when the point sits ON text: inside a line's vertical band and within its
        /// horizontal extent (small slop). This is the gate that decides flowing selection vs the
        /// classic marquee - empty page areas must keep the marquee.</summary>
        public static bool IsOverText(PageTextRuns runs, double x, double y)
        {
            const double slop = 2.0;   // PDF points
            foreach (var line in runs.Lines)
                if (y <= line.Top + slop && y >= line.Bottom - slop &&
                    x >= line.Left - slop && x <= line.Right + slop)
                    return true;
            return false;
        }

        /// <summary>Caret position (0..Chars.Count) nearest a point, browser-style clamping:
        /// above the first line selects from the page start, below the last line to the page end,
        /// between lines snaps to the closer line, beyond a line's ends clamps to its ends.</summary>
        public static int CaretFromPoint(PageTextRuns runs, double x, double y)
        {
            if (runs.Lines.Count == 0) return 0;

            RunLine? target = null;
            double best = double.MaxValue;
            foreach (var line in runs.Lines)
            {
                if (y <= line.Top && y >= line.Bottom) { target = line; best = 0; break; }
                double d = y > line.Top ? y - line.Top : line.Bottom - y;
                if (d < best) { best = d; target = line; }
            }
            // Above the first line entirely -> caret 0; below the last -> caret N.
            var first = runs.Lines[0];
            var last = runs.Lines[runs.Lines.Count - 1];
            if (y > first.Top && target == first && x < first.Left) return 0;
            if (y < last.Bottom && target == last && x > last.Right) return runs.Chars.Count;
            if (target is null) return 0;

            if (x <= target.Left) return target.Start;
            if (x >= target.Right) return target.End;
            for (int i = target.Start; i < target.End; i++)
            {
                var c = runs.Chars[i];
                double mid = (c.Left + c.Right) / 2;
                if (x < mid) return i;
            }
            return target.End;
        }

        /// <summary>Text for the caret range [start, end): spaces between words, newlines between
        /// lines. Also reports how many distinct words the range touches.</summary>
        public static string TextForRange(PageTextRuns runs, int start, int end, out int wordCount)
        {
            wordCount = 0;
            var sb = new System.Text.StringBuilder();
            int lastWord = -1, lastLine = -1;
            for (int i = Math.Max(0, start); i < Math.Min(end, runs.Chars.Count); i++)
            {
                var c = runs.Chars[i];
                if (lastLine >= 0 && c.Line != lastLine) sb.Append('\n');
                else if (lastWord >= 0 && c.Word != lastWord) sb.Append(' ');
                if (c.Word != lastWord) wordCount++;
                sb.Append(c.Value);
                lastWord = c.Word;
                lastLine = c.Line;
            }
            return sb.ToString();
        }
    }
}
