using System.Globalization;
using System.Text;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Authoring;
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
    private static readonly PdfName DocMdpName = Name("DocMDP");
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
        ExistingFormField? existingField = FindField(document, tree, options.FieldName);
        if (existingField is null && options.FieldName.Contains('.'))
            throw new ArgumentException(
                "A new signature field name cannot contain a period.", nameof(options));
        if (existingField is null
            && (options.PageIndex < 0 || options.PageIndex >= tree.Pages.Count))
            throw new ArgumentOutOfRangeException(nameof(options),
                "The signature page index is outside the document.");
        if (existingField is null && tree.Catalog.ContainsKey(StructureTreeRootName))
            throw new NotSupportedException(
                "Signing a tagged PDF requires an accessible signature-field structure association, which is not yet supported.");
        int? certificationPermission = ReadCertificationPermission(document, tree);
        if (options.CertificationPermission.HasValue && certificationPermission.HasValue)
            throw new InvalidOperationException(
                "The document already contains a certification signature.");
        if (options.CertificationPermission.HasValue
            && HasSignedSignatureField(document, tree))
            throw new InvalidOperationException(
                "A certification signature must be the first signed field in the document.");
        if (certificationPermission == 1)
            throw new InvalidOperationException(
                "The document certification signature prohibits all subsequent changes.");
        if (certificationPermission.HasValue && existingField is null)
            throw new InvalidOperationException(
                "A certified document can only be signed through an existing signature field.");

        PdfDictionary? fieldMdpParameters = existingField is null
            ? null : ReadFieldMdpParameters(document, existingField);

        var update = new PdfIncrementalUpdateBuilder(document);
        PdfIndirectReference signatureReference = update.ReserveObject();
        update.SetObject(signatureReference,
            BuildSignatureDictionary(options, fieldMdpParameters));
        PdfDictionary catalogReplacement = tree.Catalog;
        bool catalogChanged = false;
        if (existingField is not null)
        {
            if (!existingField.FieldType.Equals(Name("Sig")))
                throw new InvalidOperationException(
                    $"The AcroForm field '{options.FieldName}' is not a signature field.");
            if (existingField.Dictionary.TryGetValue(Name("V"), out PdfObject? existingValue))
            {
                PdfObject resolvedValue = existingValue is PdfIndirectReference valueReference
                    ? document.Resolve(valueReference) : existingValue;
                if (resolvedValue is not PdfNull)
                    throw new InvalidOperationException(
                        $"The signature field '{options.FieldName}' is already signed.");
            }
            update.ReplaceObject(existingField.Reference.ObjectNumber,
                ReplaceMany(existingField.Dictionary, new Dictionary<PdfName, PdfObject>
                {
                    [Name("V")] = signatureReference
                }));
            PdfDictionary? replacement = UpdateSignatureFlags(document, tree, update);
            if (replacement is not null)
            {
                catalogReplacement = replacement;
                catalogChanged = true;
            }
        }
        else
        {
            PdfIndirectReference fieldReference = update.ReserveObject();
            PdfPageTreeEntry page = tree.Pages[options.PageIndex];
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

            PdfArray annotations = page.Dictionary.TryGetValue(
                    AnnotsName, out PdfObject? annotationsValue)
                ? ResolveArray(document, annotationsValue, "The signature page /Annots value")
                : new PdfArray([]);
            update.ReplaceObject(page.Reference.ObjectNumber,
                ReplaceMany(page.Dictionary, new Dictionary<PdfName, PdfObject>
                {
                    [AnnotsName] = new PdfArray(annotations.Append(fieldReference))
                }));
            PdfDictionary? replacement = AddSignatureField(
                document, tree, update, fieldReference, options.FieldName);
            if (replacement is not null)
            {
                catalogReplacement = replacement;
                catalogChanged = true;
            }
        }
        if (options.CertificationPermission.HasValue)
        {
            PdfDictionary? replacement = AddCertificationPermission(
                document, tree, update, catalogReplacement, signatureReference);
            if (replacement is not null)
            {
                catalogReplacement = replacement;
                catalogChanged = true;
            }
        }
        if (catalogChanged)
            update.ReplaceObject(tree.CatalogReference.ObjectNumber, catalogReplacement);
        byte[] prepared = update.Build();
        FillSignature(prepared, options.ReservedSignatureSize, createDetachedCms);
        return prepared;
    }

    private static PdfDictionary BuildSignatureDictionary(
        PdfSignatureOptions options, PdfDictionary? fieldMdpParameters)
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
        var references = new List<PdfObject>();
        if (options.CertificationPermission.HasValue)
            references.Add(Dictionary(
                    ("Type", Name("SigRef")),
                    ("TransformMethod", Name("DocMDP")),
                    ("TransformParams", Dictionary(
                        ("Type", Name("TransformParams")),
                        ("P", new PdfInteger((int)options.CertificationPermission.Value)),
                        ("V", Name("1.2"))))));
        if (fieldMdpParameters is not null)
            references.Add(Dictionary(
                ("Type", Name("SigRef")),
                ("TransformMethod", Name("FieldMDP")),
                ("TransformParams", fieldMdpParameters)));
        if (references.Count > 0)
            entries.Add(("Reference", new PdfArray(references)));
        return Dictionary(entries.ToArray());
    }

    private static PdfDictionary? AddSignatureField(
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
            return null;
        }
        return ReplaceMany(tree.Catalog, new Dictionary<PdfName, PdfObject>
        {
            [AcroFormName] = replacement
        });
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
            if (definesName && fullName == fieldName)
                throw new InvalidOperationException($"The AcroForm already contains a field named '{fieldName}'.");
            if (field.TryGetValue(KidsName, out PdfObject? kidsValue))
            {
                PdfArray kids = ResolveArray(document, kidsValue, "An AcroForm field /Kids value");
                foreach (PdfObject kid in kids) Visit(kid, fullName, depth + 1);
            }
            if (reference is not null) active.Remove(reference.ObjectNumber);
        }
    }

    private static ExistingFormField? FindField(
        PdfDocument document, PdfPageTree tree, string fieldName)
    {
        if (!tree.Catalog.TryGetValue(AcroFormName, out PdfObject? formValue)) return null;
        PdfDictionary form = ResolveDictionary(document, formValue, "The catalog /AcroForm value");
        if (!form.TryGetValue(FieldsName, out PdfObject? fieldsValue)) return null;
        PdfArray fields = ResolveArray(document, fieldsValue, "The AcroForm /Fields value");
        var active = new HashSet<int>();
        var visited = new HashSet<int>();
        ExistingFormField? match = null;
        foreach (PdfObject value in fields) Visit(value, null, null, 0);
        return match;

        void Visit(PdfObject value, string? parentName, PdfName? inheritedType, int depth)
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
            PdfName? fieldType = inheritedType;
            if (field.TryGetValue(Name("FT"), out PdfObject? typeValue))
                fieldType = typeValue as PdfName
                    ?? throw new InvalidOperationException("An AcroForm field /FT value is not a name.");
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
            {
                if (match is not null)
                    throw new InvalidOperationException(
                        $"The AcroForm contains more than one field named '{fieldName}'.");
                if (reference is null)
                    throw new NotSupportedException(
                        "Signing a direct AcroForm field requires rewriting its containing field tree.");
                match = new ExistingFormField(reference, field, fieldType
                    ?? throw new InvalidOperationException(
                        $"The AcroForm field '{fieldName}' has no field type."));
            }
            if (field.TryGetValue(KidsName, out PdfObject? kidsValue))
            {
                PdfArray kids = ResolveArray(document, kidsValue, "An AcroForm field /Kids value");
                foreach (PdfObject kid in kids) Visit(kid, fullName, fieldType, depth + 1);
            }
            if (reference is not null) active.Remove(reference.ObjectNumber);
        }
    }

    private static PdfDictionary? ReadFieldMdpParameters(
        PdfDocument document, ExistingFormField field)
    {
        if (!field.Dictionary.TryGetValue(Name("Lock"), out PdfObject? lockValue))
            return null;
        if (lockValue is not PdfIndirectReference)
            throw new InvalidOperationException(
                "A signature field /Lock value is not an indirect reference.");
        PdfDictionary fieldLock = ResolveDictionary(
            document, lockValue, "The signature field /Lock value");
        if (!fieldLock.TryGetValue(Name("Action"), out PdfObject? actionValue)
            || actionValue is not PdfName action
            || action.ValueAsLatin1() is not ("All" or "Include" or "Exclude"))
            throw new InvalidOperationException(
                "A signature field lock has no valid /Action value.");
        string actionName = action.ValueAsLatin1();
        PdfArray? fields = null;
        if (fieldLock.TryGetValue(Name("Fields"), out PdfObject? fieldsValue))
        {
            fields = ResolveArray(document, fieldsValue, "The signature field lock /Fields value");
            if (fields.Count == 0 || fields.Any(value => value is not PdfString))
                throw new InvalidOperationException(
                    "A signature field lock /Fields array must contain field-name strings.");
        }
        if (actionName == "All" && fields is not null)
            throw new InvalidOperationException(
                "An all-fields signature lock cannot contain a /Fields array.");
        if (actionName != "All" && fields is null)
            throw new InvalidOperationException(
                "An include or exclude signature lock requires a /Fields array.");

        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("TransformParams")),
            ("Action", action),
            ("V", Name("1.2"))
        };
        if (fields is not null) entries.Add(("Fields", fields));
        if (fieldLock.TryGetValue(Name("P"), out PdfObject? permissionValue))
        {
            if (permissionValue is not PdfInteger permission || permission.Value is < 1 or > 3)
                throw new InvalidOperationException(
                    "A signature field lock /P value is not an integer from 1 through 3.");
            entries.Add(("P", permission));
        }
        return Dictionary(entries.ToArray());
    }

    private static int? ReadCertificationPermission(
        PdfDocument document, PdfPageTree tree)
    {
        if (!tree.Catalog.TryGetValue(PermissionsName, out PdfObject? permissionsValue))
            return null;
        PdfDictionary permissions = ResolveDictionary(
            document, permissionsValue, "The catalog /Perms value");
        if (!permissions.TryGetValue(DocMdpName, out PdfObject? signatureValue))
            return null;
        if (signatureValue is not PdfIndirectReference)
            throw new InvalidOperationException(
                "The certification /Perms /DocMDP value is not an indirect reference.");
        PdfDictionary signature = ResolveDictionary(
            document, signatureValue, "The certification signature");
        if (!signature.TryGetValue(Name("Reference"), out PdfObject? referencesValue))
            throw new InvalidOperationException(
                "The certification signature has no /Reference array.");
        PdfArray references = ResolveArray(
            document, referencesValue, "The certification signature /Reference value");
        PdfDictionary? transformParameters = null;
        foreach (PdfObject referenceValue in references)
        {
            PdfDictionary reference = ResolveDictionary(
                document, referenceValue, "A certification signature reference");
            if (!reference.TryGetValue(Name("TransformMethod"), out PdfObject? methodValue))
                continue;
            if (methodValue is not PdfName method)
                throw new InvalidOperationException(
                    "A certification signature /TransformMethod value is not a name.");
            if (!method.Equals(DocMdpName)) continue;
            if (transformParameters is not null)
                throw new InvalidOperationException(
                    "The certification signature has more than one DocMDP transform.");
            if (!reference.TryGetValue(Name("TransformParams"), out PdfObject? parametersValue))
                throw new InvalidOperationException(
                    "The certification signature DocMDP transform has no parameters.");
            transformParameters = ResolveDictionary(
                document, parametersValue, "The DocMDP transform parameters");
        }
        if (transformParameters is null)
            throw new InvalidOperationException(
                "The certification signature has no DocMDP transform.");
        if (!transformParameters.TryGetValue(Name("P"), out PdfObject? permissionValue))
            return 2;
        if (permissionValue is not PdfInteger permission || permission.Value is < 1 or > 3)
            throw new InvalidOperationException(
                "The DocMDP transform permission is not an integer from 1 through 3.");
        return (int)permission.Value;
    }

    private static bool HasSignedSignatureField(PdfDocument document, PdfPageTree tree)
    {
        if (!tree.Catalog.TryGetValue(AcroFormName, out PdfObject? formValue)) return false;
        PdfDictionary form = ResolveDictionary(document, formValue, "The catalog /AcroForm value");
        if (!form.TryGetValue(FieldsName, out PdfObject? fieldsValue)) return false;
        PdfArray fields = ResolveArray(document, fieldsValue, "The AcroForm /Fields value");
        var active = new HashSet<int>();
        var visited = new HashSet<int>();
        foreach (PdfObject field in fields)
            if (Visit(field, null, 0)) return true;
        return false;

        bool Visit(PdfObject value, PdfName? inheritedType, int depth)
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
            PdfName? fieldType = inheritedType;
            if (field.TryGetValue(Name("FT"), out PdfObject? typeValue))
                fieldType = typeValue as PdfName
                    ?? throw new InvalidOperationException("An AcroForm field /FT value is not a name.");
            if (fieldType?.Equals(Name("Sig")) == true
                && field.TryGetValue(Name("V"), out PdfObject? signatureValue))
            {
                PdfObject resolved = signatureValue is PdfIndirectReference signatureReference
                    ? document.Resolve(signatureReference) : signatureValue;
                if (resolved is not PdfNull) return true;
            }
            if (field.TryGetValue(KidsName, out PdfObject? kidsValue))
            {
                PdfArray kids = ResolveArray(document, kidsValue, "An AcroForm field /Kids value");
                foreach (PdfObject kid in kids)
                    if (Visit(kid, fieldType, depth + 1)) return true;
            }
            if (reference is not null) active.Remove(reference.ObjectNumber);
            return false;
        }
    }

    private static PdfDictionary? AddCertificationPermission(
        PdfDocument document,
        PdfPageTree tree,
        PdfIncrementalUpdateBuilder update,
        PdfDictionary catalog,
        PdfIndirectReference signatureReference)
    {
        if (!tree.Catalog.TryGetValue(PermissionsName, out PdfObject? permissionsValue))
            return ReplaceMany(catalog, new Dictionary<PdfName, PdfObject>
            {
                [PermissionsName] = Dictionary(("DocMDP", signatureReference))
            });

        PdfIndirectReference? permissionsReference = permissionsValue as PdfIndirectReference;
        PdfDictionary permissions = ResolveDictionary(
            document, permissionsValue, "The catalog /Perms value");
        if (permissions.ContainsKey(DocMdpName))
            throw new InvalidOperationException(
                "The document already contains a certification signature.");
        PdfDictionary replacement = ReplaceMany(permissions, new Dictionary<PdfName, PdfObject>
        {
            [DocMdpName] = signatureReference
        });
        if (permissionsReference is not null)
        {
            update.ReplaceObject(permissionsReference.ObjectNumber, replacement);
            return null;
        }
        return ReplaceMany(catalog, new Dictionary<PdfName, PdfObject>
        {
            [PermissionsName] = replacement
        });
    }

    private static PdfDictionary? UpdateSignatureFlags(
        PdfDocument document, PdfPageTree tree, PdfIncrementalUpdateBuilder update)
    {
        PdfObject formValue = tree.Catalog[AcroFormName];
        PdfIndirectReference? formReference = formValue as PdfIndirectReference;
        PdfDictionary form = ResolveDictionary(document, formValue, "The catalog /AcroForm value");
        long flags = 0;
        if (form.TryGetValue(SignatureFlagsName, out PdfObject? flagsValue))
            flags = flagsValue is PdfInteger integer && integer.Value >= 0
                ? integer.Value
                : throw new InvalidOperationException(
                    "The AcroForm /SigFlags value is not a non-negative integer.");
        PdfDictionary replacement = ReplaceMany(form, new Dictionary<PdfName, PdfObject>
        {
            [SignatureFlagsName] = new PdfInteger(flags | 3)
        });
        if (formReference is not null)
        {
            update.ReplaceObject(formReference.ObjectNumber, replacement);
            return null;
        }
        return ReplaceMany(tree.Catalog, new Dictionary<PdfName, PdfObject>
        {
            [AcroFormName] = replacement
        });
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
        if (string.IsNullOrWhiteSpace(options.FieldName))
            throw new ArgumentException("A signature field name must be non-empty.", nameof(options));
        if (options.ReservedSignatureSize is <= 0 or > MaximumReservedSignatureSize)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"The reserved signature size must be between 1 and {MaximumReservedSignatureSize} bytes.");
        if (options.CertificationPermission.HasValue
            && options.CertificationPermission.Value is not
                (PdfSignatureCertificationPermission.NoChanges
                or PdfSignatureCertificationPermission.FormFillingAndSignatures
                or PdfSignatureCertificationPermission.FormFillingSignaturesAndAnnotations))
            throw new ArgumentOutOfRangeException(nameof(options),
                "A certification permission must be one of the three DocMDP permission levels.");
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

    private sealed record ExistingFormField(
        PdfIndirectReference Reference, PdfDictionary Dictionary, PdfName FieldType);
}
