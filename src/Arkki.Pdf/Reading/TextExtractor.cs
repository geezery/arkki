using System.Globalization;
using System.Text;

namespace Arkki.Pdf.Reading;

/// <summary>
/// Pulls the visible text out of a page, in content order, for tests and search. Characters are
/// mapped through each font's /ToUnicode CMap (or Latin-1 for simple fonts without one). A new
/// line starts when the baseline moves; a space is inserted when text jumps right on the same
/// line, using the font widths to know where the previous text ended. Text inside form XObjects,
/// Type 3 fonts and rotated layouts are not handled.
/// </summary>
internal sealed class TextExtractor(PdfReader reader)
{
    private readonly Dictionary<PdfDict, FontInfo> _fonts = new(ReferenceEqualityComparer.Instance);

    public string Extract(ReaderPage page)
    {
        var output = new StringBuilder();
        PdfDict? fontResources = reader.Resolve(reader.Resolve(page.Resources) is PdfDict res ? res.Get("Font") : null) as PdfDict;

        foreach (byte[] content in ContentStreams(page.Dict.Get("Contents")))
            Run(content, fontResources, output);
        return output.ToString().TrimEnd('\n', ' ');
    }

    private IEnumerable<byte[]> ContentStreams(PdfValue? contents)
    {
        PdfValue? value = reader.Resolve(contents);
        IEnumerable<PdfValue> parts = value is PdfArray a ? a.Items : value is null ? [] : [value];
        foreach (PdfValue part in parts)
        {
            if (reader.Resolve(part) is not PdfStream s) continue;
            byte[]? data;
            try { data = StreamDecoder.Decode(s, reader.Resolve); }
            catch (Exception e) when (e is NotSupportedException or InvalidDataException) { data = null; }
            if (data is not null) yield return data;
        }
    }

    // Text state of the interpreter.
    private double _a = 1, _b, _c, _d = 1, _e, _f;          // text matrix
    private double _la = 1, _lb, _lc, _ld = 1, _le, _lf;    // line matrix
    private double _leading, _size = 1;
    private FontInfo? _font;
    private bool _anyText;
    private double _lastY, _lastEndX;

    private void Run(byte[] content, PdfDict? fontResources, StringBuilder output)
    {
        var p = new PdfParser(content);
        var operands = new List<PdfValue>();
        while (true)
        {
            p.SkipWhiteAndComments();
            if (p.Position >= content.Length) break;
            byte b = content[p.Position];
            if (b is (byte)'/' or (byte)'(' or (byte)'<' or (byte)'[' or (byte)'+' or (byte)'-' or (byte)'.' || (b >= '0' && b <= '9'))
            {
                operands.Add(p.ReadObject());
                continue;
            }
            string op = p.ReadToken();
            if (op.Length == 0) { p.Position++; operands.Clear(); continue; }
            if (op == "BI") { SkipInlineImage(p, content); operands.Clear(); continue; }
            Operator(op, operands, fontResources, output);
            operands.Clear();
        }
    }

    private void Operator(string op, List<PdfValue> o, PdfDict? fontResources, StringBuilder output)
    {
        double N(int i) => i < o.Count && o[i] is PdfNumber n ? n.Value : 0;
        switch (op)
        {
            case "BT":
                (_a, _b, _c, _d, _e, _f) = (1, 0, 0, 1, 0, 0);
                (_la, _lb, _lc, _ld, _le, _lf) = (1, 0, 0, 1, 0, 0);
                break;
            case "Tf":
                _size = N(1);
                _font = o.Count > 0 && o[0] is PdfName name && fontResources?.Get(name.Encoded) is { } f && reader.Resolve(f) is PdfDict fd
                    ? Font(fd) : null;
                break;
            case "TL": _leading = N(0); break;
            case "Tm":
                (_a, _b, _c, _d, _e, _f) = (N(0), N(1), N(2), N(3), N(4), N(5));
                (_la, _lb, _lc, _ld, _le, _lf) = (_a, _b, _c, _d, _e, _f);
                break;
            case "Td": MoveLine(N(0), N(1)); break;
            case "TD": _leading = -N(1); MoveLine(N(0), N(1)); break;
            case "T*": MoveLine(0, -_leading); break;
            case "Tj": if (o.Count > 0) Show(o[0], output); break;
            case "'": MoveLine(0, -_leading); if (o.Count > 0) Show(o[0], output); break;
            case "\"": MoveLine(0, -_leading); if (o.Count > 2) Show(o[2], output); break;
            case "TJ":
                if (o.Count > 0 && o[0] is PdfArray items)
                {
                    foreach (PdfValue item in items.Items)
                    {
                        if (item is PdfNumber kern)
                        {
                            // A large negative adjustment is how many producers write a word space.
                            double move = -kern.Value / 1000 * _size * _a;
                            _e += move;
                            if (kern.Value < -250 && output.Length > 0 && output[^1] != ' ' && output[^1] != '\n') output.Append(' ');
                        }
                        else Show(item, output);
                    }
                }
                break;
        }
    }

