using KillerPdf.Engine.Documents;
using System.IO;

namespace KillerPDF.Services;

/// <summary>Immutable engine view of the active serialized working document.</summary>
internal sealed class PdfEngineDocumentSession
{
    private PdfEngineDocumentSession(string path, byte[] source, PdfDocument document,
        IReadOnlyList<PdfPageInformation> pages)
    {
        Path = path;
        Source = source;
        Document = document;
        Pages = pages;
    }

    internal string Path { get; }
    internal ReadOnlyMemory<byte> Source { get; }
    internal PdfDocument Document { get; }
    internal IReadOnlyList<PdfPageInformation> Pages { get; }

    internal static PdfEngineDocumentSession Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] source = File.ReadAllBytes(path);
        PdfDocument document = PdfDocument.Open(source);
        return new PdfEngineDocumentSession(path, source, document,
            PdfPageInformation.Read(document));
    }
}
