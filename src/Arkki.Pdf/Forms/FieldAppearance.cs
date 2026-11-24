using System.Globalization;
using Arkki.Pdf.Internal;

namespace Arkki.Pdf;

/// <summary>Turns text into WinAnsiEncoding bytes, the encoding of the standard fonts used in generated field appearances.</summary>
internal static class WinAnsi
{
    private static readonly Dictionary<char, byte> High = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88,
        ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93,
        ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B,
        ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };

    public static byte[] Encode(string s)
    {
        var bytes = new byte[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bytes[i] = c < 0x80 || (c >= 0xA0 && c <= 0xFF) ? (byte)c : High.TryGetValue(c, out byte b) ? b : (byte)'?';
        }
        return bytes;
    }
}

/// <summary>
/// The box a field's appearance draws around its value: a background and a border, each a colour as the numbers of a
/// PDF colour operator (one for grey, three for RGB, four for CMYK) or null for none.
/// </summary>
internal sealed record FieldFrame(string? Border, string? Background, double Width = 1, bool Underline = false)
{
    /// <summary>The frame of fields created in this document: a thin grey border, as their /MK /BC says.</summary>
    public static readonly FieldFrame Created = new(FieldAppearance.BorderColor, null);

    /// <summary>The operators that paint the frame into a box of this size.</summary>
    public void Write(ByteBuf c, double width, double height)
    {
        if (Background is not null)
            c.Ascii("q ").Ascii(Background).Sp().Ascii(Operator(Background, fill: true)).Ascii(" 0 0 ").Num(width).Sp().Num(height).Ascii(" re f Q ");
        if (Border is null || Width <= 0) return;
        double half = Width / 2;
        c.Ascii("q ").Ascii(Border).Sp().Ascii(Operator(Border, fill: false)).Sp().Num(Width).Ascii(" w ");
        if (Underline) c.Num(0).Sp().Num(half).Ascii(" m ").Num(width).Sp().Num(half).Ascii(" l S Q ");
        else c.Num(half).Sp().Num(half).Sp().Num(Math.Max(0, width - Width)).Sp().Num(Math.Max(0, height - Width)).Ascii(" re S Q ");
    }

    private static string Operator(string color, bool fill) => color.Count(ch => ch == ' ') switch
    {
        0 => fill ? "g" : "G",
        2 => fill ? "rg" : "RG",
        _ => fill ? "k" : "K",
    };
}

/// <summary>
/// Appearance streams for form fields, shared by new fields and filled imported ones. A field created in this document
/// gets a thin grey border; a filled imported one is redrawn with the border and background its widget asks for.
/// </summary>
internal static class FieldAppearance
{
    /// <summary>The border colour of created fields, also written as their /MK /BC so viewers redraw the same.</summary>
    public const string BorderColor = "0.6 0.6 0.6";

