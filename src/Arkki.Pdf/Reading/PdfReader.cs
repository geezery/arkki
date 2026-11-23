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

    public static PdfReader Open(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var reader = new PdfReader(data);
        reader.Load();
        return reader;
    }

    public static PdfReader Open(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Open(ms.ToArray());
    }

    internal IReadOnlyList<ReaderPage> Pages => _pages;

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
        if (_trailer.Get("Encrypt") is not (null or PdfNull))
            throw new NotSupportedException("Encrypted PDF files are not supported.");
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
                var (num, _) = p.ReadObjectHeader();
                if (num != number) throw new InvalidDataException($"Object {number} is not at its cross-reference offset.");
                value = p.ReadObject(allowStream: true);
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
