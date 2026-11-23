using System.Text;

namespace Arkki.Pdf.Reading;

/// <summary>
/// Reads an existing PDF file so that its pages can be imported into a <see cref="PdfDocument"/>.
/// Supports cross-reference tables and streams, object streams, incremental updates, and
/// rebuilds a damaged cross-reference by scanning the file. Encrypted files are rejected.
/// A reader is immutable after <see cref="Open(byte[])"/> apart from an internal object cache,
/// and is not thread-safe.
/// </summary>
public sealed class PdfReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly Dictionary<int, XrefEntry> _xref = [];
    private readonly Dictionary<int, PdfValue> _cache = [];
    private readonly Dictionary<int, (PdfParser Parser, int[] Offsets, int First)> _objectStreams = [];
    private readonly HashSet<int> _resolving = [];
    private PdfDict _trailer = new([]);
    private readonly List<ReaderPage> _pages = [];

    private PdfReader(ReadOnlyMemory<byte> data) => _data = data;

    /// <summary>The version from the file header, for example "1.7".</summary>
    public string Version { get; private set; } = "1.4";

    public int PageCount => _pages.Count;

    /// <summary>Page size in points, as the page's crop box (falling back to its media box) gives it, before rotation.</summary>
    public (double Width, double Height) GetPageSize(int index)
    {
        ReaderPage p = _pages[index];
        double[] box = p.Box;
        return (Math.Abs(box[2] - box[0]), Math.Abs(box[3] - box[1]));
    }

    /// <summary>The page's /Rotate, normalised to 0, 90, 180 or 270.</summary>
    public int GetPageRotation(int index)
    {
        int r = (Resolve(_pages[index].Rotate) as PdfNumber)?.IntValue ?? 0;
        r = ((r % 360) + 360) % 360;
        return r - r % 90;
    }

    /// <summary>
    /// The text of a page (zero-based), in drawing order, with a line break where the baseline
    /// moves. Meant for tests and search, not for layout reconstruction.
    /// </summary>
    public string ExtractText(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        return new TextExtractor(this).Extract(_pages[pageIndex]);
    }

    /// <summary>The text of every page, pages separated by a blank line.</summary>
    public string ExtractText() => string.Join("\n\n", Enumerable.Range(0, PageCount).Select(ExtractText));

    /// <summary>Reads a PDF file. An encrypted file opens with its user or owner password (or none, when its user password is empty).</summary>
    /// <exception cref="InvalidDataException">The data is not a PDF file, or is damaged beyond repair.</exception>
    /// <exception cref="PdfPasswordException">The file is encrypted and the password does not open it.</exception>
    public static PdfReader Open(byte[] data, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var reader = new PdfReader(data) { _password = password };
        reader.Load();
        return reader;
    }

    public static PdfReader Open(Stream stream, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Open(ms.ToArray(), password);
    }

    private string? _password;
    private Security.Decryptor? _decryptor;
    private int _encryptNumber = -1;

    /// <summary>True when the file is encrypted (and was opened with a password that works).</summary>
    public bool IsEncrypted => _decryptor is not null;

    /// <summary>What the file permits a reader without the owner password; <see cref="PdfPermissions.All"/> for an unencrypted file or the owner.</summary>
    public PdfPermissions Permissions => _decryptor is null || _decryptor.IsOwner ? PdfPermissions.All : (PdfPermissions)_decryptor.Permissions & PdfPermissions.All;

    internal IReadOnlyList<ReaderPage> Pages => _pages;

    internal PdfDict? Catalog => Resolve(_trailer.Get("Root")) as PdfDict;
    internal PdfDict? AcroForm => Resolve(Catalog?.Get("AcroForm")) as PdfDict;
    internal PdfDict Trailer => _trailer;
    internal ReadOnlyMemory<byte> Data => _data;
    internal double[]? ReadRect(PdfValue? value) => ReadBox(value);

    /// <summary>The document information: title, author and the rest, as the file states them.</summary>
    public PdfInfo Info
    {
        get
        {
            var info = new PdfInfo();
            if (Resolve(_trailer.Get("Info")) is not PdfDict d) return info;
            string? Text(string key) => Resolve(d.Get(key)) is PdfString str ? Internal.PdfSerializer.DecodeText(str.Bytes) : null;
            info.Title = Text("Title"); info.Author = Text("Author"); info.Subject = Text("Subject");
            info.Keywords = Text("Keywords"); info.Creator = Text("Creator"); info.Producer = Text("Producer");
            info.CreationDate = ParseDate(Text("CreationDate"));
            info.ModificationDate = ParseDate(Text("ModDate"));
            return info;
        }
    }

    /// <summary>The natural language the catalog declares, such as "fi-FI"; null when none.</summary>
    public string? Language => Resolve(Catalog?.Get("Lang")) is PdfString s ? Internal.PdfSerializer.DecodeText(s.Bytes) : null;

    /// <summary>Parses a PDF date (D:YYYYMMDDHHmmSSOHH'mm'); null when absent or malformed.</summary>
    internal static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string t = text.StartsWith("D:", StringComparison.Ordinal) ? text[2..] : text;
        int Part(int at, int len, int fallback) => t.Length >= at + len && int.TryParse(t.AsSpan(at, len), out int v) ? v : fallback;
        try
        {
            int year = Part(0, 4, -1);
            if (year < 1) return null;
            var offset = TimeSpan.Zero;
            if (t.Length > 14 && t[14] is '+' or '-')
            {
                int oh = Part(15, 2, 0), om = t.Length >= 20 ? Part(18, 2, 0) : 0;
                offset = new TimeSpan(oh, om, 0) * (t[14] == '-' ? -1 : 1);
            }
            return new DateTimeOffset(year, Part(4, 2, 1), Part(6, 2, 1), Part(8, 2, 0), Part(10, 2, 0), Part(12, 2, 0), offset);
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The form fields (AcroForm) of the document, by their fully qualified names.</summary>
    public IReadOnlyList<PdfFormField> GetFormFields()
    {
        var fields = new List<PdfFormField>();
        if (AcroForm is { } form && Resolve(form.Get("Fields")) is PdfArray top)
        {
            var visited = new HashSet<int>();
            foreach (PdfValue f in top.Items) CollectField(f, null, null, null, null, fields, visited, 0);
        }
        return fields;
    }

    private void CollectField(PdfValue node, string? parentName, string? ft, PdfValue? ff, PdfValue? v, List<PdfFormField> fields, HashSet<int> visited, int depth)
    {
        if (depth > 32 || node is not PdfRef nr || !visited.Add(nr.Number) || Resolve(nr) is not PdfDict d) return;
        string? partial = Resolve(d.Get("T")) is PdfString t ? Internal.PdfSerializer.DecodeText(t.Bytes) : null;
        string? name = partial is null ? parentName : parentName is null ? partial : parentName + "." + partial;
        ft = (Resolve(d.Get("FT")) as PdfName)?.Encoded ?? ft;
        ff = d.Get("Ff") ?? ff;
        v = d.Get("V") ?? v;

        var kids = (Resolve(d.Get("Kids")) as PdfArray)?.Items ?? [];
        var fieldKids = kids.Where(k => Resolve(k) is PdfDict kd && kd.Get("T") is not null).ToList();
        if (fieldKids.Count > 0)
        {
            foreach (PdfValue k in fieldKids) CollectField(k, name, ft, ff, v, fields, visited, depth + 1);
            return;
        }
        if (name is null) return;

        // A terminal field: its widgets are its kids without names, or the field itself.
        var widgets = kids.OfType<PdfRef>().Select(k => k.Number).ToList();
        if (widgets.Count == 0) widgets.Add(nr.Number);

        int flags = (Resolve(ff) as PdfNumber)?.IntValue ?? 0;
        PdfFieldKind kind = ft switch
        {
            "Tx" => PdfFieldKind.Text,
            "Ch" => PdfFieldKind.Choice,
            "Sig" => PdfFieldKind.Signature,
            "Btn" => (flags & (1 << 16)) != 0 ? PdfFieldKind.PushButton : (flags & (1 << 15)) != 0 ? PdfFieldKind.RadioButton : PdfFieldKind.CheckBox,
            _ => PdfFieldKind.Unknown,
        };
        string? value = Resolve(v) switch
        {
            PdfString s => Internal.PdfSerializer.DecodeText(s.Bytes),
            PdfName n => n.Encoded,
            PdfArray a when a.Items.Count > 0 && Resolve(a.Items[0]) is PdfString s0 => Internal.PdfSerializer.DecodeText(s0.Bytes),
            _ => null,
        };

        var options = new List<string>();
        if (kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton)
        {
            foreach (int w in widgets)
                if (Resolve(GetObject(w)) is PdfDict wd && Resolve(wd.Get("AP")) is PdfDict ap && Resolve(ap.Get("N")) is PdfDict n)
                    foreach (var e in n.Entries) if (e.Key != "Off" && !options.Contains(e.Key)) options.Add(e.Key);
        }
        else if (kind == PdfFieldKind.Choice && Resolve(d.Get("Opt")) is PdfArray opt)
        {
            foreach (PdfValue o in opt.Items)
            {
                PdfValue? item = Resolve(o) is PdfArray pair && pair.Items.Count == 2 ? Resolve(pair.Items[1]) : Resolve(o);
                if (item is PdfString os) options.Add(Internal.PdfSerializer.DecodeText(os.Bytes));
            }
        }

        double fontSize = 0;
        if (Resolve(d.Get("DA")) is PdfString da)
        {
            string[] parts = Encoding.Latin1.GetString(da.Bytes).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int tf = Array.IndexOf(parts, "Tf");
            if (tf >= 1) double.TryParse(parts[tf - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fontSize);
        }

        fields.Add(new PdfFormField(name, kind, value, (flags & 1) != 0, options)
        {
            Reader = this, ObjectNumber = nr.Number, Widgets = widgets, FontSize = fontSize, Multiline = (flags & (1 << 12)) != 0,
        });
    }

    // ---- Loading -----------------------------------------------------------------------------

    private void Load()
    {
        ReadOnlySpan<byte> s = _data.Span;
        int header = s[..Math.Min(s.Length, 1024)].IndexOf("%PDF-"u8);
        if (header < 0) throw new InvalidDataException("Not a PDF file: the %PDF header is missing.");
        int v = header + 5, ve = v;
        while (ve < s.Length && ve < v + 4 && (char.IsAsciiDigit((char)s[ve]) || s[ve] == '.')) ve++;
        if (ve > v) Version = Encoding.ASCII.GetString(s[v..ve]);

        bool ok = false;
        try { ok = LoadCrossReference() && LoadPages(); }
        catch (Exception e) when (IsDamage(e)) { }
        if (!ok)
        {
            // The cross-reference is missing or wrong: rebuild it by scanning the file.
            _xref.Clear(); _cache.Clear(); _objectStreams.Clear(); _objStmNumbers.Clear(); _pages.Clear();
            RebuildCrossReference();
            if (!LoadPages()) throw new InvalidDataException("The PDF file has no document catalog.");
        }
    }

    private static bool IsDamage(Exception e) =>
        e is InvalidDataException or FormatException or OverflowException or ArgumentException or IndexOutOfRangeException;

    private bool LoadPages()
    {
        if (_trailer.Get("Encrypt") is not (null or PdfNull) && _decryptor is null)
        {
            if (_trailer.Get("Encrypt") is PdfRef er) _encryptNumber = er.Number;
            if (Resolve(_trailer.Get("Encrypt")) is not PdfDict enc) throw new InvalidDataException("The encryption dictionary is missing.");
            byte[] id0 = Resolve(_trailer.Get("ID")) is PdfArray ids && ids.Items.Count > 0 && Resolve(ids.Items[0]) is PdfString s0 ? s0.Bytes : [];
            _decryptor = new Security.Decryptor(enc, id0, _password, Resolve);
            // Objects read so far were read encrypted; read them again.
            _cache.Clear();
            _objectStreams.Clear();
        }
        if (Resolve(_trailer.Get("Root")) is not PdfDict root || root.Get("Pages") is not PdfRef pagesRef) return false;
        CollectPages(pagesRef, new Inherited(null, null, null, null), new HashSet<int>(), 0);
        return true;
    }

    private bool LoadCrossReference()
    {
        ReadOnlySpan<byte> s = _data.Span;
        int tail = Math.Max(0, s.Length - 2048);
        int at = s[tail..].LastIndexOf("startxref"u8);
        if (at < 0) return false;
        var p = new PdfParser(_data) { Position = tail + at + 9 };
        string token = p.ReadToken();
        if (!long.TryParse(token, out long offset)) return false;

        var seen = new HashSet<long>();
        bool first = true;
        while (offset > 0 && offset < s.Length && seen.Add(offset))
        {
            PdfDict trailer = ReadXrefSection((int)offset);
            if (first) { _trailer = trailer; first = false; }
            // A hybrid file's table points to a cross-reference stream with the compressed objects.
            if (trailer.Get("XRefStm") is PdfNumber stm && seen.Add(stm.LongValue)) ReadXrefSection(stm.IntValue);
            offset = trailer.Get("Prev") is PdfNumber prev ? prev.LongValue : 0;
        }
        return !first;
    }

    /// <summary>Reads one cross-reference section (table or stream) and returns its trailer dictionary.</summary>
    private PdfDict ReadXrefSection(int offset)
    {
        var p = new PdfParser(_data) { Position = offset };
        if (p.TryReadKeyword("xref"))
        {
            while (true)
            {
                int saved = p.Position;
                string startTok = p.ReadToken();
                if (startTok == "trailer") break;
                string countTok = p.ReadToken();
                if (!PdfParser.IsInteger(startTok) || !PdfParser.IsInteger(countTok))
                {
                    p.Position = saved;
                    throw new InvalidDataException("Malformed cross-reference table.");
                }
                int start = int.Parse(startTok), count = int.Parse(countTok);
                for (int i = 0; i < count; i++)
                {
                    string off = p.ReadToken(), gen = p.ReadToken(), kind = p.ReadToken();
                    int num = start + i;
                    if (_xref.ContainsKey(num)) continue; // a newer section already defined it
                    if (kind == "n" && long.TryParse(off, out long o) && o > 0)
                        _xref[num] = new XrefEntry(1, o, int.Parse(gen));
                    else
                        _xref[num] = new XrefEntry(0, 0, 0);
                }
            }
            return p.ReadObject() as PdfDict ?? throw new InvalidDataException("Malformed trailer.");
        }

        // A cross-reference stream (PDF 1.5).
        p.Position = offset;
        p.ReadObjectHeader();
        if (p.ReadObject(allowStream: true) is not PdfStream xs || !xs.IsType("XRef"))
            throw new InvalidDataException("startxref does not point to a cross-reference section.");
        byte[] data = StreamDecoder.Decode(xs, Resolve);
        int[] w = (xs.Get("W") as PdfArray)?.Items.Select(i => (i as PdfNumber)?.IntValue ?? 0).ToArray() ?? [];
        if (w.Length < 3) throw new InvalidDataException("Cross-reference stream has no valid /W.");
        int size = (xs.Get("Size") as PdfNumber)?.IntValue ?? 0;
        List<int> index = (xs.Get("Index") as PdfArray)?.Items.Select(i => (i as PdfNumber)?.IntValue ?? 0).ToList() ?? [0, size];
        int entryLength = w[0] + w[1] + w[2], pos = 0;
        for (int k = 0; k + 1 < index.Count; k += 2)
        {
            for (int i = 0; i < index[k + 1] && pos + entryLength <= data.Length; i++, pos += entryLength)
            {
                long type = w[0] == 0 ? 1 : Field(data, pos, w[0]);
                long f2 = Field(data, pos + w[0], w[1]);
                long f3 = Field(data, pos + w[0] + w[1], w[2]);
                int num = index[k] + i;
                if (_xref.ContainsKey(num)) continue;
                _xref[num] = type switch
                {
                    1 => new XrefEntry(1, f2, (int)f3),
                    2 => new XrefEntry(2, f2, (int)f3),
                    _ => new XrefEntry(0, 0, 0),
                };
            }
        }
        return xs;
    }

    private static long Field(byte[] data, int pos, int width)
    {
        long v = 0;
        for (int i = 0; i < width; i++) v = (v << 8) | data[pos + i];
        return v;
    }

    /// <summary>Recovery for damaged files: finds every "n g obj" and the last trailer or cross-reference stream.</summary>
    private void RebuildCrossReference()
    {
        ReadOnlySpan<byte> s = _data.Span;
        PdfDict? trailer = null;
        int pos = 0;
        while (true)
        {
            int rel = s[pos..].IndexOf("obj"u8);
            if (rel < 0) break;
            int at = pos + rel;
            pos = at + 3;
            if (pos < s.Length && !PdfParser.IsWhite(s[pos]) && !PdfParser.IsDelimiter(s[pos])) continue;
            // Walk back over "n g " before the keyword.
            int i = at - 1;
            while (i >= 0 && PdfParser.IsWhite(s[i])) i--;
            int genEnd = i + 1;
            while (i >= 0 && char.IsAsciiDigit((char)s[i])) i--;
            int genStart = i + 1;
            if (genStart == genEnd) continue;
            while (i >= 0 && PdfParser.IsWhite(s[i])) i--;
            int numEnd = i + 1;
            while (i >= 0 && char.IsAsciiDigit((char)s[i])) i--;
            int numStart = i + 1;
            if (numStart == numEnd || numEnd - numStart > 10) continue;
            if (!int.TryParse(s[numStart..numEnd], out int num) || !int.TryParse(s[genStart..genEnd], out int gen)) continue;
            _xref[num] = new XrefEntry(1, numStart, gen); // later definitions win, as in an update
        }

        // Objects inside object streams, and the trailer: take the last ones found.
        foreach (int num in _xref.Keys.ToList())
        {
            PdfValue? obj;
            try { obj = GetObject(num); } catch (Exception e) when (IsDamage(e)) { continue; }
            if (obj is PdfStream st && st.IsType("ObjStm"))
            {
                try
                {
                    var (_, offsets, _) = OpenObjectStream(num);
                    for (int k = 0; k < offsets.Length; k++)
                        _xref.TryAdd(ObjectStreamNumber(num, k), new XrefEntry(2, num, k));
                }
                catch (Exception e) when (IsDamage(e)) { }
            }
            if (obj is PdfStream xs && xs.IsType("XRef") && xs.Get("Root") is not null) trailer = xs;
        }
        int t = s.LastIndexOf("trailer"u8);
        if (t >= 0)
        {
            try
            {
                var p = new PdfParser(_data) { Position = t + 7 };
                if (p.ReadObject() is PdfDict d && d.Get("Root") is not null) trailer = d;
            }
            catch (Exception e) when (IsDamage(e)) { }
        }
        if (trailer is null)
        {
            // Last resort: any catalog object.
            foreach (int num in _xref.Keys)
            {
                PdfValue? o;
                try { o = GetObject(num); } catch (Exception e) when (IsDamage(e)) { continue; }
                if (o is PdfDict d && d.IsType("Catalog")) { trailer = new PdfDict([new("Root", new PdfRef(num, 0))]); break; }
            }
        }
        _trailer = trailer ?? throw new InvalidDataException("The PDF file is damaged beyond repair: no document catalog was found.");
    }

    private readonly Dictionary<(int Stream, int Index), int> _objStmNumbers = [];
    private int ObjectStreamNumber(int stream, int index) => _objStmNumbers.TryGetValue((stream, index), out int n) ? n : -1;

    // ---- Objects -----------------------------------------------------------------------------

    /// <summary>Follows references until a direct value; returns null for missing objects.</summary>
    internal PdfValue? Resolve(PdfValue? value)
    {
        for (int hops = 0; value is PdfRef r && hops < 32; hops++) value = GetObject(r.Number);
        return value is PdfRef ? null : value;
    }

    internal PdfValue? GetObject(int number)
    {
        if (_cache.TryGetValue(number, out PdfValue? cached)) return cached;
        if (!_xref.TryGetValue(number, out XrefEntry e) || e.Type == 0) return null;
        if (!_resolving.Add(number)) return null; // a /Length that refers back to its own stream
        try
        {
            PdfValue value;
            if (e.Type == 1)
            {
                var p = new PdfParser(_data) { Position = (int)e.Offset, ResolveLength = r => Resolve(r) };
                var (num, gen) = p.ReadObjectHeader();
                if (num != number) throw new InvalidDataException($"Object {number} is not at its cross-reference offset.");
                value = p.ReadObject(allowStream: true);
                if (_decryptor is not null && number != _encryptNumber) value = Decrypt(value, number, gen, 0);
            }
            else
            {
                var (parser, offsets, first) = OpenObjectStream((int)e.Offset);
                if (e.Index >= offsets.Length) return null;
                parser.Position = first + offsets[e.Index];
                value = parser.ReadObject();
            }
            _cache[number] = value;
            return value;
        }
        finally { _resolving.Remove(number); }
    }

    private PdfValue Decrypt(PdfValue value, int num, int gen, int depth)
    {
        if (depth > Internal.PdfSerializer.MaxDepth) throw new InvalidDataException("Objects are nested too deeply.");
        switch (value)
        {
            case PdfString s:
                return new PdfString(_decryptor!.DecryptString(s.Bytes, num, gen), s.Hex);
            case PdfArray a:
                return new PdfArray([.. a.Items.Select(i => Decrypt(i, num, gen, depth + 1))]);
            case PdfStream st:
            {
                var entries = st.Entries.Select(e => new KeyValuePair<string, PdfValue>(e.Key, Decrypt(e.Value, num, gen, depth + 1))).ToList();
                bool plain = st.IsType("XRef") || (st.IsType("Metadata") && !_decryptor!.EncryptMetadata);
                return new PdfStream(entries, plain ? st.Data : _decryptor!.DecryptStream(st.Data.ToArray(), num, gen));
            }
            case PdfDict d:
                return new PdfDict([.. d.Entries.Select(e => new KeyValuePair<string, PdfValue>(e.Key, Decrypt(e.Value, num, gen, depth + 1)))]);
            default:
                return value;
        }
    }

    private (PdfParser Parser, int[] Offsets, int First) OpenObjectStream(int number)
    {
        if (_objectStreams.TryGetValue(number, out var open)) return open;
        if (GetObject(number) is not PdfStream st) throw new InvalidDataException($"Object stream {number} is missing.");
        byte[] data = StreamDecoder.Decode(st, Resolve);
        int n = (Resolve(st.Get("N")) as PdfNumber)?.IntValue ?? 0;
        int first = (Resolve(st.Get("First")) as PdfNumber)?.IntValue ?? 0;
        var p = new PdfParser(data);
        var offsets = new int[n];
        for (int i = 0; i < n; i++)
        {
            string objNum = p.ReadToken(), off = p.ReadToken();
            if (!int.TryParse(objNum, out int on) || !int.TryParse(off, out offsets[i]))
                throw new InvalidDataException($"Object stream {number} has a malformed header.");
            _objStmNumbers[(number, i)] = on;
        }
        var result = (p, offsets, first);
        _objectStreams[number] = result;
        return result;
    }

    // ---- Pages -------------------------------------------------------------------------------

    private sealed record Inherited(PdfValue? Resources, PdfValue? MediaBox, PdfValue? CropBox, PdfValue? Rotate);

    private void CollectPages(PdfRef nodeRef, Inherited inherited, HashSet<int> visited, int depth)
    {
        if (depth > 64 || !visited.Add(nodeRef.Number)) return;
        if (Resolve(nodeRef) is not PdfDict node) return;
        var here = new Inherited(
            node.Get("Resources") ?? inherited.Resources,
            node.Get("MediaBox") ?? inherited.MediaBox,
            node.Get("CropBox") ?? inherited.CropBox,
            node.Get("Rotate") ?? inherited.Rotate);
        if (Resolve(node.Get("Kids")) is PdfArray kids && !node.IsType("Page"))
        {
            foreach (PdfValue kid in kids.Items)
                if (kid is PdfRef kr) CollectPages(kr, here, visited, depth + 1);
            return;
        }
        double[] box = ReadBox(here.CropBox) ?? ReadBox(here.MediaBox) ?? [0, 0, 612, 792];
        _pages.Add(new ReaderPage(nodeRef, node, here.Resources, here.MediaBox, here.CropBox, here.Rotate, box));
    }

    private double[]? ReadBox(PdfValue? value) =>
        Resolve(value) is PdfArray a && a.Items.Count == 4 && a.Items.All(i => Resolve(i) is PdfNumber)
            ? [.. a.Items.Select(i => ((PdfNumber)Resolve(i)!).Value)]
            : null;

    private readonly record struct XrefEntry(byte Type, long Offset, int Index);
}

/// <summary>A page of a read document, with the attributes it inherits from the page tree.</summary>
internal sealed record ReaderPage(PdfRef Ref, PdfDict Dict, PdfValue? Resources, PdfValue? MediaBox, PdfValue? CropBox, PdfValue? Rotate, double[] Box);
