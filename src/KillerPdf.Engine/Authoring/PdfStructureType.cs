namespace KillerPdf.Engine.Authoring;

/// <summary>Standard structure types used by tagged PDF logical structure trees.</summary>
public enum PdfStructureType
{
    Document,
    Part,
    Article,
    Section,
    Division,
    Paragraph,
    Heading,
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    List,
    ListItem,
    Label,
    ListBody,
    Table,
    TableRow,
    TableHeaderCell,
    TableDataCell,
    Span,
    Quote,
    Note,
    Reference,
    Code,
    Link,
    Figure,
    Formula,
    Form
}

internal static class PdfStructureTypeNames
{
    internal static bool UsesPdf17Namespace(PdfStructureType type) =>
        type is PdfStructureType.Article or PdfStructureType.Quote or PdfStructureType.Note
            or PdfStructureType.Reference or PdfStructureType.Code;

    internal static string Name(PdfStructureType type, bool pdfUa2) =>
        pdfUa2 && type == PdfStructureType.Note ? "FENote" : Name(type);

    internal static bool UsesPdf17Namespace(PdfStructureType type, bool pdfUa2) =>
        !(pdfUa2 && type == PdfStructureType.Note) && UsesPdf17Namespace(type);

    internal static string Name(PdfStructureType type) => type switch
    {
        PdfStructureType.Document => "Document",
        PdfStructureType.Part => "Part",
        PdfStructureType.Article => "Art",
        PdfStructureType.Section => "Sect",
        PdfStructureType.Division => "Div",
        PdfStructureType.Paragraph => "P",
        PdfStructureType.Heading => "H",
        PdfStructureType.Heading1 => "H1",
        PdfStructureType.Heading2 => "H2",
        PdfStructureType.Heading3 => "H3",
        PdfStructureType.Heading4 => "H4",
        PdfStructureType.Heading5 => "H5",
        PdfStructureType.Heading6 => "H6",
        PdfStructureType.List => "L",
        PdfStructureType.ListItem => "LI",
        PdfStructureType.Label => "Lbl",
        PdfStructureType.ListBody => "LBody",
        PdfStructureType.Table => "Table",
        PdfStructureType.TableRow => "TR",
        PdfStructureType.TableHeaderCell => "TH",
        PdfStructureType.TableDataCell => "TD",
        PdfStructureType.Span => "Span",
        PdfStructureType.Quote => "Quote",
        PdfStructureType.Note => "Note",
        PdfStructureType.Reference => "Reference",
        PdfStructureType.Code => "Code",
        PdfStructureType.Link => "Link",
        PdfStructureType.Figure => "Figure",
        PdfStructureType.Formula => "Formula",
        PdfStructureType.Form => "Form",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
