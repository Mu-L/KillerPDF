using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KillerPdf.Engine.Objects;

namespace KillerPdf.Engine.Security;

internal sealed class PdfStandardSecurityHandler
{
    private static readonly PdfName MetadataName = Name("Metadata");
    private static readonly PdfName EmbeddedFileName = Name("EmbeddedFile");
    private static readonly PdfName CrossReferenceName = Name("XRef");
    private static readonly PdfName SignatureName = Name("Sig");
    private static readonly PdfName ContentsName = Name("Contents");
    private static readonly PdfName ByteRangeName = Name("ByteRange");
    private static readonly PdfName TypeName = Name("Type");
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];
    private readonly byte[] _fileKey;
    private readonly CryptMethod _stringMethod;
    private readonly CryptMethod _streamMethod;
    private readonly CryptMethod _embeddedFileMethod;
    private readonly bool _encryptMetadata;

    private PdfStandardSecurityHandler(
        byte[] fileKey, CryptMethod stringMethod, CryptMethod streamMethod,
        CryptMethod embeddedFileMethod, bool encryptMetadata)
    {
        _fileKey = fileKey;
        _stringMethod = stringMethod;
        _streamMethod = streamMethod;
        _embeddedFileMethod = embeddedFileMethod;
        _encryptMetadata = encryptMetadata;
    }

    internal static PdfStandardSecurityHandler Create(
        PdfDictionary encryption, string password, ReadOnlyMemory<byte> permanentIdentifier)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(password);
        RequireName(encryption, "Filter", "Standard");
        long version = RequireInteger(encryption, "V");
        long revision = RequireInteger(encryption, "R");
        if (revision is >= 2 and <= 4 && version is >= 1 and <= 4)
            return CreateLegacy(
                encryption, password, permanentIdentifier.Span, version, revision);
        if (version != 5 || revision is not (5 or 6))
            throw new NotSupportedException(
                $"Standard security handler V={version}, R={revision} is not supported.");
        if (RequireInteger(encryption, "Length") != 256)
            throw new InvalidOperationException(
                "AES-256 encryption requires a 256-bit encryption key.");
        byte[] owner = RequireBytes(encryption, "O", 48);
        byte[] user = RequireBytes(encryption, "U", 48);
        byte[] ownerEncryptedKey = RequireBytes(encryption, "OE", 32);
        byte[] userEncryptedKey = RequireBytes(encryption, "UE", 32);
        byte[] passwordBytes = PasswordBytes(password, revision == 6);
        byte[]? fileKey = TryOwnerPassword(
            passwordBytes, owner, user, ownerEncryptedKey, revision)
            ?? TryUserPassword(passwordBytes, user, userEncryptedKey, revision);
        if (fileKey is null)
            throw new CryptographicException("The PDF password is incorrect.");
        byte[] permissions = DecryptEcb(fileKey, RequireBytes(encryption, "Perms", 16));
        if (!permissions.AsSpan(9, 3).SequenceEqual("adb"u8))
            throw new CryptographicException("The PDF encryption permission block is invalid.");
        int declaredPermissions = checked((int)RequireInteger(encryption, "P"));
        if (BinaryPrimitives.ReadInt32LittleEndian(permissions) != declaredPermissions)
            throw new CryptographicException("The PDF encryption permissions do not authenticate.");
        bool encryptMetadata = !encryption.TryGetValue(Name("EncryptMetadata"), out PdfObject? metadata)
            || metadata is PdfBoolean { Value: true };
        if (permissions[8] != (encryptMetadata ? (byte)'T' : (byte)'F'))
            throw new CryptographicException("The PDF metadata-encryption setting does not authenticate.");
        CryptMethod stringMethod = ReadModernCryptFilter(encryption, "StrF", "AESV3");
        CryptMethod streamMethod = ReadModernCryptFilter(encryption, "StmF", "AESV3");
        CryptMethod embeddedFileMethod = encryption.ContainsKey(Name("EFF"))
            ? ReadModernCryptFilter(encryption, "EFF", "AESV3") : streamMethod;
        return new PdfStandardSecurityHandler(
            fileKey, stringMethod, streamMethod, embeddedFileMethod, encryptMetadata);
    }

    internal PdfObject Decrypt(PdfObject value, int objectNumber, int generation)
    {
        return value switch
        {
            PdfString text when _stringMethod != CryptMethod.Identity =>
                new PdfString(DecryptBytes(
                    text.Bytes.Span, _stringMethod, objectNumber, generation), text.Form),
            PdfArray array => new PdfArray(array.Select(item =>
                Decrypt(item, objectNumber, generation))),
            PdfDictionary dictionary => TransformDictionary(
                dictionary, objectNumber, generation, decrypt: true),
            PdfStream stream => DecryptStream(stream, objectNumber, generation),
            _ => value
        };
    }

    internal PdfObject Encrypt(PdfObject value, int objectNumber, int generation)
    {
        return value switch
        {
            PdfString text when _stringMethod != CryptMethod.Identity =>
                new PdfString(EncryptBytes(
                    text.Bytes.Span, _stringMethod, objectNumber, generation),
                    PdfStringForm.Hexadecimal),
            PdfArray array => new PdfArray(array.Select(item =>
                Encrypt(item, objectNumber, generation))),
            PdfDictionary dictionary => TransformDictionary(
                dictionary, objectNumber, generation, decrypt: false),
            PdfStream stream => EncryptStream(stream, objectNumber, generation),
            _ => value
        };
    }

    private PdfDictionary TransformDictionary(
        PdfDictionary dictionary, int objectNumber, int generation, bool decrypt)
    {
        bool isSignature = dictionary.TryGetValue(TypeName, out PdfObject? type)
                && type is PdfName name && name.Equals(SignatureName)
            || dictionary.ContainsKey(ByteRangeName) && dictionary.ContainsKey(ContentsName);
        return new PdfDictionary(dictionary.Select(entry =>
            new KeyValuePair<PdfName, PdfObject>(entry.Key,
                isSignature && entry.Key.Equals(ContentsName)
                    ? entry.Value
                    : decrypt
                        ? Decrypt(entry.Value, objectNumber, generation)
                        : Encrypt(entry.Value, objectNumber, generation))));
    }

    private PdfStream DecryptStream(PdfStream stream, int objectNumber, int generation)
    {
        if (stream.Dictionary.TryGetValue(TypeName, out PdfObject? rawType)
            && rawType is PdfName rawName && rawName.Equals(CrossReferenceName))
            return stream;
        PdfDictionary dictionary = (PdfDictionary)Decrypt(
            stream.Dictionary, objectNumber, generation);
        bool isMetadata = dictionary.TryGetValue(TypeName, out PdfObject? type)
            && type is PdfName name && name.Equals(MetadataName);
        bool isEmbeddedFile = type is PdfName embeddedName
            && embeddedName.Equals(EmbeddedFileName);
        CryptMethod method = isEmbeddedFile ? _embeddedFileMethod : _streamMethod;
        ReadOnlySpan<byte> data = stream.EncodedData.Span;
        return new PdfStream(dictionary,
            method != CryptMethod.Identity && (_encryptMetadata || !isMetadata)
                ? DecryptBytes(data, method, objectNumber, generation) : data);
    }

    private PdfStream EncryptStream(PdfStream stream, int objectNumber, int generation)
    {
        PdfDictionary dictionary = (PdfDictionary)Encrypt(
            stream.Dictionary, objectNumber, generation);
        bool isMetadata = dictionary.TryGetValue(TypeName, out PdfObject? type)
            && type is PdfName name && name.Equals(MetadataName);
        bool isEmbeddedFile = type is PdfName embeddedName
            && embeddedName.Equals(EmbeddedFileName);
        CryptMethod method = isEmbeddedFile ? _embeddedFileMethod : _streamMethod;
        ReadOnlySpan<byte> data = stream.EncodedData.Span;
        return new PdfStream(dictionary,
            method != CryptMethod.Identity && (_encryptMetadata || !isMetadata)
                ? EncryptBytes(data, method, objectNumber, generation) : data);
    }

    private static PdfStandardSecurityHandler CreateLegacy(
        PdfDictionary encryption,
        string password,
        ReadOnlySpan<byte> permanentIdentifier,
        long version,
        long revision)
    {
        if (permanentIdentifier.IsEmpty)
            throw new InvalidOperationException(
                "Legacy Standard security requires a permanent document identifier.");
        int keyLength = revision == 2 ? 5 : checked((int)RequireInteger(encryption, "Length") / 8);
        if (keyLength is < 5 or > 16)
            throw new InvalidOperationException(
                "Legacy Standard security requires a 40-bit through 128-bit key.");
        byte[] owner = RequireBytes(encryption, "O", 32);
        byte[] user = RequireBytes(encryption, "U", 32);
        int permissions = checked((int)RequireInteger(encryption, "P"));
        bool encryptMetadata = !encryption.TryGetValue(Name("EncryptMetadata"), out PdfObject? metadata)
            || metadata is PdfBoolean { Value: true };
        byte[] supplied = PadLegacyPassword(password);
        byte[]? fileKey = TryLegacyUserPassword(
            supplied, owner, user, permissions, permanentIdentifier,
            keyLength, revision, encryptMetadata);
        if (fileKey is null)
        {
            byte[] userPassword = RecoverLegacyUserPassword(
                supplied, owner, keyLength, revision);
            fileKey = TryLegacyUserPassword(
                userPassword, owner, user, permissions, permanentIdentifier,
                keyLength, revision, encryptMetadata);
        }
        if (fileKey is null)
            throw new CryptographicException("The PDF password is incorrect.");
        CryptMethod stringMethod;
        CryptMethod streamMethod;
        CryptMethod embeddedFileMethod;
        if (version < 4)
            stringMethod = streamMethod = embeddedFileMethod = CryptMethod.Rc4;
        else
        {
            stringMethod = ReadModernCryptFilter(encryption, "StrF", null);
            streamMethod = ReadModernCryptFilter(encryption, "StmF", null);
            embeddedFileMethod = encryption.ContainsKey(Name("EFF"))
                ? ReadModernCryptFilter(encryption, "EFF", null) : streamMethod;
        }
        return new PdfStandardSecurityHandler(
            fileKey, stringMethod, streamMethod, embeddedFileMethod, encryptMetadata);
    }

    private static byte[]? TryLegacyUserPassword(
        byte[] paddedPassword,
        byte[] owner,
        byte[] user,
        int permissions,
        ReadOnlySpan<byte> permanentIdentifier,
        int keyLength,
        long revision,
        bool encryptMetadata)
    {
        byte[] key = LegacyFileKey(
            paddedPassword, owner, permissions, permanentIdentifier,
            keyLength, revision, encryptMetadata);
        byte[] candidate;
        if (revision == 2)
            candidate = Rc4(key, PasswordPadding);
        else
        {
            byte[] input = [.. PasswordPadding, .. permanentIdentifier];
            candidate = MD5.HashData(input);
            candidate = Rc4(key, candidate);
            for (int round = 1; round <= 19; round++)
                candidate = Rc4(XorKey(key, round), candidate);
        }
        int comparedLength = revision == 2 ? 32 : 16;
        return CryptographicOperations.FixedTimeEquals(
            candidate.AsSpan(0, comparedLength), user.AsSpan(0, comparedLength)) ? key : null;
    }

    private static byte[] LegacyFileKey(
        byte[] paddedPassword,
        byte[] owner,
        int permissions,
        ReadOnlySpan<byte> permanentIdentifier,
        int keyLength,
        long revision,
        bool encryptMetadata)
    {
        byte[] input = new byte[paddedPassword.Length + owner.Length + 4
            + permanentIdentifier.Length + (revision >= 4 && !encryptMetadata ? 4 : 0)];
        int offset = 0;
        paddedPassword.CopyTo(input, offset);
        offset += paddedPassword.Length;
        owner.CopyTo(input, offset);
        offset += owner.Length;
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(offset, 4), permissions);
        offset += 4;
        permanentIdentifier.CopyTo(input.AsSpan(offset));
        offset += permanentIdentifier.Length;
        if (revision >= 4 && !encryptMetadata)
            input.AsSpan(offset, 4).Fill(0xFF);
        byte[] hash = MD5.HashData(input);
        if (revision >= 3)
            for (int round = 0; round < 50; round++)
                hash = MD5.HashData(hash.AsSpan(0, keyLength));
        return hash[..keyLength];
    }

    private static byte[] RecoverLegacyUserPassword(
        byte[] paddedOwnerPassword, byte[] owner, int keyLength, long revision)
    {
        byte[] hash = MD5.HashData(paddedOwnerPassword);
        if (revision >= 3)
            for (int round = 0; round < 50; round++) hash = MD5.HashData(hash);
        byte[] key = hash[..keyLength];
        byte[] result = owner.ToArray();
        if (revision == 2) return Rc4(key, result);
        for (int round = 19; round >= 0; round--)
            result = Rc4(XorKey(key, round), result);
        return result;
    }

    private static byte[] PadLegacyPassword(string password)
    {
        byte[] encoded = Encoding.Latin1.GetBytes(password);
        byte[] result = new byte[32];
        int copied = Math.Min(encoded.Length, result.Length);
        encoded.AsSpan(0, copied).CopyTo(result);
        PasswordPadding.AsSpan(0, result.Length - copied).CopyTo(result.AsSpan(copied));
        return result;
    }

    private static byte[]? TryOwnerPassword(
        byte[] password, byte[] owner, byte[] user, byte[] encryptedKey, long revision)
    {
        byte[] validation = HashPassword(password, owner.AsSpan(32, 8), user, revision);
        if (!CryptographicOperations.FixedTimeEquals(validation, owner.AsSpan(0, 32))) return null;
        byte[] key = HashPassword(password, owner.AsSpan(40, 8), user, revision);
        return DecryptKey(key, encryptedKey);
    }

    private static byte[]? TryUserPassword(
        byte[] password, byte[] user, byte[] encryptedKey, long revision)
    {
        byte[] validation = HashPassword(password, user.AsSpan(32, 8), null, revision);
        if (!CryptographicOperations.FixedTimeEquals(validation, user.AsSpan(0, 32))) return null;
        byte[] key = HashPassword(password, user.AsSpan(40, 8), null, revision);
        return DecryptKey(key, encryptedKey);
    }

    private static byte[] HashPassword(
        byte[] password, ReadOnlySpan<byte> salt, byte[]? user, long revision)
    {
        byte[] input = [.. password, .. salt, .. user ?? []];
        if (revision == 5) return SHA256.HashData(input);
        byte[] key = SHA256.HashData(input);
        byte[] encrypted = [];
        int round = 0;
        do
        {
            byte[] block = [.. password, .. key, .. user ?? []];
            byte[] repeated = new byte[checked(block.Length * 64)];
            for (int index = 0; index < 64; index++)
                block.CopyTo(repeated, index * block.Length);
            using Aes aes = Aes.Create();
            aes.KeySize = 128;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = key[..16];
            aes.IV = key[16..32];
            encrypted = aes.EncryptCbc(repeated, aes.IV, PaddingMode.None);
            int selector = 0;
            for (int index = 0; index < 16; index++)
                selector = (selector * 256 + encrypted[index]) % 3;
            key = selector switch
            {
                0 => SHA256.HashData(encrypted),
                1 => SHA384.HashData(encrypted),
                _ => SHA512.HashData(encrypted)
            };
            round++;
        }
        while (round < 64 || encrypted[^1] > round - 32);
        return key[..32];
    }

    private static byte[] PasswordBytes(string password, bool normalize)
    {
        string value = normalize ? password.Normalize(NormalizationForm.FormKC) : password;
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return bytes.Length <= 127 ? bytes : bytes[..127];
    }

    private static byte[] DecryptKey(byte[] key, byte[] encrypted)
    {
        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = new byte[16];
        return aes.DecryptCbc(encrypted, aes.IV, PaddingMode.None);
    }

    private static byte[] DecryptEcb(byte[] key, byte[] encrypted)
    {
        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        return aes.DecryptEcb(encrypted, PaddingMode.None);
    }

    private byte[] DecryptBytes(
        ReadOnlySpan<byte> encrypted, CryptMethod method, int objectNumber, int generation) =>
        method switch
        {
            CryptMethod.Rc4 => Rc4(ObjectKey(objectNumber, generation, aes: false), encrypted),
            CryptMethod.Aes128 => DecryptAes(
                encrypted, ObjectKey(objectNumber, generation, aes: true)),
            CryptMethod.Aes256 => DecryptAes(encrypted, _fileKey),
            _ => encrypted.ToArray()
        };

    private byte[] EncryptBytes(
        ReadOnlySpan<byte> cleartext, CryptMethod method, int objectNumber, int generation) =>
        method switch
        {
            CryptMethod.Rc4 => Rc4(ObjectKey(objectNumber, generation, aes: false), cleartext),
            CryptMethod.Aes128 => EncryptAes(
                cleartext, ObjectKey(objectNumber, generation, aes: true)),
            CryptMethod.Aes256 => EncryptAes(cleartext, _fileKey),
            _ => cleartext.ToArray()
        };

    private byte[] ObjectKey(int objectNumber, int generation, bool aes)
    {
        byte[] input = new byte[_fileKey.Length + 5 + (aes ? 4 : 0)];
        _fileKey.CopyTo(input, 0);
        int offset = _fileKey.Length;
        input[offset++] = (byte)objectNumber;
        input[offset++] = (byte)(objectNumber >> 8);
        input[offset++] = (byte)(objectNumber >> 16);
        input[offset++] = (byte)generation;
        input[offset++] = (byte)(generation >> 8);
        if (aes) "sAlT"u8.CopyTo(input.AsSpan(offset));
        byte[] hash = MD5.HashData(input);
        return hash[..Math.Min(_fileKey.Length + 5, 16)];
    }

    private static byte[] DecryptAes(ReadOnlySpan<byte> encrypted, byte[] key)
    {
        if (encrypted.Length < 32 || encrypted.Length % 16 != 0)
            throw new CryptographicException("An AES-256 encrypted PDF value has an invalid length.");
        using Aes aes = Aes.Create();
        aes.KeySize = key.Length * 8;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        return aes.DecryptCbc(encrypted[16..], encrypted[..16], PaddingMode.PKCS7);
    }

    private static byte[] EncryptAes(ReadOnlySpan<byte> cleartext, byte[] key)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        using Aes aes = Aes.Create();
        aes.KeySize = key.Length * 8;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        byte[] encrypted = aes.EncryptCbc(cleartext, iv, PaddingMode.PKCS7);
        return [.. iv, .. encrypted];
    }

    private static CryptMethod ReadModernCryptFilter(
        PdfDictionary encryption, string key, string? requiredMethod)
    {
        if (!encryption.TryGetValue(Name(key), out PdfObject? filterValue)
            || filterValue is not PdfName filter)
            throw new InvalidOperationException($"The encryption dictionary /{key} value is not a name.");
        if (filter.ValueAsLatin1() == "Identity") return CryptMethod.Identity;
        if (!encryption.TryGetValue(Name("CF"), out PdfObject? filtersValue)
            || filtersValue is not PdfDictionary filters
            || !filters.TryGetValue(filter, out PdfObject? selectedValue)
            || selectedValue is not PdfDictionary selected)
            throw new InvalidOperationException($"The encryption crypt filter /{filter.ValueAsLatin1()} is missing.");
        if (!selected.TryGetValue(Name("CFM"), out PdfObject? methodValue)
            || methodValue is not PdfName method)
            throw new InvalidOperationException(
                $"The encryption crypt filter /{filter.ValueAsLatin1()} has no /CFM name.");
        string methodName = method.ValueAsLatin1();
        if (requiredMethod is not null && methodName != requiredMethod)
            throw new NotSupportedException(
                $"The encryption crypt filter method /{methodName} is not /{requiredMethod}.");
        return methodName switch
        {
            "V2" => CryptMethod.Rc4,
            "AESV2" => CryptMethod.Aes128,
            "AESV3" => CryptMethod.Aes256,
            "None" => CryptMethod.Identity,
            _ => throw new NotSupportedException(
                $"Encryption crypt filter method /{methodName} is not supported.")
        };
    }

    private static byte[] XorKey(byte[] key, int value) =>
        key.Select(item => (byte)(item ^ value)).ToArray();

    private static byte[] Rc4(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input)
    {
        Span<byte> state = stackalloc byte[256];
        for (int index = 0; index < state.Length; index++) state[index] = (byte)index;
        int j = 0;
        for (int index = 0; index < state.Length; index++)
        {
            j = (j + state[index] + key[index % key.Length]) & 255;
            (state[index], state[j]) = (state[j], state[index]);
        }
        byte[] output = new byte[input.Length];
        int i = 0;
        j = 0;
        for (int index = 0; index < input.Length; index++)
        {
            i = (i + 1) & 255;
            j = (j + state[i]) & 255;
            (state[i], state[j]) = (state[j], state[i]);
            output[index] = (byte)(input[index] ^ state[(state[i] + state[j]) & 255]);
        }
        return output;
    }

    private static byte[] RequireBytes(PdfDictionary dictionary, string key, int length)
    {
        if (!dictionary.TryGetValue(Name(key), out PdfObject? value)
            || value is not PdfString text || text.Bytes.Length != length)
            throw new InvalidOperationException(
                $"The encryption dictionary /{key} value is not a {length}-byte string.");
        return text.Bytes.ToArray();
    }

    private static long RequireInteger(PdfDictionary dictionary, string key)
    {
        if (!dictionary.TryGetValue(Name(key), out PdfObject? value) || value is not PdfInteger integer)
            throw new InvalidOperationException($"The encryption dictionary /{key} value is not an integer.");
        return integer.Value;
    }

    private static void RequireName(PdfDictionary dictionary, string key, string expected)
    {
        if (!dictionary.TryGetValue(Name(key), out PdfObject? value)
            || value is not PdfName name || name.ValueAsLatin1() != expected)
            throw new InvalidOperationException(
                $"The encryption dictionary /{key} value is not /{expected}.");
    }

    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));

    private enum CryptMethod
    {
        Identity,
        Rc4,
        Aes128,
        Aes256
    }
}