    /// <summary>Writes the appearance of a text field or combo box showing <paramref name="value"/> and returns its object number.</summary>
    public static int Text(SaveContext ctx, double width, double height, string value, double fontSize, bool multiline, FieldFrame? frame = null)
    {
        double size = fontSize > 0 ? fontSize : Math.Clamp(height * 0.6, 4, 12);
        var c = new ByteBuf(128);
        frame?.Write(c, width, height);
        c.Ascii("/Tx BMC q 1 1 ").Num(Math.Max(0, width - 2)).Sp().Num(Math.Max(0, height - 2)).Ascii(" re W n BT /Helv ").Num(size).Ascii(" Tf 0 g ");
        string[] lines = multiline ? value.Replace("\r\n", "\n").Split('\n') : [value.Replace("\r", " ").Replace("\n", " ")];
        double leading = size * 1.15;
        double y = multiline ? height - 2 - size : (height - size * 0.7) / 2;
        c.Num(2).Sp().Num(y).Ascii(" Td ");
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) c.Num(0).Sp().Num(-leading).Ascii(" Td ");
            PdfSerializer.WriteString(c, WinAnsi.Encode(lines[i]), hex: false);
            c.Ascii(" Tj ");
        }
        c.Ascii("ET Q EMC");
        return Form(ctx, width, height, c, $"/Font << /Helv {ctx.Helvetica} 0 R >>");
    }

    /// <summary>
    /// Writes the appearance of a list box: its options from the first one down, the chosen ones on a highlight.
    /// Options that do not fit are clipped; a viewer scrolls them when the reader uses the field.
    /// </summary>
    public static int List(SaveContext ctx, double width, double height, IReadOnlyList<string> options, IReadOnlyCollection<int> selected,
        double fontSize, FieldFrame? frame = null)
    {
        double size = fontSize > 0 ? fontSize : 10;
        double leading = size * 1.25;
        var c = new ByteBuf(256);
        frame?.Write(c, width, height);
        c.Ascii("/Tx BMC q 1 1 ").Num(Math.Max(0, width - 2)).Sp().Num(Math.Max(0, height - 2)).Ascii(" re W n ");
        int visible = Math.Min(options.Count, (int)Math.Ceiling(Math.Max(0, height - 2) / leading));
        for (int i = 0; i < visible; i++)
            if (selected.Contains(i))
                c.Ascii("0.6 0.757 0.855 rg 1 ").Num(height - 1 - (i + 1) * leading).Sp().Num(Math.Max(0, width - 2)).Sp().Num(leading).Ascii(" re f ");
        if (visible > 0)
        {
            // The baseline that centres a line of Helvetica (ascent 0.72, descent 0.21) in its row.
            c.Ascii("BT /Helv ").Num(size).Ascii(" Tf 0 g ").Num(leading).Ascii(" TL 3 ").Num(height - 1 - size * 0.88).Ascii(" Td ");
            for (int i = 0; i < visible; i++)
            {
                PdfSerializer.WriteString(c, WinAnsi.Encode(options[i].Replace('\r', ' ').Replace('\n', ' ')), hex: false);
                c.Ascii(" Tj T* ");
            }
            c.Ascii("ET ");
        }
        c.Ascii("Q EMC");
        return Form(ctx, width, height, c, $"/Font << /Helv {ctx.Helvetica} 0 R >>");
    }

    /// <summary>Writes an appearance that is the frame alone, for a field with nothing in it yet.</summary>
    public static int Empty(SaveContext ctx, double width, double height, FieldFrame frame)
    {
        var c = new ByteBuf(64);
        frame.Write(c, width, height);
        return Form(ctx, width, height, c, "");
    }

    /// <summary>Writes the "on" (a tick) or "off" (empty) appearance of a check box.</summary>
    public static int Check(SaveContext ctx, double width, double height, bool on, FieldFrame? frame = null)
    {
        var c = new ByteBuf(96);
        frame?.Write(c, width, height);
        if (on)
        {
            double size = Math.Min(width, height) * 0.8;
            c.Ascii("q BT /ZaDb ").Num(size).Ascii(" Tf 0 g ").Num((width - size * 0.75) / 2).Sp().Num((height - size * 0.7) / 2).Ascii(" Td (4) Tj ET Q");
        }
        return Form(ctx, width, height, c, on ? $"/Font << /ZaDb {ctx.ZapfDingbats} 0 R >>" : "");
    }

    /// <summary>Writes the appearance of a radio button: a ring, with a dot in it when the button is chosen.</summary>
    public static int Radio(SaveContext ctx, double width, double height, bool on)
    {
        var c = new ByteBuf(256);
        double cx = width / 2, cy = height / 2, r = Math.Max(0, Math.Min(width, height) / 2 - 0.5);
        c.Ascii("q ").Ascii(BorderColor).Ascii(" RG 1 w ");
        Circle(c, cx, cy, r).Ascii("S Q");
        if (on)
        {
            c.Ascii(" q 0 g ");
            Circle(c, cx, cy, r * 0.5).Ascii("f Q");
        }
        return Form(ctx, width, height, c, "");
    }

    /// <summary>A circle as four Bézier curves, counter-clockwise from its rightmost point.</summary>
    private static ByteBuf Circle(ByteBuf c, double cx, double cy, double r)
    {
        const double K = 0.5522847498; // the control-point distance that makes a quarter circle
        double k = r * K;
        c.Num(cx + r).Sp().Num(cy).Ascii(" m ");
        Curve(cx + r, cy + k, cx + k, cy + r, cx, cy + r);
        Curve(cx - k, cy + r, cx - r, cy + k, cx - r, cy);
        Curve(cx - r, cy - k, cx - k, cy - r, cx, cy - r);
        Curve(cx + k, cy - r, cx + r, cy - k, cx + r, cy);
        return c;

        void Curve(double x1, double y1, double x2, double y2, double x3, double y3) =>
            c.Num(x1).Sp().Num(y1).Sp().Num(x2).Sp().Num(y2).Sp().Num(x3).Sp().Num(y3).Ascii(" c ");
    }

    private static int Form(SaveContext ctx, double width, double height, ByteBuf content, string resources)
    {
        int id = ctx.Writer.Reserve();
        string bbox = string.Create(CultureInfo.InvariantCulture, $"[0 0 {width:0.###} {height:0.###}]");
        ctx.Writer.WriteStream(id, $"/Type /XObject /Subtype /Form /BBox {bbox} /Resources << {resources} >>", content.Span, compress: false);
        return id;
    }
}
