using System.Security.Cryptography;
using System.Text;

namespace Arkki.Pdf.Security;

/// <summary>The password hash of the revision 6 security handler (ISO 32000-2, algorithm 2.B) and its helpers.</summary>
internal static class PasswordR6
{
    /// <summary>The password as the handler uses it: normalised (an approximation of SASLprep) and at most 127 UTF-8 bytes.</summary>
    public static byte[] Prepare(string password)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC));
        return bytes.Length <= 127 ? bytes : bytes[..127];
    }

    public static byte[] Hash(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> userKey)
    {
        byte[] k = SHA256.HashData([.. password, .. salt, .. userKey]);
        using var aes = Aes.Create();
        for (int round = 0; ; round++)
        {
            byte[] block = [.. password, .. k, .. userKey];
            byte[] k1 = new byte[block.Length * 64];
            for (int i = 0; i < 64; i++) block.CopyTo(k1, i * block.Length);
            aes.Key = k[..16];
            byte[] e = aes.EncryptCbc(k1, k.AsSpan(16, 16), PaddingMode.None);
            int mod = 0;
            for (int i = 0; i < 16; i++) mod += e[i];
            k = (mod % 3) switch { 0 => SHA256.HashData(e), 1 => SHA384.HashData(e), _ => SHA512.HashData(e) };
            if (round + 1 >= 64 && e[^1] <= round + 1 - 32) break;
        }
        return k[..32];
    }

    /// <summary>AES-256-CBC with a zero IV and no padding, as /OE and /UE use it.</summary>
    public static byte[] AesNoIv(byte[] key, byte[] data, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        byte[] iv = new byte[16];
        return encrypt ? aes.EncryptCbc(data, iv, PaddingMode.None) : aes.DecryptCbc(data, iv, PaddingMode.None);
    }
}
