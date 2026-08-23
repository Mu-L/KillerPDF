namespace KillerPdf.Engine.Objects;

public sealed class PdfNull : PdfObject
{
    private PdfNull() { }

    public static PdfNull Instance { get; } = new();
}

public sealed class PdfBoolean(bool value) : PdfObject
{
    public bool Value { get; } = value;
}

public sealed class PdfInteger(long value) : PdfObject
{
    public long Value { get; } = value;
}

public sealed class PdfReal : PdfObject
{
    public PdfReal(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "A PDF real number must be finite.");

        Value = value;
    }

    public double Value { get; }
}
