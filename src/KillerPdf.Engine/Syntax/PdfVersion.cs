using System.Globalization;

namespace KillerPdf.Engine.Syntax;

/// <summary>A PDF specification version that KillerPdf.Engine can read or write.</summary>
public readonly record struct PdfVersion : IComparable<PdfVersion>
{
    public static readonly PdfVersion Pdf10 = new(1, 0);
    public static readonly PdfVersion Pdf17 = new(1, 7);
    public static readonly PdfVersion Pdf20 = new(2, 0);

    public PdfVersion(int major, int minor)
    {
        if (!IsDefined(major, minor))
            throw new ArgumentOutOfRangeException(nameof(minor), $"PDF {major}.{minor} is not a defined PDF version.");

        Major = major;
        Minor = minor;
    }

    public int Major { get; }
    public int Minor { get; }

    // ISO 32000-2 reserves the 2.x header range for PDF 2.0-compatible declarations.
    public static bool IsDefined(int major, int minor) =>
        (major == 1 && minor is >= 0 and <= 7) || (major == 2 && minor is >= 0 and <= 9);

    public int CompareTo(PdfVersion other)
    {
        int major = Major.CompareTo(other.Major);
        return major != 0 ? major : Minor.CompareTo(other.Minor);
    }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"{Major}.{Minor}");
}
