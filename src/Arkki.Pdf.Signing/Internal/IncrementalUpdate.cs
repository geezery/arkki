using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf.Signing.Internal;

/// <summary>
/// An incremental update of a PDF: new and replaced objects appended after the original bytes with their own
/// cross-reference section, so everything already in the file, signatures included, stays byte for byte as it was.
/// </summary>
internal sealed class IncrementalUpdate
{
    private readonly byte[] _original;
    private readonly SortedDictionary<int, (int Generation, byte[] Body)> _changed = [];
    private readonly int _size;
    private int _next;

    public IncrementalUpdate(PdfReader reader, byte[] original)
    {
        Reader = reader;
        _original = original;
        PdfDict trailer = reader.Trailer;
        _size = (trailer.Get("Size") as PdfNumber)?.IntValue ?? throw new InvalidDataException("The trailer has no /Size.");
        _next = _size;
        RootRef = trailer.Get("Root") as PdfRef ?? throw new InvalidDataException("The trailer has no /Root reference.");
        Catalog = reader.Catalog ?? throw new InvalidDataException("The document catalog is missing.");
    }

    public PdfReader Reader { get; }
    public PdfRef RootRef { get; }

    /// <summary>The catalog as this update leaves it; change it with <see cref="SetCatalog"/>.</summary>
    public PdfDict Catalog { get; private set; }

    /// <summary>The number of a new object.</summary>
    public int Reserve() => _next++;

    public static string MapRef(PdfRef r) => $"{r.Number} {r.Generation} R";

    /// <summary>Replaces an object of the file, or gives a reserved number its value.</summary>
    public void Put(int id, int generation, PdfValue value)
    {
        var b = new ByteBuf(256);
        PdfSerializer.Write(b, value, MapRef);
        _changed[id] = (generation, b.Span.ToArray());
    }

    /// <summary>Gives a reserved number an object written by hand.</summary>
    public void Put(int id, ReadOnlySpan<byte> body) => _changed[id] = (0, body.ToArray());

    /// <summary>Gives a reserved number a stream; <paramref name="dictionary"/> is its entries without /Length.</summary>
    public void PutStream(int id, string dictionary, ReadOnlySpan<byte> data)
    {
        var b = new ByteBuf(data.Length + dictionary.Length + 64);
        b.Ascii("<< ").Ascii(dictionary).Ascii(" /Length ").Int(data.Length).Ascii(" >>\nstream\n").Bytes(data).Ascii("\nendstream");
        _changed[id] = (0, b.Span.ToArray());
    }

    public void SetCatalog(PdfDict catalog)
    {
        Catalog = catalog;
        Put(RootRef.Number, RootRef.Generation, catalog);
    }

    /// <summary>
    /// Copies a value of another file into this update: every object it refers to gets a new number, once. Returns the
    /// value with its references pointing at the copies.
    /// </summary>
    public PdfValue Import(PdfReader source, PdfValue value, Dictionary<int, int> copied, int depth = 0)
    {
        if (depth > PdfSerializer.MaxDepth) throw new InvalidDataException("Objects are nested too deeply.");
        switch (value)
        {
            case PdfRef r:
                if (copied.TryGetValue(r.Number, out int known)) return new PdfOutputRef(known);
                if (source.Resolve(r) is not { } target) return PdfNull.Instance;
                int id = Reserve();
                copied[r.Number] = id;
                if (target is PdfStream stream) PutStream(id, ImportEntries(source, stream, copied, depth), stream.Data.Span);
                else Put(id, 0, Import(source, target, copied, depth + 1));
                return new PdfOutputRef(id);
            case PdfArray a:
                return new PdfArray([.. a.Items.Select(i => Import(source, i, copied, depth + 1))]);
            case PdfDict d:
                return new PdfDict([.. d.Entries.Select(e => new KeyValuePair<string, PdfValue>(e.Key, Import(source, e.Value, copied, depth + 1)))]);
            default:
                return value;
        }
    }

    /// <summary>A stream's dictionary entries, references copied, without /Length (the data is copied as stored).</summary>
    public string ImportEntries(PdfReader source, PdfStream stream, Dictionary<int, int> copied, int depth = 0)
    {
        var b = new ByteBuf(128);
        foreach (var e in stream.Entries)
        {
            if (e.Key == "Length") continue;
            b.Ascii("/").Bytes(Encoding.Latin1.GetBytes(e.Key)).Sp();
            PdfSerializer.Write(b, Import(source, e.Value, copied, depth + 1), MapRef);
            b.Sp();
        }
        return Encoding.Latin1.GetString(b.Span);
    }

