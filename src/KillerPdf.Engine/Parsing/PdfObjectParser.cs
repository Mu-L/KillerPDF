using System.Globalization;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;

namespace KillerPdf.Engine.Parsing;

/// <summary>Builds typed PDF objects from the lexical token stream.</summary>
public sealed class PdfObjectParser
{
    public const int MaximumNestingDepth = 256;

    private static readonly PdfName LengthName = new("Length"u8);

    private readonly PdfTokenizer _tokenizer;
    private readonly Func<PdfIndirectReference, long>? _streamLengthResolver;
    private readonly List<PdfToken> _lookahead = [];

    public PdfObjectParser(
        ReadOnlyMemory<byte> source,
        Func<PdfIndirectReference, long>? streamLengthResolver = null)
        : this(source, 0, streamLengthResolver)
    {
    }

    public PdfObjectParser(
        ReadOnlyMemory<byte> source,
        int startOffset,
        Func<PdfIndirectReference, long>? streamLengthResolver = null)
    {
        _tokenizer = new PdfTokenizer(source, startOffset);
        _streamLengthResolver = streamLengthResolver;
    }

    public PdfObject ParseObject() => ParseObject(0);

    /// <summary>Parses exactly one direct object and rejects any trailing non-trivia bytes.</summary>
    public PdfObject ParseSingleObject()
    {
        PdfObject value = ParseObject(0);
        PdfToken trailing = Take();
        if (trailing.Kind != PdfTokenKind.EndOfInput)
            throw Error("Unexpected data follows the PDF object", trailing.Offset);
        return value;
    }

    public PdfIndirectObject ParseIndirectObject()
    {
        PdfToken objectNumberToken = Take();
        PdfToken generationToken = Take();
        PdfToken objToken = Take();

        long objectNumber = ParseRequiredInteger(objectNumberToken, "An indirect object must begin with an object number");
        long generation = ParseRequiredInteger(generationToken, "An indirect object must include a generation number");
        RequireKeyword(objToken, "obj", "An indirect object header must end with the obj keyword");
        ValidateReference(objectNumber, generation, objectNumberToken.Offset);

        PdfObject value = ParseObject(0);
        PdfToken endToken = Take();
        if (IsKeyword(endToken, "stream"))
        {
            if (value is not PdfDictionary dictionary)
                throw Error("A stream must be preceded by a dictionary", endToken.Offset);

            value = ParseStream(dictionary, endToken.Offset);
            endToken = Take();
        }

        RequireKeyword(endToken, "endobj", "An indirect object must end with the endobj keyword");

        return new PdfIndirectObject((int)objectNumber, (int)generation, value, objectNumberToken.Offset);
    }

    private PdfObject ParseObject(int depth)
    {
        if (depth >= MaximumNestingDepth)
            throw Error("The PDF object nesting limit was exceeded", Peek().Offset);

        PdfToken token = Take();
        return token.Kind switch
        {
            PdfTokenKind.Null => PdfNull.Instance,
            PdfTokenKind.Boolean => new PdfBoolean(token.Value.Span.SequenceEqual("true"u8)),
            PdfTokenKind.Integer => ParseIntegerOrReference(token),
            PdfTokenKind.Real => ParseReal(token),
            PdfTokenKind.Name => new PdfName(token.Value.Span),
            PdfTokenKind.LiteralString => new PdfString(token.Value.Span, PdfStringForm.Literal),
            PdfTokenKind.HexString => new PdfString(token.Value.Span, PdfStringForm.Hexadecimal),
            PdfTokenKind.ArrayStart => ParseArray(depth + 1, token.Offset),
            PdfTokenKind.DictionaryStart => ParseDictionary(depth + 1, token.Offset),
            PdfTokenKind.EndOfInput => throw Error("Expected a PDF object but reached the end of input", token.Offset),
            _ => throw Error($"Token {token.Kind} cannot begin a PDF object", token.Offset)
        };
    }

    private PdfObject ParseIntegerOrReference(PdfToken first)
    {
        long value = ParseInteger(first);
        if (Peek().Kind != PdfTokenKind.Integer
            || Peek(1).Kind != PdfTokenKind.Keyword
            || !Peek(1).Value.Span.SequenceEqual("R"u8))
            return new PdfInteger(value);

        PdfToken generationToken = Take();
        Take(); // R
        long generation = ParseInteger(generationToken);
        ValidateReference(value, generation, first.Offset);
        return new PdfIndirectReference((int)value, (int)generation);
    }

    private PdfReal ParseReal(PdfToken token)
    {
        if (!double.TryParse(token.Value.Span, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                             CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value))
            throw Error("The real number is outside the supported finite range", token.Offset);

        return new PdfReal(value);
    }

    private PdfArray ParseArray(int depth, int start)
    {
        var items = new List<PdfObject>();
        while (Peek().Kind != PdfTokenKind.ArrayEnd)
        {
            if (Peek().Kind == PdfTokenKind.EndOfInput)
                throw Error("Unterminated PDF array", start);
            items.Add(ParseObject(depth));
        }

        Take();
        return new PdfArray(items);
    }

