using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;

namespace KillerPdf.Engine.Documents;

/// <summary>High-level descriptive and structural information read from a PDF document.</summary>
public sealed record PdfDocumentInformation
{
    /// <summary>Gets the document title.</summary>
    public string? Title { get; init; }
    /// <summary>Gets the document author.</summary>
    public string? Author { get; init; }
    /// <summary>Gets the document subject.</summary>
    public string? Subject { get; init; }
    /// <summary>Gets document-search keywords.</summary>
    public string? Keywords { get; init; }
    /// <summary>Gets the application that created the original content.</summary>
    public string? Creator { get; init; }
    /// <summary>Gets the application that produced the PDF.</summary>
    public string? Producer { get; init; }
    /// <summary>Gets the header version.</summary>
    public required PdfVersion Version { get; init; }
    /// <summary>Gets the number of leaf pages in the page tree.</summary>
    public required int PageCount { get; init; }

    /// <summary>Reads document information from an open PDF.</summary>
    public static PdfDocumentInformation Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PdfDictionary? info = null;
        if (document.Trailer.TryGetValue(new PdfName("Info"u8), out PdfObject? value))
        {
            value = Resolve(document, value);
            info = value as PdfDictionary
                ?? throw new InvalidOperationException("The trailer /Info value is not a dictionary.");
        }

        return new PdfDocumentInformation
        {
            Title = Text(info, "Title"),
            Author = Text(info, "Author"),
            Subject = Text(info, "Subject"),
            Keywords = Text(info, "Keywords"),
            Creator = Text(info, "Creator"),
            Producer = Text(info, "Producer"),
            Version = document.Header.Version,
            PageCount = PdfPageTree.Read(document).Pages.Count
        };
    }

    private static PdfObject Resolve(PdfDocument document, PdfObject value)
    {
        var visited = new HashSet<(int, int)>();
        while (value is PdfIndirectReference reference)
        {
            if (!visited.Add((reference.ObjectNumber, reference.Generation)))
                throw new InvalidOperationException("The document information reference contains a cycle.");
            value = document.Resolve(reference);
        }
        return value;
    }

    private static string? Text(PdfDictionary? info, string key)
    {
        if (info is null || !info.TryGetValue(new PdfName(System.Text.Encoding.ASCII.GetBytes(key)), out PdfObject? value))
            return null;
        return value is PdfString text
            ? PdfUnicodeEncoding.DecodeTextString(text.Bytes.Span, $"The /{key} document information value")
            : throw new InvalidOperationException($"The /{key} document information value is not a string.");
    }
}
