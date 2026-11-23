using System.IO.Compression;
using System.Security.Cryptography;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf.Merging;

/// <summary>
/// Copies pages and everything they reference from read documents into the document being
/// written. Objects are copied once per source, and objects that serialize to identical bytes
/// (the same logo or font subset in every invoice of a batch) are written only once in total.
/// Stream data is copied in its stored, already compressed form.
/// </summary>
internal sealed class PageImporter(PdfWriter writer, CompressionLevel compression)
{
    // Page-level keys that only matter to the source document or cost space for nothing.
    private static readonly HashSet<string> DroppedPageKeys = ["Parent", "Thumb", "PieceInfo", "B", "Resources", "MediaBox", "CropBox", "Rotate"];

    private readonly Dictionary<(PdfReader, int), int> _written = [];
    private readonly Dictionary<(PdfReader, int), int> _placeholders = [];
    private readonly HashSet<(PdfReader, int)> _inProgress = [];
    private readonly Dictionary<string, int> _byContent = [];

    /// <summary>Number of objects that were not written because an identical one already was.</summary>
    public int DeduplicatedObjects { get; private set; }

    /// <summary>Tells the importer the output number of an imported page, so links between imported pages survive.</summary>
    public void RegisterPage(PdfReader reader, ReaderPage page, int outputId) => _written[(reader, page.Ref.Number)] = outputId;

    public void WritePage(PdfReader reader, ReaderPage page, int outputId, int parentId)
    {
        var b = new ByteBuf(256);
        b.Ascii("<< /Type /Page /Parent ").Int(parentId).Ascii(" 0 R");
        WriteEntry(b, reader, "MediaBox", page.MediaBox ?? page.CropBox);
        WriteEntry(b, reader, "CropBox", page.CropBox);
        WriteEntry(b, reader, "Rotate", page.Rotate);
        // A page without resources inherits none in its new tree; an empty dictionary keeps it valid.
        if (page.Resources is null) b.Ascii(" /Resources << >>");
        else WriteEntry(b, reader, "Resources", page.Resources);
        foreach (var e in page.Dict.Entries)
        {
            if (e.Key == "Type" || DroppedPageKeys.Contains(e.Key)) continue;
            WriteEntry(b, reader, e.Key, e.Value);
        }
        b.Ascii(" >>");
        writer.WriteObject(outputId, b.Span);
    }

    private void WriteEntry(ByteBuf b, PdfReader reader, string key, PdfValue? value)
    {
        if (value is null) return;
        b.Ascii(" /").Ascii(key).Sp();
        Serialize(b, reader, value, 0);
    }

    /// <summary>Copies an indirect object and returns its number in the output.</summary>
    private int Import(PdfReader reader, int number)
    {
        var key = (reader, number);
        if (_written.TryGetValue(key, out int done)) return done;
        if (_inProgress.Contains(key))
        {
            // A cycle (an annotation pointing back to its parent, say): number it now, write it later.
            if (!_placeholders.TryGetValue(key, out int ph)) _placeholders[key] = ph = writer.Reserve();
            return ph;
        }

        PdfValue value = reader.GetObject(number) ?? PdfNull.Instance;
        _inProgress.Add(key);
        var b = new ByteBuf(256);
        byte[]? streamData = null;
        if (value is PdfStream stream) streamData = SerializeStreamHead(b, reader, stream);
        else Serialize(b, reader, value, 0);
        _inProgress.Remove(key);

        if (streamData is not null) b.Ascii("\nstream\n").Bytes(streamData).Ascii("\nendstream");

        int id;
        if (_placeholders.Remove(key, out int placeholder))
        {
            id = placeholder;
            writer.WriteObject(id, b.Span);
        }
        else
        {
            string hash = Convert.ToHexString(SHA256.HashData(b.Span));
            if (_byContent.TryGetValue(hash, out id))
            {
                DeduplicatedObjects++;
            }
            else
            {
                id = writer.Reserve();
                writer.WriteObject(id, b.Span);
                _byContent[hash] = id;
            }
        }
        _written[key] = id;
        return id;
    }

    /// <summary>Writes a stream's dictionary with a direct /Length and returns the data to store.</summary>
    private byte[] SerializeStreamHead(ByteBuf b, PdfReader reader, PdfStream stream)
    {
        byte[] data = stream.Data.ToArray();
        bool hasFilter = stream.Get("Filter") is not (null or PdfNull);
        bool compress = false;
        if (!hasFilter && compression != CompressionLevel.NoCompression && data.Length > 64)
        {
            // Some producers store content uncompressed; compress it when that pays off.
            using var ms = new MemoryStream(data.Length / 2 + 64);
            using (var z = new ZLibStream(ms, compression, leaveOpen: true)) z.Write(data);
            if (ms.Length < data.Length) { data = ms.ToArray(); compress = true; }
        }

        b.Ascii("<<");
        foreach (var e in stream.Entries)
        {
            if (e.Key is "Length" || (compress && e.Key is "DecodeParms")) continue;
            WriteEntry(b, reader, e.Key, e.Value);
        }
        if (compress) b.Ascii(" /Filter /FlateDecode");
        b.Ascii(" /Length ").Int(data.Length).Ascii(" >>");
        return data;
    }

    private void Serialize(ByteBuf b, PdfReader reader, PdfValue value, int depth)
    {
        if (depth > 256) throw new InvalidDataException("Objects are nested too deeply.");
        switch (value)
        {
            case PdfNull: b.Ascii("null"); break;
            case PdfBool v: b.Ascii(v.Value ? "true" : "false"); break;
            case PdfNumber n: b.Ascii(n.Text); break;
            case PdfName n: b.Byte((byte)'/').Bytes(System.Text.Encoding.Latin1.GetBytes(n.Encoded)); break;
            case PdfString s: WriteString(b, s); break;
            case PdfRef r:
                if (IsForeignPage(reader, r)) { b.Ascii("null"); break; }
                b.Int(Import(reader, r.Number)).Ascii(" 0 R");
                break;
            case PdfArray a:
                b.Byte((byte)'[');
                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) b.Sp();
                    Serialize(b, reader, a.Items[i], depth + 1);
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
                    b.Ascii(" /").Bytes(System.Text.Encoding.Latin1.GetBytes(e.Key)).Sp();
                    Serialize(b, reader, e.Value, depth + 1);
                }
                b.Ascii(" >>");
                break;
        }
    }

    /// <summary>
    /// True for a reference to a page (or page tree node) that is not being imported. Following
    /// it would drag the whole source document in through links, outlines and /Parent entries.
    /// </summary>
    private bool IsForeignPage(PdfReader reader, PdfRef r)
    {
        if (_written.ContainsKey((reader, r.Number)) || _inProgress.Contains((reader, r.Number))) return false;
        return reader.GetObject(r.Number) is PdfDict d && (d.IsType("Page") || d.IsType("Pages"));
    }

    private static void WriteString(ByteBuf b, PdfString s)
    {
        if (s.Hex)
        {
            b.Byte((byte)'<');
            foreach (byte x in s.Bytes) b.Hex8(x);
            b.Byte((byte)'>');
            return;
        }
        b.Byte((byte)'(');
        foreach (byte x in s.Bytes)
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
}