    private PdfDictionary ParseDictionary(int depth, int start)
    {
        var entries = new Dictionary<PdfName, PdfObject>();
        while (Peek().Kind != PdfTokenKind.DictionaryEnd)
        {
            PdfToken keyToken = Take();
            if (keyToken.Kind == PdfTokenKind.EndOfInput)
                throw Error("Unterminated PDF dictionary", start);
            if (keyToken.Kind != PdfTokenKind.Name)
                throw Error("A PDF dictionary key must be a name", keyToken.Offset);

            var key = new PdfName(keyToken.Value.Span);
            if (entries.ContainsKey(key))
                throw Error($"The PDF dictionary contains the duplicate key {key}", keyToken.Offset);

            entries.Add(key, ParseObject(depth));
        }

        Take();
        return new PdfDictionary(entries);
    }

    private PdfStream ParseStream(PdfDictionary dictionary, int streamKeywordOffset)
    {
        if (_lookahead.Count != 0)
            throw Error("Internal parser lookahead crossed a stream boundary", streamKeywordOffset);

        ConsumeStreamOpeningLineEnding(streamKeywordOffset);
        int dataOffset = _tokenizer.Position;
        int length = ResolveStreamLength(dictionary, streamKeywordOffset);
        if (_tokenizer.RemainingByteCount < length)
            throw Error("The stream payload is shorter than its Length entry", dataOffset);

        ReadOnlyMemory<byte> encodedData = _tokenizer.ReadRawBytes(length);
        ConsumeStreamClosingLineEnding(dataOffset + length);

        PdfToken endStream = Take();
        RequireKeyword(endStream, "endstream", "A stream payload must end with the endstream keyword");
        return new PdfStream(dictionary, encodedData.Span);
    }

    private int ResolveStreamLength(PdfDictionary dictionary, int offset)
    {
        if (!dictionary.TryGetValue(LengthName, out PdfObject lengthObject))
            throw Error("A stream dictionary must contain a Length entry", offset);

        long length = lengthObject switch
        {
            PdfInteger integer => integer.Value,
            PdfIndirectReference reference when _streamLengthResolver is not null =>
                _streamLengthResolver(reference),
            PdfIndirectReference => throw Error(
                "The stream Length is indirect and no length resolver was supplied", offset),
            _ => throw Error("A stream Length must be an integer or an indirect reference", offset)
        };

        if (length is < 0 or > int.MaxValue)
            throw Error("A stream Length must be between 0 and 2,147,483,647 bytes", offset);
        return (int)length;
    }

    private void ConsumeStreamOpeningLineEnding(int offset)
    {
        if (!_tokenizer.TryReadRawByte(out byte first))
            throw Error("The stream keyword must be followed by a line ending", offset);

        if (first == (byte)'\n')
            return;
        if (first == (byte)'\r')
        {
            if (_tokenizer.TryPeekRawByte(out byte second) && second == (byte)'\n')
                _tokenizer.TryReadRawByte(out _);
            return;
        }

        throw Error("The stream keyword must be followed by CR, LF, or CRLF", offset);
    }

    private void ConsumeStreamClosingLineEnding(int offset)
    {
        if (!_tokenizer.TryPeekRawByte(out byte first))
            throw Error("The stream payload must be followed by endstream", offset);
        if (first == (byte)'\n')
        {
            _tokenizer.TryReadRawByte(out _);
            return;
        }
        if (first == (byte)'\r')
        {
            _tokenizer.TryReadRawByte(out _);
            // CR, LF, and CRLF are all PDF line endings here. If this is CRLF, consume the LF.
            if (_tokenizer.TryPeekRawByte(out byte second) && second == (byte)'\n')
                _tokenizer.TryReadRawByte(out _);
        }
        // qpdf and other widely used producers can include the closing EOL in /Length. In that
        // case the tokenizer is already positioned at endstream; its exact keyword is still
        // required immediately below, so accepting the omitted separator is unambiguous.
    }

    private static long ParseRequiredInteger(PdfToken token, string message)
    {
        if (token.Kind != PdfTokenKind.Integer)
            throw Error(message, token.Offset);
        return ParseInteger(token);
    }

    private static long ParseInteger(PdfToken token)
    {
        if (!long.TryParse(token.Value.Span, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            throw Error("The integer is outside the supported 64-bit range", token.Offset);
        return value;
    }

    private static void ValidateReference(long objectNumber, long generation, int offset)
    {
        if (objectNumber is < 0 or > int.MaxValue)
            throw Error("A PDF object number must be between 0 and 2,147,483,647", offset);
        if (generation is < 0 or > 65_535)
            throw Error("A PDF generation number must be between 0 and 65,535", offset);
    }

    private static void RequireKeyword(PdfToken token, string keyword, string message)
    {
        if (!IsKeyword(token, keyword))
            throw Error(message, token.Offset);
    }

    private static bool IsKeyword(PdfToken token, string keyword) =>
        token.Kind == PdfTokenKind.Keyword
        && token.Value.Span.SequenceEqual(System.Text.Encoding.ASCII.GetBytes(keyword));

    private PdfToken Peek(int distance = 0)
    {
        while (_lookahead.Count <= distance)
            _lookahead.Add(_tokenizer.Read());
        return _lookahead[distance];
    }

    private PdfToken Take()
    {
        PdfToken token = Peek();
        _lookahead.RemoveAt(0);
        return token;
    }

    private static PdfSyntaxException Error(string message, int offset) => new(message, offset);

}