    /// <summary>
    /// Writes the file with the update appended. <paramref name="bodyStart"/> receives, for each object of the update,
    /// the offset of the first byte of its body, so a signature's placeholders can be found again.
    /// </summary>
    public byte[] Write(out IReadOnlyDictionary<int, long> bodyStart)
    {
        using var output = new MemoryStream(_original.Length + _changed.Values.Sum(c => c.Body.Length + 32) + 1024);
        output.Write(_original);
        if (_original.Length > 0 && _original[^1] != '\n') output.WriteByte((byte)'\n');
        var offsets = new SortedDictionary<int, (long Offset, int Generation)>();
        var starts = new Dictionary<int, long>();
        foreach (var (id, (generation, body)) in _changed)
        {
            offsets[id] = (output.Position, generation);
            Ascii(output, $"{id} {generation} obj\n");
            starts[id] = output.Position;
            output.Write(body);
            Ascii(output, "\nendobj\n");
        }

        PdfDict trailer = Reader.Trailer;
        long previous = StartXref(_original);
        string trailerEntries = $"/Root {MapRef(RootRef)}"
            + (trailer.Get("Info") is PdfRef info ? $" /Info {MapRef(info)}" : "")
            + IdEntry(trailer)
            + $" /Prev {previous}";
        long xrefOffset = output.Position;
        if (!_original.AsSpan((int)previous).StartsWith("xref"u8)) WriteXrefStream(output, offsets, _next, trailerEntries, xrefOffset);
        else
        {
            var b = new StringBuilder("xref\n");
            foreach (var run in Runs(offsets.Keys))
            {
                b.Append(CultureInfo.InvariantCulture, $"{run[0]} {run.Count}\n");
                foreach (int id in run) b.Append(CultureInfo.InvariantCulture, $"{offsets[id].Offset:D10} {offsets[id].Generation:D5} n \n");
            }
            b.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {_next} {trailerEntries} >>\n");
            Ascii(output, b.ToString());
        }
        Ascii(output, $"startxref\n{xrefOffset}\n%%EOF\n");
        bodyStart = starts;
        return output.ToArray();
    }

    private static void WriteXrefStream(MemoryStream output, SortedDictionary<int, (long Offset, int Generation)> offsets, int size, string trailerEntries, long xrefOffset)
    {
        // The update of a file whose last section is a cross-reference stream is one as well. Its own entry is included.
        int xrefId = size;
        offsets[xrefId] = (xrefOffset, 0);
        var data = new MemoryStream();
        var index = new StringBuilder();
        foreach (var run in Runs(offsets.Keys))
        {
            index.Append(CultureInfo.InvariantCulture, $"{run[0]} {run.Count} ");
            foreach (int id in run)
            {
                long o = offsets[id].Offset;
                data.WriteByte(1);
                for (int s = 24; s >= 0; s -= 8) data.WriteByte((byte)(o >> s));
                data.WriteByte((byte)(offsets[id].Generation >> 8));
                data.WriteByte((byte)offsets[id].Generation);
            }
        }
        Ascii(output, $"{xrefId} 0 obj\n<< /Type /XRef /Size {size + 1} /Index [{index.ToString().TrimEnd()}] /W [1 4 2] {trailerEntries} /Length {data.Length} >>\nstream\n");
        output.Write(data.ToArray());
        Ascii(output, "\nendstream\nendobj\n");
    }

    private static IEnumerable<List<int>> Runs(IEnumerable<int> sortedIds)
    {
        var run = new List<int>();
        foreach (int id in sortedIds)
        {
            if (run.Count > 0 && id != run[^1] + 1) { yield return run; run = []; }
            run.Add(id);
        }
        if (run.Count > 0) yield return run;
    }

    private static string IdEntry(PdfDict trailer)
    {
        if (trailer.Get("ID") is PdfArray { Items: [PdfString first, PdfString second] })
            return $" /ID [<{Convert.ToHexString(first.Bytes)}> <{Convert.ToHexString(second.Bytes)}>]";
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return $" /ID [<{id}> <{id}>]";
    }

    internal static long StartXref(byte[] file)
    {
        int from = Math.Max(0, file.Length - 1024);
        int at = file.AsSpan(from).LastIndexOf("startxref"u8);
        if (at < 0) throw new InvalidDataException("The file has no startxref; it cannot take an incremental update.");
        int i = from + at + 9;
        while (i < file.Length && (file[i] is (byte)' ' or (byte)'\r' or (byte)'\n')) i++;
        long value = 0;
        while (i < file.Length && file[i] is >= (byte)'0' and <= (byte)'9') value = value * 10 + (file[i++] - '0');
        if (value <= 0 || value >= file.Length) throw new InvalidDataException("The startxref offset lies outside the file.");
        return value;
    }

    public static PdfDict With(PdfDict dict, string key, PdfValue value)
    {
        var entries = dict.Entries.Where(e => e.Key != key).ToList();
        entries.Add(new(key, value));
        return new PdfDict(entries);
    }

    private static void Ascii(Stream s, string text) => s.Write(Encoding.ASCII.GetBytes(text));
}
