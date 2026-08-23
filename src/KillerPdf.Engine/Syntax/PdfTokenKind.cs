namespace KillerPdf.Engine.Syntax;

/// <summary>The lexical units defined by the PDF object syntax.</summary>
public enum PdfTokenKind
{
    EndOfInput,
    Integer,
    Real,
    Boolean,
    Null,
    Name,
    LiteralString,
    HexString,
    Keyword,
    ArrayStart,
    ArrayEnd,
    DictionaryStart,
    DictionaryEnd,
    BraceStart,
    BraceEnd
}
