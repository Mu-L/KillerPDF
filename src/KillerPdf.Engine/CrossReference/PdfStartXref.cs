using KillerPdf.Engine.Syntax;

namespace KillerPdf.Engine.CrossReference;

/// <summary>The final startxref declaration and the byte offset it points to.</summary>
public readonly record struct PdfStartXref(long Offset, int MarkerOffset)
{
    private static ReadOnlySpan<byte> Marker => "startxref"u8;
    private static ReadOnlySpan<byte> EndMarker => "%%EOF"u8;

    public static PdfStartXref Find(ReadOnlySpan<byte> source)
    {
        int markerOffset = source.LastIndexOf(Marker);
        if (markerOffset < 0)
            throw new PdfSyntaxException("The PDF does not contain a final startxref declaration", source.Length);

        int position = markerOffset + Marker.Length;
        SkipWhitespace(source, ref position);
        int numberOffset = position;
        long offset = 0;
        while (position < source.Length && source[position] is >= (byte)'0' and <= (byte)'9')
        {
            try
            {
                offset = checked(offset * 10 + source[position] - (byte)'0');
            }
            catch (OverflowException ex)
            {
                throw new PdfSyntaxException($"The startxref offset is too large: {ex.Message}", numberOffset);
            }
            position++;
        }

        if (position == numberOffset)
            throw new PdfSyntaxException("The startxref declaration does not contain a byte offset", numberOffset);
        if (offset >= source.Length)
            throw new PdfSyntaxException("The startxref offset points beyond the end of the file", numberOffset);

        SkipWhitespace(source, ref position);
        if (!source[position..].StartsWith(EndMarker))
            throw new PdfSyntaxException("The startxref declaration is not followed by %%EOF", position);
        position += EndMarker.Length;
        SkipWhitespace(source, ref position);
        if (position != source.Length)
            throw new PdfSyntaxException("Unexpected data follows the final %%EOF marker", position);

        return new PdfStartXref(offset, markerOffset);
    }

    private static void SkipWhitespace(ReadOnlySpan<byte> source, ref int position)
    {
        while (position < source.Length && source[position] is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
            position++;
    }
}
