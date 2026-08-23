using System.Globalization;
using KillerPdf.Engine.CrossReference;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;

namespace KillerPdf.Engine.Writing;

/// <summary>Produces a deterministic full rewrite of the current merged document revision.</summary>
public static class PdfDocumentWriter
{
    private static readonly PdfName TypeName = new("Type"u8);
    private static readonly PdfName XRefName = new("XRef"u8);
    private static readonly PdfName ObjStmName = new("ObjStm"u8);
    private static readonly PdfName SizeName = new("Size"u8);
    private static readonly PdfName RootName = new("Root"u8);
    private static readonly PdfName InfoName = new("Info"u8);
    private static readonly PdfName IdName = new("ID"u8);
    private static readonly PdfName EncryptName = new("Encrypt"u8);

    public static byte[] Write(PdfDocument document, PdfDocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new PdfDocumentWriteOptions();
        PdfVersion outputVersion = options.TargetVersion ?? document.Header.Version;
        if (outputVersion.CompareTo(document.Header.Version) < 0)
            throw new NotSupportedException("A full rewrite cannot downgrade the source PDF version without feature analysis.");
        if (document.CrossReferences.TryGetTrailerValue(EncryptName, out _))
            throw new NotSupportedException("Encrypted PDF rewriting requires the encryption writer milestone.");
        if (!document.CrossReferences.TryGetTrailerValue(RootName, out PdfObject root))
            throw new InvalidOperationException("A full rewrite requires a trailer /Root reference.");

        List<WritableObject> objects = ReadCurrentObjects(document);
        int maximumObjectNumber = objects.Select(item => item.ObjectNumber).DefaultIfEmpty(0).Max();
        if (maximumObjectNumber == int.MaxValue)
            throw new NotSupportedException("The PDF object-number range is too large to rewrite.");

        using var output = new MemoryStream();
        WriteAscii(output, $"%PDF-{outputVersion}\n");
        output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        var offsets = new List<WrittenOffset>(objects.Count);
        foreach (WritableObject item in objects)
        {
            int offset = checked((int)output.Position);
            offsets.Add(new WrittenOffset(item.ObjectNumber, item.Generation, offset));
            PdfObjectWriter.Write(
                output,
                new PdfIndirectObject(item.ObjectNumber, item.Generation, item.Value, offset));
        }

        int xrefOffset = checked((int)output.Position);
        output.Write("xref\n0 1\n0000000000 65535 f \n"u8);
        foreach (WrittenOffset item in offsets)
        {
            WriteAscii(output, $"{item.ObjectNumber} 1\n{item.Offset:0000000000} {item.Generation:00000} n \n");
        }

        output.Write("trailer\n"u8);
        PdfObjectWriter.Write(output, BuildTrailer(document, maximumObjectNumber + 1, root, options));
        output.Write("\nstartxref\n"u8);
        WriteAscii(output, xrefOffset.ToString(CultureInfo.InvariantCulture));
        output.Write("\n%%EOF\n"u8);
        return output.ToArray();
    }

    private static List<WritableObject> ReadCurrentObjects(PdfDocument document)
    {
        var result = new List<WritableObject>();
        foreach (PdfCrossReferenceEntry entry in document.CrossReferences.Values
                     .Where(entry => entry.Type is PdfCrossReferenceEntryType.InUse or PdfCrossReferenceEntryType.Compressed)
                     .OrderBy(entry => entry.ObjectNumber))
        {
            PdfObject value = document.Resolve(entry.ObjectNumber);
            if (IsStructuralStream(value))
                continue;
            int generation = entry.Type == PdfCrossReferenceEntryType.InUse ? entry.Field2 : 0;
            result.Add(new WritableObject(entry.ObjectNumber, generation, value));
        }
        return result;
    }

    private static bool IsStructuralStream(PdfObject value)
    {
        if (value is not PdfStream stream
            || !stream.Dictionary.TryGetValue(TypeName, out PdfObject type)
            || type is not PdfName name)
            return false;
        return name.Equals(XRefName) || name.Equals(ObjStmName);
    }

    private static PdfDictionary BuildTrailer(
        PdfDocument document,
        int size,
        PdfObject root,
        PdfDocumentWriteOptions options)
    {
        var entries = new List<KeyValuePair<PdfName, PdfObject>>
        {
            new(SizeName, new PdfInteger(size)),
            new(RootName, root)
        };
        if (options.MetadataPolicy == PdfMetadataPolicy.Preserve)
            AddInherited(document, entries, InfoName);
        if (options.PreserveDocumentIdentifiers)
            AddInherited(document, entries, IdName);
        return new PdfDictionary(entries);
    }

    private static void AddInherited(
        PdfDocument document,
        List<KeyValuePair<PdfName, PdfObject>> entries,
        PdfName name)
    {
        if (document.CrossReferences.TryGetTrailerValue(name, out PdfObject value))
            entries.Add(new KeyValuePair<PdfName, PdfObject>(name, value));
    }

    private static void WriteAscii(Stream output, string value)
    {
        foreach (char character in value)
            output.WriteByte(checked((byte)character));
    }

    private sealed record WritableObject(int ObjectNumber, int Generation, PdfObject Value);
    private sealed record WrittenOffset(int ObjectNumber, int Generation, int Offset);
}