    private void MoveLine(double tx, double ty)
    {
        _le += tx * _la + ty * _lc;
        _lf += tx * _lb + ty * _ld;
        (_a, _b, _c, _d, _e, _f) = (_la, _lb, _lc, _ld, _le, _lf);
    }

    private void Show(PdfValue value, StringBuilder output)
    {
        if (value is not PdfString s) return;
        FontInfo font = _font ?? FontInfo.Fallback;
        double lineHeight = Math.Max(1, Math.Abs(_size * _d));
        if (_anyText)
        {
            if (Math.Abs(_f - _lastY) > lineHeight * 0.5)
            {
                if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
            }
            else if (_e - _lastEndX > _size * 0.15 && output.Length > 0 && output[^1] is not (' ' or '\n'))
            {
                output.Append(' ');
            }
        }

        double width = 0;
        int step = font.CodeBytes;
        for (int i = 0; i + step <= s.Bytes.Length; i += step)
        {
            int code = 0;
            for (int k = 0; k < step; k++) code = code << 8 | s.Bytes[i + k];
            output.Append(font.ToText(code));
            width += font.Width(code) / 1000 * _size;
        }
        _e += width * _a;
        _anyText = true;
        _lastY = _f;
        _lastEndX = _e;
    }

    private static void SkipInlineImage(PdfParser p, byte[] content)
    {
        // Inline image data runs from "ID" to "EI" surrounded by white space.
        int id = content.AsSpan(p.Position).IndexOf("ID"u8);
        if (id < 0) { p.Position = content.Length; return; }
        int i = p.Position + id + 2;
        while (i + 2 < content.Length)
        {
            if (content[i] == 'E' && content[i + 1] == 'I' && PdfParser.IsWhite(content[i - 1]) && (i + 2 == content.Length || PdfParser.IsWhite(content[i + 2])))
            {
                p.Position = i + 2;
                return;
            }
            i++;
        }
        p.Position = content.Length;
    }

    // ---- Fonts -------------------------------------------------------------------------------

    private FontInfo Font(PdfDict dict)
    {
        if (_fonts.TryGetValue(dict, out FontInfo? cached)) return cached;
        bool composite = dict.Get("Subtype") is PdfName { Encoded: "Type0" };
        Dictionary<int, string>? map = null;
        int codeBytes = composite ? 2 : 1;
        if (reader.Resolve(dict.Get("ToUnicode")) is PdfStream cmap)
        {
            try { (map, int bytes) = ParseCMap(StreamDecoder.Decode(cmap, reader.Resolve)); if (bytes > 0) codeBytes = bytes; }
            catch (Exception e) when (e is NotSupportedException or InvalidDataException) { }
        }

        var widths = new Dictionary<int, double>();
        double defaultWidth = 500;
        if (composite)
        {
            if (reader.Resolve(dict.Get("DescendantFonts")) is PdfArray df && df.Items.Count > 0 && reader.Resolve(df.Items[0]) is PdfDict cid)
            {
                defaultWidth = reader.Resolve(cid.Get("DW")) is PdfNumber dw ? dw.Value : 1000;
                if (reader.Resolve(cid.Get("W")) is PdfArray w) ReadCidWidths(w, widths);
            }
        }
        else if (reader.Resolve(dict.Get("Widths")) is PdfArray ws)
        {
            int first = reader.Resolve(dict.Get("FirstChar")) is PdfNumber fc ? fc.IntValue : 0;
            for (int i = 0; i < ws.Items.Count; i++)
                if (reader.Resolve(ws.Items[i]) is PdfNumber n) widths[first + i] = n.Value;
        }

        var font = new FontInfo(codeBytes, map, widths, defaultWidth);
        _fonts[dict] = font;
        return font;
    }

