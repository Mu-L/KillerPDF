using System.IO.Compression;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Filters;

/// <summary>Decodes a stream's filter pipeline with a hard output-size limit.</summary>
public static class PdfStreamDecoder
{
    public const int DefaultMaximumDecodedBytes = 256 * 1024 * 1024;

    private static readonly PdfName FilterName = new("Filter"u8);
    private static readonly PdfName DecodeParmsName = new("DecodeParms"u8);
    private static readonly PdfName PredictorName = new("Predictor"u8);
    private static readonly PdfName ColorsName = new("Colors"u8);
    private static readonly PdfName BitsPerComponentName = new("BitsPerComponent"u8);
    private static readonly PdfName ColumnsName = new("Columns"u8);

    public static byte[] Decode(PdfStream stream, int maximumDecodedBytes = DefaultMaximumDecodedBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maximumDecodedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));

        IReadOnlyList<PdfName> filters = ReadFilters(stream.Dictionary);
        IReadOnlyList<PdfDictionary?> parameters = ReadParameters(stream.Dictionary, filters.Count);
        byte[] current = stream.EncodedData.ToArray();
        if (filters.Count == 0)
        {
            EnsureWithinLimit(current.Length, maximumDecodedBytes);
            return current;
        }

        for (int i = 0; i < filters.Count; i++)
        {
            string filter = filters[i].ValueAsLatin1();
            current = filter switch
            {
                "FlateDecode" or "Fl" => DecodeFlate(current, maximumDecodedBytes),
                _ => throw new PdfFilterException($"The PDF stream filter /{filter} is not supported yet.")
            };

            current = ReversePredictor(current, parameters[i], maximumDecodedBytes);
        }

        return current;
    }

    private static IReadOnlyList<PdfName> ReadFilters(PdfDictionary dictionary)
    {
        if (!dictionary.TryGetValue(FilterName, out PdfObject filterObject))
            return [];
        if (filterObject is PdfName name)
            return [name];
        if (filterObject is not PdfArray array)
            throw new PdfFilterException("A stream Filter entry must be a name or an array of names.");

        var filters = new List<PdfName>(array.Count);
        foreach (PdfObject item in array)
        {
            if (item is not PdfName filter)
                throw new PdfFilterException("Every entry in a stream Filter array must be a name.");
            filters.Add(filter);
        }
        return filters;
    }

    private static IReadOnlyList<PdfDictionary?> ReadParameters(PdfDictionary dictionary, int filterCount)
    {
        var result = new PdfDictionary?[filterCount];
        if (!dictionary.TryGetValue(DecodeParmsName, out PdfObject parameters)
            || parameters is PdfNull)
            return result;

        if (parameters is PdfDictionary single)
        {
            if (filterCount != 1)
                throw new PdfFilterException("A single DecodeParms dictionary requires exactly one stream filter.");
            result[0] = single;
            return result;
        }

        if (parameters is not PdfArray array || array.Count != filterCount)
            throw new PdfFilterException("A DecodeParms array must have one entry per stream filter.");

        for (int i = 0; i < array.Count; i++)
        {
            result[i] = array[i] switch
            {
                PdfNull => null,
                PdfDictionary item => item,
                _ => throw new PdfFilterException("Each DecodeParms entry must be a dictionary or null.")
            };
        }
        return result;
    }

    private static byte[] DecodeFlate(byte[] encoded, int maximumDecodedBytes)
    {
        try
        {
            using var input = new MemoryStream(encoded, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[81_920];
            while (true)
            {
                int read = zlib.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;

                EnsureWithinLimit(output.Length + read, maximumDecodedBytes);
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch (PdfFilterException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new PdfFilterException("The FlateDecode stream contains invalid zlib data.", ex);
        }
    }

    private static byte[] ReversePredictor(byte[] data, PdfDictionary? parameters, int maximumDecodedBytes)
    {
        if (parameters is null)
            return data;

        int predictor = GetInteger(parameters, PredictorName, 1);
        if (predictor == 1)
            return data;

        int colors = GetPositiveInteger(parameters, ColorsName, 1);
        int bitsPerComponent = GetPositiveInteger(parameters, BitsPerComponentName, 8);
        int columns = GetPositiveInteger(parameters, ColumnsName, 1);
        if (bitsPerComponent != 8)
            throw new PdfFilterException("Predictor reversal currently requires 8 bits per component.");

        int bytesPerPixel;
        int rowLength;
        try
        {
            bytesPerPixel = checked(colors * bitsPerComponent + 7) / 8;
            rowLength = checked(columns * colors * bitsPerComponent + 7) / 8;
        }
        catch (OverflowException ex)
        {
            throw new PdfFilterException("Predictor dimensions exceed the supported range.", ex);
        }

        if (predictor == 2)
            return ReverseTiffPredictor(data, rowLength, bytesPerPixel);
        if (predictor is >= 10 and <= 15)
            return ReversePngPredictor(data, rowLength, bytesPerPixel, maximumDecodedBytes);

        throw new PdfFilterException($"Predictor {predictor} is not defined by PDF.");
    }

    private static byte[] ReverseTiffPredictor(byte[] data, int rowLength, int bytesPerPixel)
    {
        if (rowLength == 0 || data.Length % rowLength != 0)
            throw new PdfFilterException("TIFF predictor data does not contain complete rows.");

        byte[] decoded = (byte[])data.Clone();
        for (int row = 0; row < decoded.Length; row += rowLength)
        {
            for (int column = bytesPerPixel; column < rowLength; column++)
                decoded[row + column] = unchecked((byte)(decoded[row + column] + decoded[row + column - bytesPerPixel]));
        }
        return decoded;
    }

    private static byte[] ReversePngPredictor(
        byte[] data,
        int rowLength,
        int bytesPerPixel,
        int maximumDecodedBytes)
    {
        int encodedRowLength = checked(rowLength + 1);
        if (data.Length % encodedRowLength != 0)
            throw new PdfFilterException("PNG predictor data does not contain complete rows.");

        int rowCount = data.Length / encodedRowLength;
        int decodedLength = checked(rowCount * rowLength);
        EnsureWithinLimit(decodedLength, maximumDecodedBytes);
        var decoded = new byte[decodedLength];

        for (int row = 0; row < rowCount; row++)
        {
            int inputStart = row * encodedRowLength;
            int outputStart = row * rowLength;
            byte filter = data[inputStart];
            if (filter > 4)
                throw new PdfFilterException($"PNG predictor row {row} uses unknown filter {filter}.");

            for (int column = 0; column < rowLength; column++)
            {
                byte raw = data[inputStart + 1 + column];
                byte left = column >= bytesPerPixel ? decoded[outputStart + column - bytesPerPixel] : (byte)0;
                byte above = row > 0 ? decoded[outputStart - rowLength + column] : (byte)0;
                byte upperLeft = row > 0 && column >= bytesPerPixel
                    ? decoded[outputStart - rowLength + column - bytesPerPixel]
                    : (byte)0;

                decoded[outputStart + column] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + above)),
                    3 => unchecked((byte)(raw + ((left + above) / 2))),
                    4 => unchecked((byte)(raw + Paeth(left, above, upperLeft))),
                    _ => throw new InvalidOperationException()
                };
            }
        }

        return decoded;
    }

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;
        int leftDistance = Math.Abs(estimate - left);
        int aboveDistance = Math.Abs(estimate - above);
        int upperLeftDistance = Math.Abs(estimate - upperLeft);
        if (leftDistance <= aboveDistance && leftDistance <= upperLeftDistance)
            return left;
        return aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static int GetPositiveInteger(PdfDictionary dictionary, PdfName name, int defaultValue)
    {
        int value = GetInteger(dictionary, name, defaultValue);
        if (value <= 0)
            throw new PdfFilterException($"DecodeParms {name} must be greater than zero.");
        return value;
    }

    private static int GetInteger(PdfDictionary dictionary, PdfName name, int defaultValue)
    {
        if (!dictionary.TryGetValue(name, out PdfObject value))
            return defaultValue;
        if (value is not PdfInteger integer || integer.Value is < int.MinValue or > int.MaxValue)
            throw new PdfFilterException($"DecodeParms {name} must be an integer.");
        return (int)integer.Value;
    }

    private static void EnsureWithinLimit(long length, int maximumDecodedBytes)
    {
        if (length > maximumDecodedBytes)
            throw new PdfFilterException($"Decoded stream exceeds the {maximumDecodedBytes:N0}-byte safety limit.");
    }
}
