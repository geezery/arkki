using System.Buffers.Binary;
using System.Text;

namespace Arkki.Pdf.Fonts;

/// <summary>
/// Minimal, read-only TrueType parser (glyf outlines only). Parses just what PDF embedding needs:
/// metrics, the Unicode character map and glyph locations for subsetting. Instances are immutable
/// and safe to share between threads and documents.
/// </summary>
public sealed class TrueTypeFont
{
    private readonly byte[] _d;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private readonly ushort[] _advances;
    private readonly Dictionary<int, ushort> _cmap = new();
    private readonly uint[] _loca;

    public string PostScriptName { get; }
    public int UnitsPerEm { get; }
    public int GlyphCount { get; }
    public short Ascender { get; }
    public short Descender { get; }
    public short LineGap { get; }
    public short CapHeight { get; }
    public short XMin { get; }
    public short YMin { get; }
    public short XMax { get; }
    public short YMax { get; }
    public double ItalicAngle { get; }
    public bool IsFixedPitch { get; }
    public int WeightClass { get; }

    private TrueTypeFont(byte[] data)
    {
        _d = data;
        uint version = U32(0);
        if (version == 0x4F54544F) // 'OTTO'
            throw new NotSupportedException("CFF-based OpenType fonts are not supported yet; use a font with TrueType (glyf) outlines.");
        if (version != 0x00010000 && version != 0x74727565)
            throw new InvalidDataException("Not a TrueType font file.");

        int numTables = U16(4);
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(_d, rec, 4);
            _tables[tag] = ((int)U32(rec + 8), (int)U32(rec + 12));
        }

        int head = Table("head");
        UnitsPerEm = U16(head + 18);
        XMin = I16(head + 36); YMin = I16(head + 38); XMax = I16(head + 40); YMax = I16(head + 42);
        int locaFormat = I16(head + 50);

        int hhea = Table("hhea");
        Ascender = I16(hhea + 4); Descender = I16(hhea + 6); LineGap = I16(hhea + 8);
        int numHMetrics = U16(hhea + 34);

        GlyphCount = U16(Table("maxp") + 4);

        int hmtx = Table("hmtx");
        _advances = new ushort[GlyphCount];
        ushort last = 0;
        for (int g = 0; g < GlyphCount; g++)
        {
            if (g < numHMetrics) last = (ushort)U16(hmtx + g * 4);
            _advances[g] = last;
        }

        CapHeight = (short)(Ascender * 0.7);
        WeightClass = 400;
        if (_tables.TryGetValue("OS/2", out var os2))
        {
            WeightClass = U16(os2.Offset + 4);
            if (U16(os2.Offset) >= 2 && os2.Length >= 90) CapHeight = I16(os2.Offset + 88);
        }

        if (_tables.TryGetValue("post", out var post))
        {
            ItalicAngle = (int)U32(post.Offset + 4) / 65536.0;
            IsFixedPitch = U32(post.Offset + 12) != 0;
        }

        int loca = Table("loca");
        _loca = new uint[GlyphCount + 1];
        for (int g = 0; g <= GlyphCount; g++)
            _loca[g] = locaFormat == 0 ? (uint)U16(loca + g * 2) * 2 : U32(loca + g * 4);

