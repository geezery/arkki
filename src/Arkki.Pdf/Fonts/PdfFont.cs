using System.Text;
using Arkki.Pdf.Fonts;
using Arkki.Pdf.Internal;

namespace Arkki.Pdf;

/// <summary>
/// A font as used by one document. Tracks which glyphs the document actually draws so that only
/// those are embedded. Text is encoded as two-byte glyph ids (Identity-H), and a ToUnicode map is
/// written so the text stays searchable and copyable.
/// </summary>
public sealed class PdfFont
{
    private readonly SortedDictionary<int, int> _used = new(); // glyph id -> code point
    private readonly double _scale;

    internal PdfFont(TrueTypeFont face, string resourceName)
    {
        Face = face;
        ResourceName = resourceName;
        _scale = 1.0 / face.UnitsPerEm;
    }

    public TrueTypeFont Face { get; }
    internal string ResourceName { get; }

    /// <summary>Ascent above the baseline for a font size of 1.</summary>
    public double Ascent => Face.Ascender * _scale;

    /// <summary>Descent below the baseline for a font size of 1 (negative).</summary>
    public double Descent => Face.Descender * _scale;

    public double CapHeight => Face.CapHeight * _scale;

    /// <summary>Width of <paramref name="text"/> in points at the given font size.</summary>
    public double Width(ReadOnlySpan<char> text, double size)
    {
        long units = 0;
        foreach (Rune r in text.EnumerateRunes()) units += Face.Advance(Face.GlyphIndex(r.Value));
        return units * _scale * size;
    }

    /// <summary>Appends the text as a PDF hex string of glyph ids and records the glyphs as used.</summary>
    internal void Encode(ReadOnlySpan<char> text, ByteBuf b)
    {
        b.Byte((byte)'<');
        foreach (Rune r in text.EnumerateRunes())
        {
            int g = Face.GlyphIndex(r.Value);
            if (g != 0) _used.TryAdd(g, r.Value);
            b.Hex16(g);
        }
        b.Byte((byte)'>');
    }

    internal bool IsUsed => _used.Count > 0;

    /// <summary>Writes the Type0 font, its CID descendant, descriptor, subset font program and ToUnicode map.</summary>
    internal void Write(PdfWriter w, int fontId)
    {
        int cidId = w.Reserve(), descId = w.Reserve(), fileId = w.Reserve(), toUnicodeId = w.Reserve();
        // A CFF-outline OpenType font is embedded whole (FontFile3 /OpenType); TrueType outlines are subset.
        string name = Face.IsCff ? Face.PostScriptName : SubsetTag() + "+" + Face.PostScriptName;
        double k = 1000.0 / Face.UnitsPerEm;

        if (Face.IsCff) w.WriteStream(fileId, "/Subtype /OpenType", Face.FileData, compress: true);
        else
        {
            byte[] program = Face.BuildSubset(_used.Keys);
            w.WriteStream(fileId, "/Length1 " + program.Length, program, compress: true);
        }

        var b = new ByteBuf(512);
        int flags = 4 | (Face.IsFixedPitch ? 1 : 0) | (Face.ItalicAngle != 0 ? 64 : 0);
        b.Ascii("<< /Type /FontDescriptor /FontName /").Ascii(name).Ascii(" /Flags ").Int(flags)
            .Ascii(" /FontBBox [").Num(Face.XMin * k).Sp().Num(Face.YMin * k).Sp().Num(Face.XMax * k).Sp().Num(Face.YMax * k)
            .Ascii("] /ItalicAngle ").Num(Face.ItalicAngle)
            .Ascii(" /Ascent ").Num(Face.Ascender * k).Ascii(" /Descent ").Num(Face.Descender * k)
            .Ascii(" /CapHeight ").Num(Face.CapHeight * k).Ascii(" /StemV ").Int(Face.WeightClass >= 600 ? 140 : 80)
            .Ascii(Face.IsCff ? " /FontFile3 " : " /FontFile2 ").Int(fileId).Ascii(" 0 R >>");
        w.WriteObject(descId, b);

        b.Reset();
        b.Ascii(Face.IsCff ? "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /" : "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /").Ascii(name)
            .Ascii(" /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor ")
            .Int(descId).Ascii(Face.IsCff ? " 0 R /DW 1000 /W [" : " 0 R /CIDToGIDMap /Identity /DW 1000 /W [");
        int previous = -2;
        foreach (int g in _used.Keys)
        {
            if (g != previous + 1)
            {
                if (previous >= 0) b.Ascii("] ");
                b.Int(g).Ascii(" [");
            }
            else b.Sp();
            b.Num(Math.Round(Face.Advance(g) * k, 2));
            previous = g;
        }
        if (previous >= 0) b.Byte((byte)']');
        b.Ascii("] >>");
        w.WriteObject(cidId, b);

        w.WriteStream(toUnicodeId, "", BuildToUnicode(), compress: true);

        b.Reset();
        b.Ascii("<< /Type /Font /Subtype /Type0 /BaseFont /").Ascii(name)
            .Ascii(" /Encoding /Identity-H /DescendantFonts [").Int(cidId).Ascii(" 0 R] /ToUnicode ")
            .Int(toUnicodeId).Ascii(" 0 R >>");
        w.WriteObject(fontId, b);
    }

    private byte[] BuildToUnicode()
    {
        var b = new ByteBuf(1024);
        b.Ascii("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n" +
                "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n" +
                "/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n" +
                "1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        var entries = _used.ToArray();
        for (int i = 0; i < entries.Length; i += 100)
        {
            int n = Math.Min(100, entries.Length - i);
            b.Int(n).Ascii(" beginbfchar\n");
            for (int j = i; j < i + n; j++)
            {
                b.Byte((byte)'<').Hex16(entries[j].Key).Ascii("> <");
                foreach (char c in char.ConvertFromUtf32(entries[j].Value)) b.Hex16(c);
                b.Ascii(">\n");
            }
            b.Ascii("endbfchar\n");
        }
        b.Ascii("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return b.Span.ToArray();
    }

    /// <summary>Six-letter subset tag derived from the glyph set, so equal subsets get equal names.</summary>
    private string SubsetTag()
    {
        uint h = 2166136261;
        foreach (int g in _used.Keys) h = (h ^ (uint)g) * 16777619;
        Span<char> tag = stackalloc char[6];
        for (int i = 0; i < 6; i++) { tag[i] = (char)('A' + h % 26); h /= 26; }
        return new string(tag);
    }
}
