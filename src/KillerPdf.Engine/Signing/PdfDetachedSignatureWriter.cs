using System.Globalization;
using System.Text;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Signing;

/// <summary>
/// Adds an invisible approval-signature field in an incremental revision and fills its
/// PDF 2.0 byte range with detached CMS bytes supplied by the caller.
/// </summary>
public static class PdfDetachedSignatureWriter
{
    private static readonly PdfName AnnotsName = Name("Annots");
    private static readonly PdfName AcroFormName = Name("AcroForm");
    private static readonly PdfName FieldsName = Name("Fields");
    private static readonly PdfName SignatureFlagsName = Name("SigFlags");
    private static readonly PdfName StructureTreeRootName = Name("StructTreeRoot");
    private static readonly PdfName PermissionsName = Name("Perms");
    private static readonly PdfName FieldNameName = Name("T");
    private static readonly PdfName KidsName = Name("Kids");
    private const long RangeSentinel1 = 1_111_111_111;
    private const long RangeSentinel2 = 2_222_222_222;
    private const long RangeSentinel3 = 3_333_333_333;
    private const long RangeSentinel4 = 4_444_444_444;
    private const int MaximumReservedSignatureSize = 1_048_576;

    public static byte[] Sign(
        PdfDocument document,
        Func<ReadOnlyMemory<byte>, byte[]> createDetachedCms,
        PdfSignatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(createDetachedCms);
        options ??= new PdfSignatureOptions();
        ValidateOptions(options);

        PdfPageTree tree = PdfPageTree.Read(document);
        if (options.PageIndex < 0 || options.PageIndex >= tree.Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(options), "The signature page index is outside the document.");
        if (tree.Catalog.ContainsKey(StructureTreeRootName))
            throw new NotSupportedException(
                "Signing a tagged PDF requires an accessible signature-field structure association, which is not yet supported.");
        if (tree.Catalog.ContainsKey(PermissionsName))
            throw new NotSupportedException(
                "Signing a document with certification permissions requires DocMDP permission analysis, which is not yet supported.");

        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference signatureReference = update.ReserveObject();
        PdfIndirectReference fieldReference = update.ReserveObject();
        PdfPageTreeEntry page = tree.Pages[options.PageIndex];

        update.SetObject(signatureReference, BuildSignatureDictionary(options));
        update.SetObject(fieldReference, Dictionary(
            ("Type", Name("Annot")),
            ("Subtype", Name("Widget")),
            ("FT", Name("Sig")),
            ("T", UnicodeString(options.FieldName)),
            ("V", signatureReference),
            ("Rect", new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(0), new PdfInteger(0)])),
            ("F", new PdfInteger(4)),
            ("P", page.Reference)));

        PdfArray annotations = page.Dictionary.TryGetValue(AnnotsName, out PdfObject? annotationsValue)
            ? ResolveArray(document, annotationsValue, "The signature page /Annots value")
            : new PdfArray([]);
        update.ReplaceObject(page.Reference.ObjectNumber,
            ReplaceMany(page.Dictionary, new Dictionary<PdfName, PdfObject>
            {
                [AnnotsName] = new PdfArray(annotations.Append(fieldReference))
            }));

        AddSignatureField(document, tree, update, fieldReference, options.FieldName);
        byte[] prepared = update.Build();
        FillSignature(prepared, options.ReservedSignatureSize, createDetachedCms);
        return prepared;
    }

    private static PdfDictionary BuildSignatureDictionary(PdfSignatureOptions options)
    {
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Sig")),
            ("Filter", Name("Adobe.PPKLite")),
            ("SubFilter", Name("ETSI.CAdES.detached")),
            ("ByteRange", new PdfArray([
                new PdfInteger(RangeSentinel1), new PdfInteger(RangeSentinel2),
                new PdfInteger(RangeSentinel3), new PdfInteger(RangeSentinel4)])),
            ("Contents", new PdfString(
                new byte[options.ReservedSignatureSize], PdfStringForm.Hexadecimal))
        };
        AddOptionalText(entries, "Name", options.SignerName);
        AddOptionalText(entries, "Reason", options.Reason);
        AddOptionalText(entries, "Location", options.Location);
        AddOptionalText(entries, "ContactInfo", options.ContactInformation);
        if (options.SigningTime.HasValue)
            entries.Add(("M", Latin1String(PdfDate(options.SigningTime.Value))));
        return Dictionary(entries.ToArray());
    }

    private static void AddSignatureField(
        PdfDocument document,
        PdfPageTree tree,
        PdfIncrementalUpdateBuilder update,
        PdfIndirectReference fieldReference,
        string fieldName)
    {
        PdfDictionary form;
        PdfIndirectReference? formReference = null;
        if (tree.Catalog.TryGetValue(AcroFormName, out PdfObject? formValue))
        {
            formReference = formValue as PdfIndirectReference;
            form = ResolveDictionary(document, formValue, "The catalog /AcroForm value");
        }
        else
        {
            form = new PdfDictionary([]);
        }

        PdfArray fields = form.TryGetValue(FieldsName, out PdfObject? fieldsValue)
            ? ResolveArray(document, fieldsValue, "The AcroForm /Fields value")
            : new PdfArray([]);
        EnsureFieldNameAvailable(document, fields, fieldName);
        long signatureFlags = 0;
        if (form.TryGetValue(SignatureFlagsName, out PdfObject? flagsValue))
        {
            signatureFlags = flagsValue is PdfInteger integer && integer.Value >= 0
                ? integer.Value
                : throw new InvalidOperationException("The AcroForm /SigFlags value is not a non-negative integer.");
        }
        PdfDictionary replacement = ReplaceMany(form, new Dictionary<PdfName, PdfObject>
        {
            [FieldsName] = new PdfArray(fields.Append(fieldReference)),
            [SignatureFlagsName] = new PdfInteger(signatureFlags | 3)
        });
        if (formReference is not null)
        {
            update.ReplaceObject(formReference.ObjectNumber, replacement);
            return;
        }
        update.ReplaceObject(tree.CatalogReference.ObjectNumber,
            ReplaceMany(tree.Catalog, new Dictionary<PdfName, PdfObject>
            {
                [AcroFormName] = replacement
            }));
    }

    private static void EnsureFieldNameAvailable(
        PdfDocument document, PdfArray fields, string fieldName)
    {
        var active = new HashSet<int>();
        var visited = new HashSet<int>();
        foreach (PdfObject value in fields) Visit(value, null, 0);

        void Visit(PdfObject value, string? parentName, int depth)
        {
            if (depth >= PdfObjectWriter.MaximumNestingDepth)
                throw new InvalidOperationException("The AcroForm field tree is too deeply nested.");
            PdfIndirectReference? reference = value as PdfIndirectReference;
            if (reference is not null)
            {
                if (!active.Add(reference.ObjectNumber))
                    throw new InvalidOperationException("The AcroForm field tree contains a cycle.");
                if (!visited.Add(reference.ObjectNumber))
                    throw new InvalidOperationException(
                        "The AcroForm field tree references the same field more than once.");
            }
            PdfDictionary field = ResolveDictionary(document, value, "An AcroForm field");
            string? fullName = parentName;
            if (field.TryGetValue(FieldNameName, out PdfObject? nameValue))
            {
                string partialName = nameValue is PdfString name
                    ? DecodeString(name)
                    : throw new InvalidOperationException("An AcroForm field /T value is not a string.");
                fullName = string.IsNullOrEmpty(parentName)
                    ? partialName : $"{parentName}.{partialName}";
            }
            if (fullName == fieldName)
                throw new InvalidOperationException($"The AcroForm already contains a field named '{fieldName}'.");
            if (field.TryGetValue(KidsName, out PdfObject? kidsValue))
            {
                PdfArray kids = ResolveArray(document, kidsValue, "An AcroForm field /Kids value");
                foreach (PdfObject kid in kids) Visit(kid, fullName, depth + 1);
            }
            if (reference is not null) active.Remove(reference.ObjectNumber);
        }
    }

    private static void FillSignature(
        byte[] prepared,
        int reservedSize,
        Func<ReadOnlyMemory<byte>, byte[]> createDetachedCms)
    {
        byte[] rangeMarker = Encoding.ASCII.GetBytes(
            $"/ByteRange [{RangeSentinel1} {RangeSentinel2} {RangeSentinel3} {RangeSentinel4}]");
        int rangeMarkerIndex = prepared.AsSpan().IndexOf(rangeMarker);
        if (rangeMarkerIndex < 0)
            throw new InvalidOperationException("The signature byte-range placeholder was not found.");
        ReadOnlySpan<byte> contentsMarker = "/Contents <"u8;
        int relativeContentsIndex = prepared.AsSpan(rangeMarkerIndex).IndexOf(contentsMarker);
        if (relativeContentsIndex < 0)
            throw new InvalidOperationException("The signature contents placeholder was not found.");
        int contentsValueStart = rangeMarkerIndex + relativeContentsIndex
            + contentsMarker.Length - 1;
        int contentsHexStart = contentsValueStart + 1;
        int contentsHexEnd = checked(contentsHexStart + reservedSize * 2);
        if (contentsHexEnd >= prepared.Length || prepared[contentsHexEnd] != (byte)'>')
            throw new InvalidOperationException("The signature contents placeholder has an unexpected length.");
        int secondRangeStart = contentsHexEnd + 1;
        long[] ranges = [0, contentsValueStart, secondRangeStart, prepared.Length - secondRangeStart];
        long[] sentinels = [RangeSentinel1, RangeSentinel2, RangeSentinel3, RangeSentinel4];
        for (int index = 0; index < ranges.Length; index++)
        {
            byte[] marker = Encoding.ASCII.GetBytes(
                sentinels[index].ToString(CultureInfo.InvariantCulture));
            int relative = prepared.AsSpan(rangeMarkerIndex, rangeMarker.Length).IndexOf(marker);
            if (relative < 0)
                throw new InvalidOperationException("A signature byte-range placeholder was not found.");
            WriteFixedDecimal(prepared.AsSpan(rangeMarkerIndex + relative, 10), ranges[index]);
        }

        byte[] signedBytes = new byte[checked((int)(ranges[1] + ranges[3]))];
        prepared.AsSpan(0, checked((int)ranges[1])).CopyTo(signedBytes);
        prepared.AsSpan(checked((int)ranges[2]), checked((int)ranges[3]))
            .CopyTo(signedBytes.AsSpan(checked((int)ranges[1])));
        byte[] cms = createDetachedCms(signedBytes)
            ?? throw new InvalidOperationException("The detached CMS signer returned null.");
        if (cms.Length == 0)
            throw new InvalidOperationException("The detached CMS signer returned an empty signature.");
        if (cms.Length > reservedSize)
            throw new InvalidOperationException(
                $"The detached CMS signature requires {cms.Length} bytes but only {reservedSize} were reserved.");
        WriteHex(prepared.AsSpan(contentsHexStart, reservedSize * 2), cms);
    }

    private static void WriteFixedDecimal(Span<byte> destination, long value)
    {
        string text = value.ToString("D10", CultureInfo.InvariantCulture);
        if (text.Length != destination.Length)
            throw new NotSupportedException("Signed PDF byte offsets cannot exceed ten digits.");
        Encoding.ASCII.GetBytes(text, destination);
    }

    private static void WriteHex(Span<byte> destination, ReadOnlySpan<byte> value)
    {
        const string digits = "0123456789ABCDEF";
        int offset = 0;
        foreach (byte item in value)
        {
            destination[offset++] = (byte)digits[item >> 4];
            destination[offset++] = (byte)digits[item & 0x0F];
        }
        destination[offset..].Fill((byte)'0');
    }

    private static void ValidateOptions(PdfSignatureOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FieldName) || options.FieldName.Contains('.'))
            throw new ArgumentException(
                "A signature field name must be non-empty and cannot contain a period.", nameof(options));
        if (options.ReservedSignatureSize is <= 0 or > MaximumReservedSignatureSize)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"The reserved signature size must be between 1 and {MaximumReservedSignatureSize} bytes.");
    }

    private static PdfDictionary ResolveDictionary(
        PdfDocument document, PdfObject value, string description)
    {
        PdfObject resolved = value is PdfIndirectReference reference
            ? document.Resolve(reference) : value;
        return resolved as PdfDictionary
            ?? throw new InvalidOperationException($"{description} is not a dictionary.");
    }

    private static PdfArray ResolveArray(
        PdfDocument document, PdfObject value, string description)
    {
        PdfObject resolved = value is PdfIndirectReference reference
            ? document.Resolve(reference) : value;
        return resolved as PdfArray
            ?? throw new InvalidOperationException($"{description} is not an array.");
    }

    private static PdfDictionary ReplaceMany(
        PdfDictionary source, IReadOnlyDictionary<PdfName, PdfObject> replacements) =>
        new(source.Where(entry => !replacements.ContainsKey(entry.Key)).Concat(replacements));

    private static PdfDictionary Dictionary(params (string Name, PdfObject Value)[] entries) =>
        new(entries.Select(entry =>
            new KeyValuePair<PdfName, PdfObject>(Name(entry.Name), entry.Value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
    private static PdfString Latin1String(string value) =>
        new(Encoding.Latin1.GetBytes(value), PdfStringForm.Literal);
    private static PdfString UnicodeString(string value)
    {
        byte[] text = Encoding.BigEndianUnicode.GetBytes(value);
        byte[] bytes = new byte[text.Length + 2];
        bytes[0] = 0xFE;
        bytes[1] = 0xFF;
        text.CopyTo(bytes, 2);
        return new PdfString(bytes, PdfStringForm.Hexadecimal);
    }
    private static string DecodeString(PdfString value)
    {
        ReadOnlySpan<byte> bytes = value.Bytes.Span;
        return bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
            ? Encoding.BigEndianUnicode.GetString(bytes[2..])
            : Encoding.Latin1.GetString(bytes);
    }
    private static string PdfDate(DateTimeOffset value)
    {
        TimeSpan offset = value.Offset;
        char sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();
        return $"D:{value:yyyyMMddHHmmss}{sign}{offset.Hours:00}'{offset.Minutes:00}'";
    }
    private static void AddOptionalText(
        ICollection<(string Name, PdfObject Value)> entries, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) entries.Add((name, UnicodeString(value)));
    }
}