    private void ReadCidWidths(PdfArray w, Dictionary<int, double> widths)
    {
        var items = w.Items.Select(reader.Resolve).ToList();
        for (int i = 0; i < items.Count;)
        {
            if (items[i] is not PdfNumber start) { i++; continue; }
            if (i + 1 < items.Count && items[i + 1] is PdfArray list)
            {
                for (int k = 0; k < list.Items.Count; k++)
                    if (reader.Resolve(list.Items[k]) is PdfNumber n) widths[start.IntValue + k] = n.Value;
                i += 2;
            }
            else if (i + 2 < items.Count && items[i + 1] is PdfNumber end && items[i + 2] is PdfNumber value)
            {
                for (int c = start.IntValue; c <= end.IntValue && c - start.IntValue < 65536; c++) widths[c] = value.Value;
                i += 3;
            }
            else i++;
        }
    }

    /// <summary>Reads bfchar and bfrange mappings, and the code length from the codespace range.</summary>
    private static (Dictionary<int, string> Map, int CodeBytes) ParseCMap(byte[] data)
    {
        var map = new Dictionary<int, string>();
        var p = new PdfParser(data);
        int codeBytes = 0;
        var operands = new List<PdfValue>();
        string? section = null;
        while (true)
        {
            p.SkipWhiteAndComments();
            if (p.Position >= data.Length) break;
            byte b = data[p.Position];
            if (b is (byte)'<' or (byte)'[' or (byte)'/' or (byte)'(' || (b >= '0' && b <= '9'))
            {
                if (b == '<' && p.Position + 1 < data.Length && data[p.Position + 1] == '<') { p.ReadObject(); continue; }
                operands.Add(p.ReadObject());
                continue;
            }
            string token = p.ReadToken();
            if (token.Length == 0) { p.Position++; continue; }
            switch (token)
            {
                case "begincodespacerange" or "beginbfchar" or "beginbfrange": section = token; operands.Clear(); break;
                case "endcodespacerange":
                    if (operands.Count > 0 && operands[0] is PdfString cs) codeBytes = cs.Bytes.Length;
                    section = null; break;
                case "endbfchar":
                    for (int i = 0; i + 1 < operands.Count; i += 2)
                        if (operands[i] is PdfString src && operands[i + 1] is PdfString dst) map[Code(src.Bytes)] = Utf16(dst.Bytes);
                    section = null; break;
                case "endbfrange":
                    for (int i = 0; i + 2 < operands.Count; i += 3)
                    {
                        if (operands[i] is not PdfString lo || operands[i + 1] is not PdfString hi) continue;
                        int from = Code(lo.Bytes), to = Code(hi.Bytes);
                        if (to - from > 65535) continue;
                        if (operands[i + 2] is PdfString first)
                        {
                            byte[] dst = (byte[])first.Bytes.Clone();
                            for (int c = from; c <= to; c++)
                            {
                                map[c] = Utf16(dst);
                                if (dst.Length > 0) dst[^1]++; // the last byte counts up through the range
                            }
                        }
                        else if (operands[i + 2] is PdfArray list)
                        {
                            for (int c = from; c <= to && c - from < list.Items.Count; c++)
                                if (list.Items[c - from] is PdfString d) map[c] = Utf16(d.Bytes);
                        }
                    }
                    section = null; break;
                default:
                    if (section is null) operands.Clear();
                    break;
            }
        }
        return (map, codeBytes);
    }

    private static int Code(byte[] bytes)
    {
        int c = 0;
        foreach (byte x in bytes) c = c << 8 | x;
        return c;
    }

    private static string Utf16(byte[] bytes) => Encoding.BigEndianUnicode.GetString(bytes);

    private sealed class FontInfo(int codeBytes, Dictionary<int, string>? map, Dictionary<int, double> widths, double defaultWidth)
    {
        public static readonly FontInfo Fallback = new(1, null, [], 500);

        public int CodeBytes { get; } = codeBytes;

        public double Width(int code) => widths.TryGetValue(code, out double w) ? w : defaultWidth;

        public string ToText(int code)
        {
            if (map is not null) return map.TryGetValue(code, out string? s) ? s : "";
            if (CodeBytes != 1) return "";
            // No /ToUnicode: a simple font in WinAnsi encoding. Latin-1 matches it except in 0x80–0x9F.
            return code switch
            {
                0x80 => "€", 0x85 => "…", 0x91 => "‘", 0x92 => "’", 0x93 => "“", 0x94 => "”", 0x96 => "–", 0x97 => "—",
                _ => ((char)code).ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
