using System.Security.Cryptography;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf.Security;

/// <summary>
/// Decrypts the strings and streams of a file protected by the standard security handler: RC4 (revisions 2–3),
/// AES-128 (revision 4) and AES-256 (revisions 5–6). Built from the trailer's /Encrypt and /ID and a password.
/// </summary>
internal sealed class Decryptor
{
    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    private readonly byte[] _key;
    private readonly int _revision;
    private readonly bool _aesStrings, _aesStreams, _identityStreams, _identityStrings;

    public bool EncryptMetadata { get; }

    /// <summary>True when the password given was the owner password (or the file grants every permission anyway).</summary>
    public bool IsOwner { get; }

    public int Permissions { get; }

    public Decryptor(PdfDict enc, byte[] id0, string? password, Func<PdfValue?, PdfValue?> resolve)
    {
        string filter = (resolve(enc.Get("Filter")) as PdfName)?.Encoded ?? "";
        if (filter != "Standard") throw new NotSupportedException($"PDF files encrypted with the '{filter}' security handler are not supported.");
        int v = (resolve(enc.Get("V")) as PdfNumber)?.IntValue ?? 0;
        _revision = (resolve(enc.Get("R")) as PdfNumber)?.IntValue ?? 0;
        int lengthBits = (resolve(enc.Get("Length")) as PdfNumber)?.IntValue ?? 40;
        Permissions = (resolve(enc.Get("P")) as PdfNumber)?.IntValue ?? 0;
        EncryptMetadata = resolve(enc.Get("EncryptMetadata")) is not PdfBool { Value: false };
        byte[] o = (resolve(enc.Get("O")) as PdfString)?.Bytes ?? [];
        byte[] u = (resolve(enc.Get("U")) as PdfString)?.Bytes ?? [];

        string Method(string key)
        {
            string name = (resolve(enc.Get(key)) as PdfName)?.Encoded ?? "Identity";
            if (name == "Identity") return "Identity";
            var cf = resolve(enc.Get("CF")) as PdfDict;
            return (resolve((resolve(cf?.Get(name)) as PdfDict)?.Get("CFM")) as PdfName)?.Encoded ?? "None";
        }
        if (v >= 4)
        {
            string stm = Method("StmF"), str = Method("StrF");
            _aesStreams = stm is "AESV2" or "AESV3"; _aesStrings = str is "AESV2" or "AESV3";
            _identityStreams = stm == "Identity"; _identityStrings = str == "Identity";
        }

        byte[] pw = password is null ? [] : _revision >= 5 ? PasswordR6.Prepare(password) : System.Text.Encoding.Latin1.GetBytes(password);
        if (_revision >= 5)
        {
            if (o.Length < 48 || u.Length < 48) throw new InvalidDataException("The encryption dictionary is malformed.");
            byte[] oe = (resolve(enc.Get("OE")) as PdfString)?.Bytes ?? [], ue = (resolve(enc.Get("UE")) as PdfString)?.Bytes ?? [];
            byte[] u48 = u[..48];
            if (PasswordR6.Hash(pw, o.AsSpan(32, 8), u48).AsSpan().SequenceEqual(o.AsSpan(0, 32)))
            {
                _key = PasswordR6.AesNoIv(PasswordR6.Hash(pw, o.AsSpan(40, 8), u48), oe[..32], encrypt: false);
                IsOwner = true;
            }
            else if (PasswordR6.Hash(pw, u.AsSpan(32, 8), []).AsSpan().SequenceEqual(u.AsSpan(0, 32)))
                _key = PasswordR6.AesNoIv(PasswordR6.Hash(pw, u.AsSpan(40, 8), []), ue[..32], encrypt: false);
            else
                throw new PdfPasswordException(password is null ? "The PDF file is encrypted: a password is needed to open it." : "The password does not open this PDF file.");
            return;
        }

        int n = _revision == 2 ? 5 : Math.Clamp(lengthBits / 8, 5, 16);
        byte[]? key = UserKey(pw, o, id0, n);
        if (key is not null && CheckUser(key, u, id0)) { _key = key; return; }

        // The owner password: it decrypts /O into the user password.
        byte[] ownerKey = MD5.HashData(Pad(pw));
        if (_revision >= 3) for (int i = 0; i < 50; i++) ownerKey = MD5.HashData(ownerKey.AsSpan(0, n));
        byte[] userPw = o;
        if (_revision == 2) userPw = Rc4.Apply(ownerKey[..n], o);
        else
            for (int i = 19; i >= 0; i--)
                userPw = Rc4.Apply([.. ownerKey[..n].Select(b => (byte)(b ^ i))], userPw);
        key = UserKey(userPw, o, id0, n);
        if (key is not null && CheckUser(key, u, id0)) { _key = key; IsOwner = true; return; }
        throw new PdfPasswordException(password is null ? "The PDF file is encrypted: a password is needed to open it." : "The password does not open this PDF file.");
    }

