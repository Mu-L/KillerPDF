using System.Text;

namespace KillerPdf.Engine.Objects;

internal static class PdfUnicodeEncoding
{
    private static readonly UnicodeEncoding BigEndian = new(
        bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static byte[] EncodeBigEndian(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            return BigEndian.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "PDF Unicode text cannot contain an unpaired UTF-16 surrogate.",
                nameof(value), exception);
        }
    }

    internal static byte[] EncodeUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            return Utf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "PDF UTF-8 text cannot contain an unpaired UTF-16 surrogate.",
                nameof(value), exception);
        }
    }

    internal static string DecodeBigEndian(ReadOnlySpan<byte> value, string description)
    {
        try
        {
            return BigEndian.GetString(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException(
                $"{description} contains malformed UTF-16BE text.", exception);
        }
    }
}
