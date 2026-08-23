using System.Text;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Documents;

internal static class PdfNumberTree
{
    private static readonly PdfName NumsName = Name("Nums");
    private static readonly PdfName KidsName = Name("Kids");
    private const int MaximumDepth = 256;
    private const int MaximumEntryCount = 1_000_000;

    internal static IReadOnlyList<PdfNumberTreeEntry> Read(PdfDocument document, PdfObject root)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(root);
        var result = new List<PdfNumberTreeEntry>();
        var keys = new HashSet<long>();
        var active = new HashSet<int>();
        Visit(root, 0);
        return result;

        void Visit(PdfObject value, int depth)
        {
            if (depth > MaximumDepth)
                throw new InvalidOperationException("The number tree exceeds the supported nesting depth.");
            int? referenceNumber = null;
            if (value is PdfIndirectReference reference)
            {
                referenceNumber = reference.ObjectNumber;
                if (!active.Add(reference.ObjectNumber))
                    throw new InvalidOperationException("The number tree contains a cycle.");
                value = document.Resolve(reference);
            }
            try
            {
                PdfDictionary node = value as PdfDictionary
                    ?? throw new InvalidOperationException("A number-tree node is not a dictionary.");
                bool hasNumbers = node.TryGetValue(NumsName, out PdfObject? numbersValue);
                bool hasKids = node.TryGetValue(KidsName, out PdfObject? kidsValue);
                if (hasNumbers == hasKids)
                    throw new InvalidOperationException(
                        "A number-tree node must contain exactly one of /Nums or /Kids.");
                if (hasNumbers)
                {
                    PdfArray numbers = numbersValue as PdfArray
                        ?? throw new InvalidOperationException("A number-tree /Nums value is not an array.");
                    if (numbers.Count % 2 != 0)
                        throw new InvalidOperationException("A number-tree /Nums array has an unmatched key.");
                    for (int index = 0; index < numbers.Count; index += 2)
                    {
                        PdfInteger key = numbers[index] as PdfInteger
                            ?? throw new InvalidOperationException("A number-tree key is not an integer.");
                        if (!keys.Add(key.Value))
                            throw new InvalidOperationException("The number tree contains a duplicate key.");
                        if (result.Count >= MaximumEntryCount)
                            throw new NotSupportedException("The number tree contains too many entries.");
                        result.Add(new PdfNumberTreeEntry(key.Value, numbers[index + 1]));
                    }
                    return;
                }
                PdfArray kids = kidsValue as PdfArray
                    ?? throw new InvalidOperationException("A number-tree /Kids value is not an array.");
                foreach (PdfObject kid in kids) Visit(kid, depth + 1);
            }
            finally
            {
                if (referenceNumber.HasValue) active.Remove(referenceNumber.Value);
            }
        }
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}

internal sealed record PdfNumberTreeEntry(long Key, PdfObject Value);
