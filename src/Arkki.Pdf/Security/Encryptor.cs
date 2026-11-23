using System.Security.Cryptography;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf.Security;

/// <summary>AES-256 encryption of the strings and streams of a document being written (one key for every object, as in revision 6).</summary>
internal sealed class Encryptor(byte[] fileKey, byte[] dictionary)
{
    public byte[] Dictionary { get; } = dictionary;

    public byte[] EncryptData(ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = fileKey;
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] cipher = aes.EncryptCbc(data, iv, PaddingMode.PKCS7);
        return [.. iv, .. cipher];
    }

    /// <summary>
    /// The serialized object with every string encrypted (and written as hex). Strings are found by scanning the bytes;
    /// a name or number cannot contain '(' or '&lt;', so nothing else is touched.
    /// </summary>
    public byte[] EncryptStrings(ReadOnlySpan<byte> body, int objectId, bool open = false)
    {
        if (body.IndexOf((byte)'(') < 0 && !HasHexString(body)) return body.ToArray();
        byte[] src = body.ToArray();
        var parser = new PdfParser(src);
        var b = new ByteBuf(src.Length + 64);
        int i = 0;
        while (i < src.Length)
        {
            byte c = src[i];
            if (c == '%') { while (i < src.Length && src[i] is not (10 or 13)) b.Byte(src[i++]); continue; }
            bool hex = c == '<' && (i + 1 >= src.Length || src[i + 1] != '<');
            if (c == '<' && !hex) { b.Byte(c).Byte(src[i + 1]); i += 2; continue; }
            if (c == '(' || hex)
            {
                parser.Position = i;
                var s = (PdfString)parser.ReadObject();
                PdfSerializer.WriteString(b, EncryptData(s.Bytes), hex: true);
                i = parser.Position;
                continue;
            }
            b.Byte(c);
            i++;
        }
        return b.Span.ToArray();
    }

    private static bool HasHexString(ReadOnlySpan<byte> body)
    {
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '<') continue;
            if (i + 1 < body.Length && body[i + 1] == '<') { i++; continue; }
            return true;
        }
        return false;
    }
}
