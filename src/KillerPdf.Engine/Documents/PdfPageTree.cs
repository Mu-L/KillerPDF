using System.Text;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Documents;

internal sealed class PdfPageTree
{
    private static readonly PdfName RootName = Name("Root");
    private static readonly PdfName PagesName = Name("Pages");
    private static readonly PdfName KidsName = Name("Kids");
    private static readonly PdfName TypeName = Name("Type");
    private static readonly PdfName PageName = Name("Page");
    private static readonly PdfName[] InheritableNames =
    [
        Name("Resources"), Name("MediaBox"), Name("CropBox"), Name("Rotate")
    ];
    private const int MaximumDepth = 256;
    private const int MaximumPageCount = 1_000_000;

    private PdfPageTree(
        PdfIndirectReference catalogReference, PdfDictionary catalog,
        PdfIndirectReference rootReference, IReadOnlyList<PdfPageTreeEntry> pages)
    {
        CatalogReference = catalogReference;
        Catalog = catalog;
        RootReference = rootReference;
        Pages = pages;
    }

    internal PdfIndirectReference CatalogReference { get; }
    internal PdfDictionary Catalog { get; }
    internal PdfIndirectReference RootReference { get; }
    internal IReadOnlyList<PdfPageTreeEntry> Pages { get; }

    internal static PdfPageTree Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PdfIndirectReference catalogReference = document.CrossReferences.TryGetTrailerValue(
            RootName, out PdfObject rootValue)
            ? rootValue as PdfIndirectReference
                ?? throw new InvalidOperationException("The trailer /Root is not an indirect reference.")
            : throw new InvalidOperationException("The PDF trailer has no /Root.");
        PdfDictionary catalog = document.Resolve(catalogReference) as PdfDictionary
            ?? throw new InvalidOperationException("The document catalog is not a dictionary.");
        PdfIndirectReference rootReference = catalog.TryGetValue(PagesName, out PdfObject pagesValue)
            ? pagesValue as PdfIndirectReference
                ?? throw new InvalidOperationException("The catalog /Pages is not an indirect reference.")
            : throw new InvalidOperationException("The document catalog has no /Pages tree.");

        var pages = new List<PdfPageTreeEntry>();
        var active = new HashSet<int>();
        var visitedPages = new HashSet<int>();
        Visit(rootReference, 0, new Dictionary<PdfName, PdfObject>());
        return new PdfPageTree(catalogReference, catalog, rootReference, pages);

        void Visit(
            PdfIndirectReference reference, int depth,
            IReadOnlyDictionary<PdfName, PdfObject> inherited)
        {
            if (depth > MaximumDepth)
                throw new InvalidOperationException("The page tree exceeds the supported nesting depth.");
            if (!active.Add(reference.ObjectNumber))
                throw new InvalidOperationException("The page tree contains a cycle.");
            try
            {
                PdfDictionary node = document.Resolve(reference) as PdfDictionary
                    ?? throw new InvalidOperationException("A page-tree reference is not a dictionary.");
                var effective = new Dictionary<PdfName, PdfObject>(inherited);
                foreach (PdfName name in InheritableNames)
                    if (node.TryGetValue(name, out PdfObject value)) effective[name] = value;
                if (node.TryGetValue(TypeName, out PdfObject type)
                    && type is PdfName typeName && typeName.Equals(PageName))
                {
                    if (!visitedPages.Add(reference.ObjectNumber))
                        throw new InvalidOperationException("The page tree references the same page more than once.");
                    if (pages.Count >= MaximumPageCount)
                        throw new InvalidOperationException("The PDF contains too many pages.");
                    pages.Add(new PdfPageTreeEntry(pages.Count, reference, node, effective));
                    return;
                }
                PdfArray kids = node.TryGetValue(KidsName, out PdfObject kidsValue)
                    ? kidsValue as PdfArray
                        ?? throw new InvalidOperationException("A page-tree /Kids value is not an array.")
                    : throw new InvalidOperationException("A page-tree node has neither /Type /Page nor /Kids.");
                foreach (PdfObject kid in kids)
                    Visit(kid as PdfIndirectReference
                        ?? throw new InvalidOperationException("A page-tree kid is not an indirect reference."),
                        depth + 1, effective);
            }
            finally
            {
                active.Remove(reference.ObjectNumber);
            }
        }
    }

    internal static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}

internal sealed record PdfPageTreeEntry(
    int Index, PdfIndirectReference Reference, PdfDictionary Dictionary,
    IReadOnlyDictionary<PdfName, PdfObject> InheritedValues);
