using System.Text;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf.Internal;

/// <summary>Writes parsed PDF values back to bytes. References go through a caller-supplied mapping.</summary>
internal static class PdfSerializer
{
    public const int MaxDepth = 256;

    /// <param name="mapRef">Turns a reference of the read file into the text written for it ("12 0 R" or "null").</param>
    /// <param name="transformString">Applied to every string's bytes, for encryption; null keeps them.</param>
    public static void Write(ByteBuf b, PdfValue value, Func<PdfRef, string> mapRef, Func<byte[], byte[]>? transformString = null, int depth = 0)
    {
        if (depth > MaxDepth) throw new InvalidDataException("Objects are nested too deeply.");
        switch (value)
        {
            case PdfNull: b.Ascii("null"); break;
            case PdfBool v: b.Ascii(v.Value ? "true" : "false"); break;
            case PdfNumber n: b.Ascii(n.Text); break;
            case PdfName n: b.Byte((byte)'/').Bytes(Encoding.Latin1.GetBytes(n.Encoded)); break;
            case PdfString s:
                if (transformString is null) WriteString(b, s.Bytes, s.Hex);
                else WriteString(b, transformString(s.Bytes), hex: true);
                break;
            case PdfOutputRef o: b.Int(o.Id).Ascii(" 0 R"); break;
            case PdfRef r: b.Ascii(mapRef(r)); break;
            case PdfArray a:
                b.Byte((byte)'[');
                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) b.Sp();
                    Write(b, a.Items[i], mapRef, transformString, depth + 1);
                }
                b.Byte((byte)']');
                break;
            case PdfStream:
                // A stream can only be indirect; a direct one is malformed input.
                b.Ascii("null");
                break;
            case PdfDict d:
                b.Ascii("<<");
                foreach (var e in d.Entries)
                {
                    b.Ascii(" /").Bytes(Encoding.Latin1.GetBytes(e.Key)).Sp();
                    Write(b, e.Value, mapRef, transformString, depth + 1);
                }
                b.Ascii(" >>");
                break;
        }
    }

    /// <summary>Writes the entries of a dictionary without the brackets, skipping <paramref name="skip"/>.</summary>
    public static void WriteEntries(ByteBuf b, PdfDict d, Func<PdfRef, string> mapRef, Func<string, bool>? skip = null, Func<byte[], byte[]>? transformString = null)
    {
        foreach (var e in d.Entries)
        {
            if (skip?.Invoke(e.Key) == true) continue;
            b.Ascii(" /").Bytes(Encoding.Latin1.GetBytes(e.Key)).Sp();
            Write(b, e.Value, mapRef, transformString, 1);
        }
    }

    public static void WriteString(ByteBuf b, ReadOnlySpan<byte> bytes, bool hex)
    {
        if (hex)
        {
            b.Byte((byte)'<');
            foreach (byte x in bytes) b.Hex8(x);
            b.Byte((byte)'>');
            return;
        }
        b.Byte((byte)'(');
        foreach (byte x in bytes)
        {
            switch (x)
            {
                case (byte)'(' or (byte)')' or (byte)'\\': b.Byte((byte)'\\').Byte(x); break;
                case 10: b.Ascii("\\n"); break;
                case 13: b.Ascii("\\r"); break;
                default: b.Byte(x); break;
            }
        }
        b.Byte((byte)')');
    }

    /// <summary>A PDF text string for any Unicode text: PDFDocEncoding-compatible Latin-1 when possible, else UTF-16BE.</summary>
    public static byte[] TextBytes(string s)
    {
        bool latin = true;
        foreach (char c in s) if (c > 0x7E || c < 0x20 && c is not ('\n' or '\r' or '\t')) { latin = false; break; }
        if (latin) return Encoding.ASCII.GetBytes(s);
        var bytes = new byte[2 + s.Length * 2];
        bytes[0] = 0xFE; bytes[1] = 0xFF;
        for (int i = 0; i < s.Length; i++) { bytes[2 + 2 * i] = (byte)(s[i] >> 8); bytes[3 + 2 * i] = (byte)s[i]; }
        return bytes;
    }

    /// <summary>Decodes a PDF text string (UTF-16BE with BOM, UTF-8 with BOM, or PDFDocEncoding read as Latin-1).</summary>
    public static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.Latin1.GetString(bytes);
    }
}
