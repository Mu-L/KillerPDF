using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Authoring;

/// <summary>
/// Reusable vector or composite page content stored once as a PDF Form XObject.
/// </summary>
public sealed class PdfFormXObject
{
    public PdfFormXObject(
        double width, double height, PdfContentStreamBuilder content,
        bool isolatedTransparencyGroup = false, bool knockoutTransparencyGroup = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        Width = Dimension(width, nameof(width));
        Height = Dimension(height, nameof(height));
        if (content.MarkedContentIds.Count > 0)
            throw new ArgumentException(
                "Tagged marked content belongs to a page structure tree and cannot be captured inside a reusable form.",
                nameof(content));

        Content = content.Build();
        Fonts = content.FontResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        EmbeddedFonts = content.EmbeddedFontResources.ToArray();
        Images = content.ImageResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        OptionalContentGroups = content.OptionalContentResources
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        GraphicsStates = content.GraphicsStateResources
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        Shadings = content.ShadingResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        Forms = content.FormResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        Patterns = content.PatternResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        IccColorSpaces = content.IccColorSpaceResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        SpotColors = content.SpotColorResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        LabColorSpaces = content.LabColorSpaceResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        IndexedColorSpaces = content.IndexedColorSpaceResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        CalibratedColorSpaces = content.CalibratedColorSpaceResources.ToDictionary(entry => entry.Key, entry => entry.Value);
        IsolatedTransparencyGroup = isolatedTransparencyGroup;
        KnockoutTransparencyGroup = knockoutTransparencyGroup;
    }

    public double Width { get; }
    public double Height { get; }
    public bool IsolatedTransparencyGroup { get; }
    public bool KnockoutTransparencyGroup { get; }

    internal byte[] Content { get; }
    internal IReadOnlyDictionary<PdfStandardFont, PdfName> Fonts { get; }
    internal IReadOnlyCollection<EmbeddedFontUsage> EmbeddedFonts { get; }
    internal IReadOnlyDictionary<PdfImage, PdfName> Images { get; }
    internal IReadOnlyDictionary<PdfOptionalContentGroup, PdfName> OptionalContentGroups { get; }
    internal IReadOnlyDictionary<PdfGraphicsState, PdfName> GraphicsStates { get; }
    internal IReadOnlyDictionary<PdfShading, PdfName> Shadings { get; }
    internal IReadOnlyDictionary<PdfFormXObject, PdfName> Forms { get; }
    internal IReadOnlyDictionary<PdfTilingPattern, PdfName> Patterns { get; }
    internal IReadOnlyDictionary<PdfIccProfile, PdfName> IccColorSpaces { get; }
    internal IReadOnlyDictionary<PdfSpotColor, PdfName> SpotColors { get; }
    internal IReadOnlyDictionary<PdfLabColorSpace, PdfName> LabColorSpaces { get; }
    internal IReadOnlyDictionary<PdfIndexedColorSpace, PdfName> IndexedColorSpaces { get; }
    internal IReadOnlyDictionary<PdfCalibratedColorSpace, PdfName> CalibratedColorSpaces { get; }

    private static double Dimension(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name,
                "A form dimension must be finite and greater than zero.");
        return value;
    }
}
