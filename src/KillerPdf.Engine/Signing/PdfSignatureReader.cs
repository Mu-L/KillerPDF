using System.Text;
using System.Formats.Asn1;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;

namespace KillerPdf.Engine.Signing;

/// <summary>Inspects signature fields and structurally validates their signed byte ranges.</summary>
public static class PdfSignatureReader
{
    private static readonly PdfName AcroFormName = Name("AcroForm");
    private static readonly PdfName FieldsName = Name("Fields");
    private static readonly PdfName FieldTypeName = Name("FT");
    private static readonly PdfName FieldNameName = Name("T");
    private static readonly PdfName KidsName = Name("Kids");
    private static readonly PdfName ValueName = Name("V");

    public static IReadOnlyList<PdfSignatureInfo> Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PdfDictionary catalog = ResolveDictionary(document,
            document.Trailer.TryGetValue(Name("Root"), out PdfObject? root)
                ? root : throw new InvalidOperationException("The PDF trailer has no /Root value."),
            "The trailer /Root value");
        if (!catalog.TryGetValue(AcroFormName, out PdfObject? formValue)) return [];
        PdfDictionary form = ResolveDictionary(document, formValue, "The catalog /AcroForm value");
        if (!form.TryGetValue(FieldsName, out PdfObject? fieldsValue)) return [];
        PdfArray fields = ResolveArray(document, fieldsValue, "The AcroForm /Fields value");
        int? certificationObject = CertificationObjectNumber(document, catalog);
        var result = new List<PdfSignatureInfo>();
        var active = new HashSet<int>();
        var visited = new HashSet<int>();
        foreach (PdfObject field in fields) Visit(field, null, null, 0);
        return result;

