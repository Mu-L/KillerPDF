using System.Text;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Documents;

internal static class PdfNameTree
{
    private static readonly PdfName NamesName = Name("Names");
    private static readonly PdfName KidsName = Name("Kids");
    private const int MaximumDepth = 256;
    private const int MaximumEntryCount = 1_000_000;

    internal static IReadOnlyList<PdfNameTreeEntry> Read(PdfDocument document, PdfObject root)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(root);
        var result = new List<PdfNameTreeEntry>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<(int ObjectNumber, int Generation)>();
        var visited = new HashSet<(int ObjectNumber, int Generation)>();
        Visit(root, 0);
        return result;

        void Visit(PdfObject value, int depth)
        {
            if (depth > MaximumDepth)
                throw new InvalidOperationException("The name tree exceeds the supported nesting depth.");
            (int ObjectNumber, int Generation)? referenceKey = null;
            if (value is PdfIndirectReference reference)
            {
                referenceKey = (reference.ObjectNumber, reference.Generation);
                if (!active.Add(referenceKey.Value))
                    throw new InvalidOperationException("The name tree contains a cycle.");
                if (!visited.Add(referenceKey.Value))
                {
                    active.Remove(referenceKey.Value);
                    throw new InvalidOperationException(
                        "The name tree references the same node more than once.");
                }
                value = document.Resolve(reference);
            }
            try
            {
                PdfDictionary node = value as PdfDictionary
                    ?? throw new InvalidOperationException("A name-tree node is not a dictionary.");
                bool hasNames = node.TryGetValue(NamesName, out PdfObject? namesValue);
                bool hasKids = node.TryGetValue(KidsName, out PdfObject? kidsValue);
                if (hasNames == hasKids)
                    throw new InvalidOperationException(
                        "A name-tree node must contain exactly one of /Names or /Kids.");
                if (hasNames)
                {
                    PdfArray names = namesValue as PdfArray
                        ?? throw new InvalidOperationException("A name-tree /Names value is not an array.");
                    if (names.Count % 2 != 0)
                        throw new InvalidOperationException("A name-tree /Names array has an unmatched key.");
                    for (int index = 0; index < names.Count; index += 2)
                    {
                        PdfString key = names[index] as PdfString
                            ?? throw new InvalidOperationException("A name-tree key is not a string.");
                        if (!keys.Add(Convert.ToBase64String(key.Bytes.Span)))
                            throw new InvalidOperationException("The name tree contains a duplicate key.");
                        if (result.Count >= MaximumEntryCount)
                            throw new NotSupportedException("The name tree contains too many entries.");
                        result.Add(new PdfNameTreeEntry(key, names[index + 1]));
                    }
                    return;
                }
                PdfArray kids = kidsValue as PdfArray
                    ?? throw new InvalidOperationException("A name-tree /Kids value is not an array.");
                foreach (PdfObject kid in kids) Visit(kid, depth + 1);
            }
            finally
            {
                if (referenceKey.HasValue) active.Remove(referenceKey.Value);
            }
        }
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}

internal sealed record PdfNameTreeEntry(PdfString Key, PdfObject Value);
