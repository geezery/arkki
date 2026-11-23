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
    private static readonly HashSet<string> DroppedPageKeys = ["Type", "Parent", "Thumb", "PieceInfo", "B", "Resources", "MediaBox", "CropBox", "Rotate", "Contents", "Annots"];
    private static readonly string[] MergedResourceKinds = ["Font", "XObject", "ExtGState"];

    private readonly Dictionary<(PdfReader, int), int> _written = [];
    private readonly Dictionary<(PdfReader, int), int> _placeholders = [];
    private readonly HashSet<(PdfReader, int)> _inProgress = [];
    private readonly Dictionary<string, int> _byContent = [];
    private int _openState, _closeState;

    /// <summary>Objects to write in place of the source's: a filled form field with its new value and appearance.</summary>
    public Dictionary<(PdfReader, int), PdfValue> Overrides { get; set; } = [];

    /// <summary>Number of objects that were not written because an identical one already was.</summary>
    public int DeduplicatedObjects { get; private set; }

    /// <summary>Tells the importer the output number of an imported page, so links between imported pages survive.</summary>
    public void RegisterPage(PdfReader reader, ReaderPage page, int outputId) => _written[(reader, page.Ref.Number)] = outputId;

    /// <summary>Serializes a value of a read document, importing what it references.</summary>
    public string MapValue(PdfReader reader, PdfValue value)
    {
        var b = new ByteBuf(32);
        PdfSerializer.Write(b, value, r => MapRef(reader, r));
        return System.Text.Encoding.Latin1.GetString(b.Span);
    }

    public void WritePage(PdfReader reader, ReaderPage page, int outputId, int parentId, PageExtras extras)
    {
        var b = new ByteBuf(256);
        b.Ascii("<< /Type /Page /Parent ").Int(parentId).Ascii(" 0 R");
        WriteEntry(b, reader, "MediaBox", page.MediaBox ?? page.CropBox);
        WriteEntry(b, reader, "CropBox", page.CropBox);
        if (extras.Rotation != 0) b.Ascii(" /Rotate ").Int(extras.Rotation);

        bool stamped = extras.OverlayContentId > 0;
        WriteResources(b, reader, page.Resources, stamped ? extras.Resources : null);
        WriteContents(b, reader, page.Dict.Get("Contents"), extras.OverlayContentId);

        var annots = new List<string>();
        if (reader.Resolve(page.Dict.Get("Annots")) is PdfArray source)
            foreach (PdfValue a in source.Items) annots.Add(MapValue(reader, a));
        foreach (int id in extras.Annotations) annots.Add($"{id} 0 R");
        if (annots.Count > 0) b.Ascii(" /Annots [").Ascii(string.Join(" ", annots)).Ascii("]");

        foreach (var e in page.Dict.Entries)
        {
            if (DroppedPageKeys.Contains(e.Key)) continue;
            WriteEntry(b, reader, e.Key, e.Value);
        }
        b.Ascii(" >>");
        writer.WriteObject(outputId, b.Span);
    }

    /// <summary>
    /// The page's contents, and a stamp drawn after them. The page's own operators are wrapped in q/Q so a graphics
    /// state they leave behind (a transformation, a clip) does not move or hide the stamp.
    /// </summary>
    private void WriteContents(ByteBuf b, PdfReader reader, PdfValue? contents, int overlayId)
    {
        if (overlayId == 0)
        {
            if (contents is not null) WriteEntry(b, reader, "Contents", contents);
            return;
        }
        if (_openState == 0)
        {
            _openState = writer.Reserve();
            writer.WriteStream(_openState, "", "q\n"u8, compress: false);
            _closeState = writer.Reserve();
            writer.WriteStream(_closeState, "", "\nQ\n"u8, compress: false);
        }
        b.Ascii(" /Contents [").Int(_openState).Ascii(" 0 R ");
        switch (reader.Resolve(contents) is PdfArray ? reader.Resolve(contents) : contents)
        {
            case PdfArray parts:
                foreach (PdfValue part in parts.Items) b.Ascii(MapValue(reader, part)).Sp();
                break;
            case PdfRef single:
                b.Ascii(MapRef(reader, single)).Sp();
                break;
        }
        b.Int(_closeState).Ascii(" 0 R ").Int(overlayId).Ascii(" 0 R]");
    }

    private void WriteResources(ByteBuf b, PdfReader reader, PdfValue? resources, OwnResources? own)
    {
        // A page without resources inherits none in its new tree; an empty dictionary keeps it valid.
        if (own is null)
        {
            if (resources is null) b.Ascii(" /Resources << >>");
            else WriteEntry(b, reader, "Resources", resources);
            return;
        }
        var dict = reader.Resolve(resources) as PdfDict ?? new PdfDict([]);
        b.Ascii(" /Resources <<");
        PdfSerializer.WriteEntries(b, dict, r => MapRef(reader, r), k => MergedResourceKinds.Contains(k));
        foreach (string kind in MergedResourceKinds)
        {
            string mine = kind switch { "Font" => own.Fonts, "XObject" => own.XObjects, _ => own.States };
            var theirs = reader.Resolve(dict.Get(kind)) as PdfDict;
            if (mine.Length == 0 && theirs is null) continue;
            b.Ascii(" /").Ascii(kind).Ascii(" <<");
            if (theirs is not null) PdfSerializer.WriteEntries(b, theirs, r => MapRef(reader, r));
            b.Ascii(mine).Ascii(" >>");
        }
        b.Ascii(" >>");
    }

    private void WriteEntry(ByteBuf b, PdfReader reader, string key, PdfValue? value)
    {
        if (value is null) return;
        b.Ascii(" /").Ascii(key).Sp();
        PdfSerializer.Write(b, value, r => MapRef(reader, r));
    }

    private string MapRef(PdfReader reader, PdfRef r) =>
        IsForeignPage(reader, r) ? "null" : Import(reader, r.Number) + " 0 R";

    private PdfValue? Source(PdfReader reader, int number) =>
        Overrides.TryGetValue((reader, number), out PdfValue? o) ? o : reader.GetObject(number);

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

        PdfValue value = Source(reader, number) ?? PdfNull.Instance;
        _inProgress.Add(key);
        var b = new ByteBuf(256);
        byte[]? streamData = null;
        if (value is PdfStream stream) streamData = SerializeStreamHead(b, reader, stream);
        else PdfSerializer.Write(b, value, r => MapRef(reader, r));
        _inProgress.Remove(key);

        int id;
        if (_placeholders.Remove(key, out int placeholder))
        {
            id = placeholder;
            Write(id, b, streamData);
        }
        else
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(b.Span);
            if (streamData is not null) hash.AppendData(streamData);
            string digest = Convert.ToHexString(hash.GetHashAndReset());
            if (_byContent.TryGetValue(digest, out id))
            {
                DeduplicatedObjects++;
            }
            else
            {
                id = writer.Reserve();
                Write(id, b, streamData);
                _byContent[digest] = id;
            }
        }
        _written[key] = id;
        return id;
    }

    private void Write(int id, ByteBuf head, byte[]? streamData)
    {
        if (streamData is null) writer.WriteObject(id, head.Span);
        else writer.WriteRawStream(id, head.Span, streamData);
    }

    /// <summary>Writes a stream's dictionary (without /Length, which the writer adds) and returns the data to store.</summary>
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
        return data;
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
}
