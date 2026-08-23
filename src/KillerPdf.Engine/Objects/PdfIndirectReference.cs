namespace KillerPdf.Engine.Objects;

public sealed class PdfIndirectReference : PdfObject
{
    public PdfIndirectReference(int objectNumber, int generation)
    {
        if (objectNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(objectNumber));
        if (generation is < 0 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(generation));

        ObjectNumber = objectNumber;
        Generation = generation;
    }

    public int ObjectNumber { get; }
    public int Generation { get; }
}

/// <summary>A numbered top-level object and the source offset at which its declaration begins.</summary>
public sealed class PdfIndirectObject
{
    public PdfIndirectObject(int objectNumber, int generation, PdfObject value, int offset)
    {
        if (objectNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(objectNumber));
        if (generation is < 0 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        ObjectNumber = objectNumber;
        Generation = generation;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Offset = offset;
    }

    public int ObjectNumber { get; }
    public int Generation { get; }
    public PdfObject Value { get; }
    public int Offset { get; }
}
