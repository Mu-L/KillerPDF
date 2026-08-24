using System.Text;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Authoring;

/// <summary>Controls how a destination page is positioned in a conforming viewer.</summary>
public sealed class PdfDestination
{
    private PdfDestination(PdfDestinationKind kind, params double?[] values)
    {
        Kind = kind;
        Values = values;
    }

    public static PdfDestination FitPage() => new(PdfDestinationKind.Fit);
    public static PdfDestination FitBoundingBox() => new(PdfDestinationKind.FitB);
    public static PdfDestination FitWidth(double? top = null) =>
        new(PdfDestinationKind.FitH, Optional(top, nameof(top)));
    public static PdfDestination FitHeight(double? left = null) =>
        new(PdfDestinationKind.FitV, Optional(left, nameof(left)));
    public static PdfDestination FitBoundingBoxWidth(double? top = null) =>
        new(PdfDestinationKind.FitBH, Optional(top, nameof(top)));
    public static PdfDestination FitBoundingBoxHeight(double? left = null) =>
        new(PdfDestinationKind.FitBV, Optional(left, nameof(left)));
    public static PdfDestination At(double? left = null, double? top = null, double? zoom = null)
    {
        if (zoom.HasValue && (!double.IsFinite(zoom.Value) || zoom.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(zoom));
        return new(PdfDestinationKind.Xyz,
            Optional(left, nameof(left)), Optional(top, nameof(top)), zoom);
    }
    public static PdfDestination FitRectangle(
        double left, double bottom, double right, double top)
    {
        if (!double.IsFinite(left)) throw new ArgumentOutOfRangeException(nameof(left));
        if (!double.IsFinite(bottom)) throw new ArgumentOutOfRangeException(nameof(bottom));
        if (!double.IsFinite(right) || right <= left) throw new ArgumentOutOfRangeException(nameof(right));
        if (!double.IsFinite(top) || top <= bottom) throw new ArgumentOutOfRangeException(nameof(top));
        return new(PdfDestinationKind.FitR, left, bottom, right, top);
    }

    internal PdfDestinationKind Kind { get; }
    internal IReadOnlyList<double?> Values { get; }

    internal PdfArray ToArray(PdfIndirectReference page)
    {
        var values = new List<PdfObject> { page, Name(Kind switch
        {
            PdfDestinationKind.Xyz => "XYZ",
            PdfDestinationKind.Fit => "Fit",
            PdfDestinationKind.FitH => "FitH",
            PdfDestinationKind.FitV => "FitV",
            PdfDestinationKind.FitR => "FitR",
            PdfDestinationKind.FitB => "FitB",
            PdfDestinationKind.FitBH => "FitBH",
            PdfDestinationKind.FitBV => "FitBV",
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        }) };
        values.AddRange(Values.Select(value => value switch
        {
            null => (PdfObject)PdfNull.Instance,
            double number when number == Math.Truncate(number)
                && number is >= long.MinValue and <= long.MaxValue =>
                new PdfInteger((long)number),
            double number => new PdfReal(number)
        }));
        return new PdfArray(values);
    }

    private static double? Optional(double? value, string name)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
            throw new ArgumentOutOfRangeException(name);
        return value;
    }

    private static PdfName Name(string value) =>
        new(Encoding.ASCII.GetBytes(value));
}

internal enum PdfDestinationKind { Xyz, Fit, FitH, FitV, FitR, FitB, FitBH, FitBV }
