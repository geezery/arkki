using System.Globalization;
using System.Text;
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

/// <summary>Appearance streams for text fields and check boxes, shared by new fields and filled imported ones.</summary>
internal static class FieldAppearance
{
    /// <summary>Writes the appearance of a text field showing <paramref name="value"/> and returns its object number.</summary>
    public static int Text(SaveContext ctx, double width, double height, string value, double fontSize, bool multiline)
    {
        double size = fontSize > 0 ? fontSize : Math.Clamp(height * 0.6, 4, 12);
        var c = new ByteBuf(128);
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

    /// <summary>Writes the "on" (a tick) or "off" (empty) appearance of a check box.</summary>
    public static int Check(SaveContext ctx, double width, double height, bool on)
    {
        var c = new ByteBuf(96);
        if (on)
        {
            double size = Math.Min(width, height) * 0.8;
            c.Ascii("q BT /ZaDb ").Num(size).Ascii(" Tf 0 g ").Num((width - size * 0.75) / 2).Sp().Num((height - size * 0.7) / 2).Ascii(" Td (4) Tj ET Q");
        }
        return Form(ctx, width, height, c, on ? $"/Font << /ZaDb {ctx.ZapfDingbats} 0 R >>" : "");
    }

    private static int Form(SaveContext ctx, double width, double height, ByteBuf content, string resources)
    {
        int id = ctx.Writer.Reserve();
        string bbox = string.Create(CultureInfo.InvariantCulture, $"[0 0 {width:0.###} {height:0.###}]");
        ctx.Writer.WriteStream(id, $"/Type /XObject /Subtype /Form /BBox {bbox} /Resources << {resources} >>", content.Span, compress: false);
        return id;
    }
}

internal sealed class TextFieldAnnotation(PdfPage page, string name, double x, double y, double width, double height, string value,
    double fontSize, bool multiline, bool readOnly, int maxLength) : PageAnnotation(page, x, y, width, height)
{
    public string Name { get; } = name;
    public string Value { get; set; } = value;

    public override int Write(SaveContext ctx, int pageId)
    {
        int ap = FieldAppearance.Text(ctx, Width, Height, Value, fontSize, multiline);
        int flags = (readOnly ? 1 : 0) | (multiline ? 1 << 12 : 0);
        var b = new ByteBuf(256);
        b.Ascii("<< /Type /Annot /Subtype /Widget /FT /Tx /T ");
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(Name), hex: true);
        b.Ascii(" /V ");
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(Value), hex: true);
        b.Ascii(" /DA (/Helv ").Num(fontSize).Ascii(" Tf 0 g) /F 4 /Ff ").Int(flags);
        if (maxLength > 0) b.Ascii(" /MaxLen ").Int(maxLength);
        b.Ascii(" /MK << /BC [0.6 0.6 0.6] >> /P ").Int(pageId).Ascii(" 0 R ");
        Rect(b).Ascii(" /AP << /N ").Int(ap).Ascii(" 0 R >> >>");
        int id = ctx.Writer.Reserve();
        ctx.Writer.WriteObject(id, b);
        ctx.NewFields.Add(id);
        return id;
    }
}

internal sealed class CheckBoxAnnotation(PdfPage page, string name, double x, double y, double size, bool isChecked, bool readOnly)
    : PageAnnotation(page, x, y, size, size)
{
    public string Name { get; } = name;
    public bool Checked { get; set; } = isChecked;

    public override int Write(SaveContext ctx, int pageId)
    {
        int on = FieldAppearance.Check(ctx, Width, Height, true), off = FieldAppearance.Check(ctx, Width, Height, false);
        string state = Checked ? "Yes" : "Off";
        var b = new ByteBuf(256);
        b.Ascii("<< /Type /Annot /Subtype /Widget /FT /Btn /T ");
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(Name), hex: true);
        b.Ascii(" /V /").Ascii(state).Ascii(" /AS /").Ascii(state).Ascii(" /F 4 /Ff ").Int(readOnly ? 1 : 0)
            .Ascii(" /DA (/ZaDb 0 Tf 0 g) /MK << /BC [0.6 0.6 0.6] /CA (4) >> /P ").Int(pageId).Ascii(" 0 R ");
        Rect(b).Ascii(" /AP << /N << /Yes ").Int(on).Ascii(" 0 R /Off ").Int(off).Ascii(" 0 R >> >> >>");
        int id = ctx.Writer.Reserve();
        ctx.Writer.WriteObject(id, b);
        ctx.NewFields.Add(id);
        return id;
    }
}