        ParseCmap(Table("cmap"));
        PostScriptName = ReadPostScriptName() ?? "ArkkiFont";
    }

    public static TrueTypeFont Load(byte[] data) => new(data);

    /// <summary>Glyph index for a Unicode code point, or 0 (.notdef) when the font has no glyph for it.</summary>
    public int GlyphIndex(int codePoint) => _cmap.TryGetValue(codePoint, out ushort g) ? g : 0;

    public bool HasGlyph(int codePoint) => _cmap.ContainsKey(codePoint);

    /// <summary>Advance width in font design units.</summary>
    public int Advance(int glyph) => (uint)glyph < (uint)_advances.Length ? _advances[glyph] : 0;

    // ---- parsing -------------------------------------------------------------------------

    private int Table(string tag) =>
        _tables.TryGetValue(tag, out var t) ? t.Offset : throw new InvalidDataException($"Font is missing the required '{tag}' table.");

    private int U16(int o) => BinaryPrimitives.ReadUInt16BigEndian(_d.AsSpan(o));
    private short I16(int o) => BinaryPrimitives.ReadInt16BigEndian(_d.AsSpan(o));
    private uint U32(int o) => BinaryPrimitives.ReadUInt32BigEndian(_d.AsSpan(o));

    private void ParseCmap(int cmap)
    {
        int n = U16(cmap + 2);
        int best = -1, bestScore = -1;
        for (int i = 0; i < n; i++)
        {
            int rec = cmap + 4 + i * 8;
            int platform = U16(rec), encoding = U16(rec + 2);
            int sub = cmap + (int)U32(rec + 4);
            int format = U16(sub);
            int score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 5,
                (0, _, 12) => 4,
                (3, 1, 4) => 3,
                (0, _, 4) => 2,
                _ => -1,
            };
            if (score > bestScore) { bestScore = score; best = sub; }
        }
        if (best < 0) throw new NotSupportedException("Font has no supported Unicode cmap subtable (format 4 or 12).");

        if (U16(best) == 12)
        {
            int groups = (int)U32(best + 12);
            for (int i = 0; i < groups; i++)
            {
                int g = best + 16 + i * 12;
                uint start = U32(g), end = U32(g + 4), glyph = U32(g + 8);
                for (uint c = start; c <= end && c - start < 0x10000; c++)
                    _cmap[(int)c] = (ushort)(glyph + (c - start));
            }
            return;
        }

        int segX2 = U16(best + 6);
        int endO = best + 14, startO = endO + segX2 + 2, deltaO = startO + segX2, rangeO = deltaO + segX2;
        for (int i = 0; i < segX2; i += 2)
        {
            int end = U16(endO + i), start = U16(startO + i), delta = I16(deltaO + i), ro = U16(rangeO + i);
            for (int c = start; c <= end && c != 0xFFFF; c++)
            {
                int glyph;
                if (ro == 0) glyph = (c + delta) & 0xFFFF;
                else
                {
                    glyph = U16(rangeO + i + ro + 2 * (c - start));
                    if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                }
                if (glyph != 0) _cmap[c] = (ushort)glyph;
            }
        }
    }

    private string? ReadPostScriptName()
    {
        if (!_tables.TryGetValue("name", out var name)) return null;
        int count = U16(name.Offset + 2), strings = name.Offset + U16(name.Offset + 4);
        string? found = null;
        for (int i = 0; i < count; i++)
        {
            int rec = name.Offset + 6 + i * 12;
            if (U16(rec + 6) != 6) continue;
            int platform = U16(rec), len = U16(rec + 8), off = strings + U16(rec + 10);
            string s = platform == 3 || platform == 0
                ? Encoding.BigEndianUnicode.GetString(_d, off, len)
                : Encoding.Latin1.GetString(_d, off, len);
            found = s;
            if (platform == 3) break;
        }
        if (found is null) return null;
        var sb = new StringBuilder(found.Length);
        foreach (char c in found)
            if (c > 32 && c < 127 && "[](){}<>/%".IndexOf(c) < 0) sb.Append(c);
        return sb.Length > 0 ? sb.ToString() : null;
    }

    // ---- subsetting ----------------------------------------------------------------------

    /// <summary>
    /// Builds a subset font program containing only the given glyphs (plus the components of
    /// composite glyphs). Glyph indices are retained, so no re-mapping is needed in content streams.
    /// </summary>
    internal byte[] BuildSubset(IEnumerable<int> glyphs)
    {
        int glyf = Table("glyf");
        var keep = new bool[GlyphCount];
        var stack = new Stack<int>();
        keep[0] = true;
        stack.Push(0);
        foreach (int g in glyphs)
            if ((uint)g < (uint)GlyphCount && !keep[g]) { keep[g] = true; stack.Push(g); }

        while (stack.Count > 0)
        {
            int g = stack.Pop();
            int start = glyf + (int)_loca[g], len = (int)(_loca[g + 1] - _loca[g]);
            if (len < 10 || I16(start) >= 0) continue; // empty or simple glyph
            int p = start + 10;
            while (true)
            {
                int flags = U16(p), component = U16(p + 2);
                if ((uint)component < (uint)GlyphCount && !keep[component]) { keep[component] = true; stack.Push(component); }
                p += 4 + ((flags & 0x0001) != 0 ? 4 : 2);
                if ((flags & 0x0008) != 0) p += 2;
                else if ((flags & 0x0040) != 0) p += 4;
                else if ((flags & 0x0080) != 0) p += 8;
                if ((flags & 0x0020) == 0) break;
            }
        }

        // Glyph ids are kept, but nothing after the highest kept glyph is needed.
        int glyphCount = Array.LastIndexOf(keep, true) + 1;

        using var glyfOut = new MemoryStream();
        var offsets = new uint[glyphCount + 1];
        for (int g = 0; g < glyphCount; g++)
        {
            offsets[g] = (uint)glyfOut.Length;
            if (!keep[g]) continue;
            int len = (int)(_loca[g + 1] - _loca[g]);
            glyfOut.Write(_d, glyf + (int)_loca[g], len);
            while (glyfOut.Length % 4 != 0) glyfOut.WriteByte(0);
        }
        offsets[glyphCount] = (uint)glyfOut.Length;

        // Short loca (offsets / 2 in 16 bits) when the glyph data allows it: half the size.
        bool shortLoca = glyfOut.Length <= 0x1FFFE;
        var locaOut = new byte[(glyphCount + 1) * (shortLoca ? 2 : 4)];
        for (int g = 0; g <= glyphCount; g++)
        {
            if (shortLoca) BinaryPrimitives.WriteUInt16BigEndian(locaOut.AsSpan(g * 2), (ushort)(offsets[g] / 2));
            else BinaryPrimitives.WriteUInt32BigEndian(locaOut.AsSpan(g * 4), offsets[g]);
        }

        byte[] head = _d.AsSpan(Table("head"), _tables["head"].Length).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);   // checkSumAdjustment, filled in below
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), (short)(shortLoca ? 0 : 1));

        byte[] maxp = _d.AsSpan(Table("maxp"), _tables["maxp"].Length).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)glyphCount);

        // Horizontal metrics for the remaining glyphs; unused entries are zeroed so they compress away.
        byte[] hhea = _d.AsSpan(Table("hhea"), _tables["hhea"].Length).ToArray();
        int numHMetrics = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(34)), glyphCount);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)numHMetrics);
        int hmtx = Table("hmtx");
        var hmtxOut = new byte[numHMetrics * 4 + (glyphCount - numHMetrics) * 2];
        for (int g = 0; g < glyphCount; g++)
        {
            if (!keep[g] && g != numHMetrics - 1) continue; // the last full metric also serves later glyphs
            if (g < numHMetrics) _d.AsSpan(hmtx + g * 4, 4).CopyTo(hmtxOut.AsSpan(g * 4));
            else _d.AsSpan(hmtx + numHMetrics * 4 + (g - numHMetrics) * 2, 2).CopyTo(hmtxOut.AsSpan(numHMetrics * 4 + (g - numHMetrics) * 2));
        }

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["glyf"] = glyfOut.ToArray(),
            ["head"] = head,
            ["hhea"] = hhea,
            ["hmtx"] = hmtxOut,
            ["loca"] = locaOut,
            ["maxp"] = maxp,
        };
        foreach (string tag in (string[])["cvt ", "fpgm", "prep"])
            if (_tables.TryGetValue(tag, out var t)) tables[tag] = _d.AsSpan(t.Offset, t.Length).ToArray();

        int count = tables.Count;
        int size = 12 + count * 16;
        foreach (var t in tables.Values) size += (t.Length + 3) & ~3;
        var o = new byte[size];
        int pow = 1, log = 0;
        while (pow * 2 <= count) { pow *= 2; log++; }
        BinaryPrimitives.WriteUInt32BigEndian(o, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(6), (ushort)(pow * 16));
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(8), (ushort)log);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(10), (ushort)(count * 16 - pow * 16));

        int rec = 12, pos = 12 + count * 16, headPos = 0;
        foreach (var (tag, data) in tables)
        {
            Encoding.ASCII.GetBytes(tag, o.AsSpan(rec, 4));
            data.CopyTo(o, pos);
            if (tag == "head") headPos = pos;
            BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(rec + 4), Checksum(o.AsSpan(pos, (data.Length + 3) & ~3)));
            BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(rec + 8), (uint)pos);
            BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(rec + 12), (uint)data.Length);
            rec += 16;
            pos += (data.Length + 3) & ~3;
        }
        BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(headPos + 8), 0xB1B0AFBA - Checksum(o));
        return o;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i + 3 < data.Length; i += 4) sum += BinaryPrimitives.ReadUInt32BigEndian(data[i..]);
        return sum;
    }
}