    private byte[] UserKey(byte[] pw, byte[] o, byte[] id0, int n)
    {
        byte[] p = BitConverter.GetBytes(Permissions);
        if (!BitConverter.IsLittleEndian) Array.Reverse(p);
        var input = new List<byte>(Pad(pw));
        input.AddRange(o.Take(32)); input.AddRange(p); input.AddRange(id0);
        if (_revision >= 4 && !EncryptMetadata) input.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);
        byte[] key = MD5.HashData(input.ToArray());
        if (_revision >= 3) for (int i = 0; i < 50; i++) key = MD5.HashData(key.AsSpan(0, n));
        return key[..n];
    }

    private bool CheckUser(byte[] key, byte[] u, byte[] id0)
    {
        if (_revision == 2) return Rc4.Apply(key, Padding).AsSpan().SequenceEqual(u.AsSpan(0, Math.Min(32, u.Length)));
        byte[] x = Rc4.Apply(key, MD5.HashData([.. Padding, .. id0]));
        for (int i = 1; i <= 19; i++) x = Rc4.Apply([.. key.Select(b => (byte)(b ^ i))], x);
        return u.Length >= 16 && x.AsSpan(0, 16).SequenceEqual(u.AsSpan(0, 16));
    }

    private static byte[] Pad(byte[] pw) => [.. pw.Take(32), .. Padding.Take(32 - Math.Min(32, pw.Length))];

    public byte[] DecryptString(byte[] data, int num, int gen) => _identityStrings ? data : Decrypt(data, num, gen, _aesStrings);

    public byte[] DecryptStream(byte[] data, int num, int gen) => _identityStreams ? data : Decrypt(data, num, gen, _aesStreams);

    private byte[] Decrypt(byte[] data, int num, int gen, bool aes)
    {
        byte[] key = _key;
        if (_revision < 5)
        {
            byte[] salt = [.. _key, (byte)num, (byte)(num >> 8), (byte)(num >> 16), (byte)gen, (byte)(gen >> 8), .. (aes ? "sAlT"u8.ToArray() : [])];
            key = MD5.HashData(salt)[..Math.Min(_key.Length + 5, 16)];
        }
        if (!aes) return Rc4.Apply(key, data);
        if (data.Length < 16) return [];
        using var a = Aes.Create();
        a.Key = key;
        // A damaged value (not whole blocks, bad padding) decrypts as far as it can rather than failing the file.
        ReadOnlySpan<byte> blocks = data.AsSpan(16, (data.Length - 16) / 16 * 16);
        try { return a.DecryptCbc(blocks, data.AsSpan(0, 16), PaddingMode.PKCS7); }
        catch (CryptographicException) { return a.DecryptCbc(blocks, data.AsSpan(0, 16), PaddingMode.None); }
    }
}

/// <summary>The RC4 stream cipher, which older encrypted PDF files use and .NET does not provide.</summary>
internal static class Rc4
{
    public static byte[] Apply(byte[] key, byte[] data)
    {
        Span<byte> s = stackalloc byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }
        var output = new byte[data.Length];
        for (int k = 0, i = 0, j = 0; k < data.Length; k++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[k] = (byte)(data[k] ^ s[(s[i] + s[j]) & 0xFF]);
        }
        return output;
    }
}