        void Visit(PdfObject value, string? parentName, PdfName? inheritedType, int depth)
        {
            if (depth >= PdfObjectWriter.MaximumNestingDepth)
                throw new InvalidOperationException("The AcroForm field tree is too deeply nested.");
            PdfIndirectReference? fieldReference = value as PdfIndirectReference;
            if (fieldReference is not null)
            {
                if (!active.Add(fieldReference.ObjectNumber))
                    throw new InvalidOperationException("The AcroForm field tree contains a cycle.");
                if (!visited.Add(fieldReference.ObjectNumber))
                    throw new InvalidOperationException(
                        "The AcroForm field tree references the same field more than once.");
            }
            PdfDictionary field = ResolveDictionary(document, value, "An AcroForm field");
            PdfName? fieldType = inheritedType;
            if (field.TryGetValue(FieldTypeName, out PdfObject? typeValue))
                fieldType = typeValue as PdfName
                    ?? throw new InvalidOperationException("An AcroForm field /FT value is not a name.");
            string? fullName = parentName;
            bool definesName = false;
            if (field.TryGetValue(FieldNameName, out PdfObject? nameValue))
            {
                definesName = true;
                string partialName = nameValue is PdfString name
                    ? DecodeString(name)
                    : throw new InvalidOperationException("An AcroForm field /T value is not a string.");
                fullName = string.IsNullOrEmpty(parentName)
                    ? partialName : $"{parentName}.{partialName}";
            }
            bool hasKids = field.ContainsKey(KidsName);
            if (fieldType?.ValueAsLatin1() == "Sig"
                && (definesName || (parentName is null && !hasKids)))
                result.Add(ReadSignature(
                    document, field, fullName ?? string.Empty, certificationObject));
            if (field.TryGetValue(KidsName, out PdfObject? kidsValue))
            {
                PdfArray kids = ResolveArray(document, kidsValue, "An AcroForm field /Kids value");
                foreach (PdfObject kid in kids) Visit(kid, fullName, fieldType, depth + 1);
            }
            if (fieldReference is not null) active.Remove(fieldReference.ObjectNumber);
        }
    }

    public static byte[] GetSignedContent(PdfDocument document, PdfSignatureInfo signature)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signature);
        if (!signature.HasValidByteRange || signature.ByteRange is not { Count: 4 } range)
            throw new InvalidOperationException("The signature does not have a valid byte range.");
        int firstStart = checked((int)range[0]);
        int firstLength = checked((int)range[1]);
        int secondStart = checked((int)range[2]);
        int secondLength = checked((int)range[3]);
        byte[] content = new byte[checked(firstLength + secondLength)];
        document.Source.Span.Slice(firstStart, firstLength).CopyTo(content);
        document.Source.Span.Slice(secondStart, secondLength).CopyTo(content.AsSpan(firstLength));
        return content;
    }

    private static PdfSignatureInfo ReadSignature(
        PdfDocument document, PdfDictionary field, string fieldName, int? certificationObject)
    {
        if (!field.TryGetValue(ValueName, out PdfObject? value))
            return new PdfSignatureInfo { FieldName = fieldName };
        PdfObject resolvedValue = value is PdfIndirectReference resolvedReference
            ? document.Resolve(resolvedReference) : value;
        if (resolvedValue is PdfNull)
            return new PdfSignatureInfo { FieldName = fieldName };
        PdfIndirectReference? signatureReference = value as PdfIndirectReference;
        PdfDictionary signature = resolvedValue as PdfDictionary
            ?? throw new InvalidOperationException(
                $"The signature field '{fieldName}' /V value is not a dictionary.");
        string? filter = OptionalName(signature, "Filter");
        string? subFilter = OptionalName(signature, "SubFilter");
        long[]? range = null;
        bool valid = false;
        bool coversWholeDocument = false;
        if (signature.TryGetValue(Name("ByteRange"), out PdfObject? rangeValue))
        {
            PdfArray rangeArray = ResolveArray(document, rangeValue,
                $"The signature field '{fieldName}' /ByteRange value");
            if (rangeArray.Count == 4 && rangeArray.All(item => item is PdfInteger))
            {
                range = rangeArray.Cast<PdfInteger>().Select(item => item.Value).ToArray();
                long length = document.Source.Length;
                valid = range.All(item => item >= 0)
                    && range[0] == 0
                    && range[0] <= length && range[1] <= length - range[0]
                    && range[2] > range[0] + range[1] && range[2] <= length
                    && range[3] <= length - range[2];
                coversWholeDocument = valid && range[2] + range[3] == length;
            }
        }
        ReadOnlyMemory<byte> contents = signature.TryGetValue(Name("Contents"), out PdfObject? contentsValue)
            && contentsValue is PdfString contentsString ? contentsString.Bytes : ReadOnlyMemory<byte>.Empty;
        (ReadOnlyMemory<byte> cms, bool validCms) = ReadCms(contents);
        SignatureTransforms transforms = ReadTransforms(document, signature);
        return new PdfSignatureInfo
        {
            FieldName = fieldName,
            IsSigned = true,
            IsCertificationSignature = signatureReference?.ObjectNumber == certificationObject,
            CertificationPermission = transforms.CertificationPermission,
            FieldLockAction = transforms.FieldLockAction,
            FieldLockPermission = transforms.FieldLockPermission,
            LockedFields = transforms.LockedFields,
            Filter = filter,
            SubFilter = subFilter,
            ByteRange = range,
            Contents = contents,
            Cms = cms,
            HasValidCmsEncoding = validCms,
            HasValidByteRange = valid,
            CoversWholeDocument = coversWholeDocument
        };
    }

    private static SignatureTransforms ReadTransforms(
        PdfDocument document, PdfDictionary signature)
    {
        if (!signature.TryGetValue(Name("Reference"), out PdfObject? value)) return default;
        PdfArray references = ResolveArray(document, value, "The signature /Reference value");
        PdfSignatureCertificationPermission? certification = null;
        PdfSignatureLockAction? lockAction = null;
        PdfSignatureLockPermission? lockPermission = null;
        IReadOnlyList<string>? lockedFields = null;
        foreach (PdfObject referenceValue in references)
        {
            PdfDictionary reference = ResolveDictionary(
                document, referenceValue, "A signature reference");
            if (!reference.TryGetValue(Name("TransformMethod"), out PdfObject? methodValue)
                || methodValue is not PdfName method)
                throw new InvalidOperationException(
                    "A signature reference has no valid /TransformMethod.");
            if (!reference.TryGetValue(Name("TransformParams"), out PdfObject? parametersValue))
                throw new InvalidOperationException(
                    "A signature reference has no /TransformParams value.");
            PdfDictionary parameters = ResolveDictionary(
                document, parametersValue, "The signature transform parameters");
            if (method.ValueAsLatin1() == "DocMDP")
            {
                if (certification.HasValue)
                    throw new InvalidOperationException(
                        "The signature contains more than one DocMDP transform.");
                long permission = parameters.TryGetValue(Name("P"), out PdfObject? permissionValue)
                    && permissionValue is PdfInteger integer ? integer.Value : 2;
                if (permission is < 1 or > 3)
                    throw new InvalidOperationException(
                        "The DocMDP permission is not an integer from 1 through 3.");
                certification = (PdfSignatureCertificationPermission)permission;
            }
            else if (method.ValueAsLatin1() == "FieldMDP")
            {
                if (lockAction.HasValue)
                    throw new InvalidOperationException(
                        "The signature contains more than one FieldMDP transform.");
                if (!parameters.TryGetValue(Name("Action"), out PdfObject? actionValue)
                    || actionValue is not PdfName action
                    || !Enum.TryParse(action.ValueAsLatin1(), out PdfSignatureLockAction parsedAction))
                    throw new InvalidOperationException(
                        "The FieldMDP transform has no valid lock action.");
                lockAction = parsedAction;
                if (parameters.TryGetValue(Name("P"), out PdfObject? lockPermissionValue))
                {
                    if (lockPermissionValue is not PdfInteger permission
                        || permission.Value is < 1 or > 3)
                        throw new InvalidOperationException(
                            "The FieldMDP permission is not an integer from 1 through 3.");
                    lockPermission = (PdfSignatureLockPermission)permission.Value;
                }
                if (parameters.TryGetValue(Name("Fields"), out PdfObject? fieldsValue))
                {
                    PdfArray fields = ResolveArray(
                        document, fieldsValue, "The FieldMDP /Fields value");
                    if (fields.Any(item => item is not PdfString))
                        throw new InvalidOperationException(
                            "The FieldMDP /Fields array contains a non-string value.");
                    lockedFields = fields.Cast<PdfString>().Select(DecodeString).ToArray();
                }
            }
        }
        return new SignatureTransforms(
            certification, lockAction, lockPermission, lockedFields);
    }

    private static (ReadOnlyMemory<byte> Cms, bool IsValid) ReadCms(ReadOnlyMemory<byte> contents)
    {
        if (contents.IsEmpty) return (ReadOnlyMemory<byte>.Empty, false);
        try
        {
            var reader = new AsnReader(contents, AsnEncodingRules.BER);
            ReadOnlyMemory<byte> cms = reader.ReadEncodedValue();
            ReadOnlySpan<byte> padding = contents.Span[cms.Length..];
            return padding.IndexOfAnyExcept((byte)0) < 0
                ? (cms, true) : (ReadOnlyMemory<byte>.Empty, false);
        }
        catch (AsnContentException)
        {
            return (ReadOnlyMemory<byte>.Empty, false);
        }
    }

    private static int? CertificationObjectNumber(PdfDocument document, PdfDictionary catalog)
    {
        if (!catalog.TryGetValue(Name("Perms"), out PdfObject? permissionsValue)) return null;
        PdfDictionary permissions = ResolveDictionary(document, permissionsValue,
            "The catalog /Perms value");
        if (!permissions.TryGetValue(Name("DocMDP"), out PdfObject? docMdpValue)) return null;
        return docMdpValue is PdfIndirectReference reference
            ? reference.ObjectNumber
            : throw new InvalidOperationException("The catalog /Perms /DocMDP value is not indirect.");
    }

    private static string? OptionalName(PdfDictionary dictionary, string key)
    {
        if (!dictionary.TryGetValue(Name(key), out PdfObject? value)) return null;
        return value is PdfName name ? name.ValueAsLatin1()
            : throw new InvalidOperationException($"The signature /{key} value is not a name.");
    }

    private static PdfDictionary ResolveDictionary(
        PdfDocument document, PdfObject value, string description)
    {
        PdfObject resolved = value is PdfIndirectReference reference
            ? document.Resolve(reference) : value;
        return resolved as PdfDictionary
            ?? throw new InvalidOperationException($"{description} is not a dictionary.");
    }

    private static PdfArray ResolveArray(PdfDocument document, PdfObject value, string description)
    {
        PdfObject resolved = value is PdfIndirectReference reference
            ? document.Resolve(reference) : value;
        return resolved as PdfArray
            ?? throw new InvalidOperationException($"{description} is not an array.");
    }

    private static string DecodeString(PdfString value)
    {
        ReadOnlySpan<byte> bytes = value.Bytes.Span;
        return bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
            ? Encoding.BigEndianUnicode.GetString(bytes[2..])
            : Encoding.Latin1.GetString(bytes);
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));

    private readonly record struct SignatureTransforms(
        PdfSignatureCertificationPermission? CertificationPermission,
        PdfSignatureLockAction? FieldLockAction,
        PdfSignatureLockPermission? FieldLockPermission,
        IReadOnlyList<string>? LockedFields);
}
