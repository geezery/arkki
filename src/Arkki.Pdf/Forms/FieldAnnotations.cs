using Arkki.Pdf.Internal;

namespace Arkki.Pdf;

internal sealed class TextFieldAnnotation(PdfPage page, string name, double x, double y, double width, double height, string value,
    double fontSize, bool multiline, bool readOnly, int maxLength) : PageAnnotation(page, x, y, width, height)
{
    public string Name { get; } = name;
    public string Value { get; set; } = value;

    public override int Write(SaveContext ctx, int pageId)
    {
        int ap = FieldAppearance.Text(ctx, Width, Height, Value, fontSize, multiline, border: true);
        int flags = (readOnly ? 1 : 0) | (multiline ? 1 << 12 : 0);
        var b = new ByteBuf(256);
        b.Ascii("<< /Type /Annot /Subtype /Widget /FT /Tx /T ");
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(Name), hex: true);
        b.Ascii(" /V ");
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(Value), hex: true);
        b.Ascii(" /DA (/Helv ").Num(fontSize).Ascii(" Tf 0 g) /F 4 /Ff ").Int(flags);
        if (maxLength > 0) b.Ascii(" /MaxLen ").Int(maxLength);
        b.Ascii(" /MK << /BC [").Ascii(FieldAppearance.BorderColor).Ascii("] >> /P ").Int(pageId).Ascii(" 0 R ");
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
        int on = FieldAppearance.Check(ctx, Width, Height, true, border: true), off = FieldAppearance.Check(ctx, Width, Height, false, border: true);
        string state = Checked ? "Yes" : "Off";
        var b = new ByteBuf(256);
        b.Ascii("<< /Type /Annot /Subtype /Widget /FT /Btn /T ");
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(Name), hex: true);
        b.Ascii(" /V /").Ascii(state).Ascii(" /AS /").Ascii(state).Ascii(" /F 4 /Ff ").Int(readOnly ? 1 : 0)
            .Ascii(" /DA (/ZaDb 0 Tf 0 g) /MK << /BC [").Ascii(FieldAppearance.BorderColor).Ascii("] /CA (4) >> /P ").Int(pageId).Ascii(" 0 R ");
        Rect(b).Ascii(" /AP << /N << /Yes ").Int(on).Ascii(" 0 R /Off ").Int(off).Ascii(" 0 R >> >> >>");
        int id = ctx.Writer.Reserve();
        ctx.Writer.WriteObject(id, b);
        ctx.NewFields.Add(id);
        return id;
    }
}

/// <summary>
/// A radio group created in this document: one field whose buttons (its widgets) can lie on any pages. The field is
/// written once every page has written its buttons, so it lists only the buttons of pages still in the document.
/// </summary>
internal sealed class RadioGroup(string name)
{
    public string Name { get; } = name;
    /// <summary>The export value of the chosen button, or "Off".</summary>
    public string Value { get; set; } = "Off";
    public bool ReadOnly { get; set; }
    public List<string> Options { get; } = [];
}

internal sealed class RadioButtonAnnotation(PdfPage page, RadioGroup group, string value, double x, double y, double size)
    : PageAnnotation(page, x, y, size, size)
{
    public override int Write(SaveContext ctx, int pageId)
    {
        int parent = ctx.RadioParent(group);
        int on = FieldAppearance.Radio(ctx, Width, Height, true), off = FieldAppearance.Radio(ctx, Width, Height, false);
        var b = new ByteBuf(256);
        b.Ascii("<< /Type /Annot /Subtype /Widget /Parent ").Int(parent).Ascii(" 0 R /AS /").Ascii(group.Value == value ? value : "Off")
            .Ascii(" /F 4 /MK << /BC [").Ascii(FieldAppearance.BorderColor).Ascii("] /CA (l) >> /P ").Int(pageId).Ascii(" 0 R ");
        Rect(b).Ascii(" /AP << /N << /").Ascii(value).Ascii(" ").Int(on).Ascii(" 0 R /Off ").Int(off).Ascii(" 0 R >> >> >>");
        int id = ctx.Writer.Reserve();
        ctx.Writer.WriteObject(id, b);
        ctx.RadioKids(group).Add((id, value));
        return id;
    }

    /// <summary>Writes the group's field once its buttons are written.</summary>
    public static void WriteGroup(SaveContext ctx, RadioGroup group, int id, List<(int Id, string Value)> kids)
    {
        // Radio (bit 16) and NoToggleToOff (bit 15): one button of the group stays chosen.
        int flags = (1 << 15) | (1 << 14) | (group.ReadOnly ? 1 : 0);
        string value = kids.Any(k => k.Value == group.Value) ? group.Value : "Off";
        var b = new ByteBuf(256);
        b.Ascii("<< /FT /Btn /T ");
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(group.Name), hex: true);
        b.Ascii(" /Ff ").Int(flags).Ascii(" /V /").Ascii(value).Ascii(" /DA (/ZaDb 0 Tf 0 g) /Kids [");
        foreach (var (kid, _) in kids) b.Int(kid).Ascii(" 0 R ");
        b.Ascii("] >>");
        ctx.Writer.WriteObject(id, b);
    }
}

/// <summary>A combo box (a drop-down list) or a list box created in this document.</summary>
internal sealed class ChoiceFieldAnnotation(PdfPage page, string name, double x, double y, double width, double height,
    IReadOnlyList<string> options, double fontSize, bool combo, bool editable, bool multiSelect, bool readOnly)
    : PageAnnotation(page, x, y, width, height)
{
    /// <summary>The chosen values: options, or for an editable combo box any text.</summary>
    public IReadOnlyList<string> Selected { get; set; } = [];

    public override int Write(SaveContext ctx, int pageId)
    {
        var indices = Selected.Select(s => IndexOf(options, s)).Where(i => i >= 0).Order().ToList();
        int ap = combo
            ? FieldAppearance.Text(ctx, Width, Height, Selected.FirstOrDefault() ?? "", fontSize, multiline: false, border: true)
            : FieldAppearance.List(ctx, Width, Height, options, indices, fontSize, border: true);
        // Combo is bit 18, Edit bit 19, MultiSelect bit 22.
        int flags = (readOnly ? 1 : 0) | (combo ? 1 << 17 : 0) | (combo && editable ? 1 << 18 : 0) | (multiSelect ? 1 << 21 : 0);
        var b = new ByteBuf(256 + options.Count * 24);
        b.Ascii("<< /Type /Annot /Subtype /Widget /FT /Ch /T ");
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(name), hex: true);
        b.Ascii(" /Ff ").Int(flags).Ascii(" /Opt [");
        foreach (string o in options) Str(b, o).Sp();
        b.Ascii("]");
        if (Selected.Count == 1) Str(b.Ascii(" /V "), Selected[0]);
        else if (Selected.Count > 1)
        {
            b.Ascii(" /V [");
            foreach (string s in Selected) Str(b, s).Sp();
            b.Ascii("]");
        }
        if (!combo && indices.Count > 0)
        {
            b.Ascii(" /I [");
            foreach (int i in indices) b.Int(i).Sp();
            b.Ascii("]");
        }
        b.Ascii(" /DA (/Helv ").Num(fontSize).Ascii(" Tf 0 g) /F 4 /MK << /BC [").Ascii(FieldAppearance.BorderColor).Ascii("] >> /P ").Int(pageId).Ascii(" 0 R ");
        Rect(b).Ascii(" /AP << /N ").Int(ap).Ascii(" 0 R >> >>");
        int id = ctx.Writer.Reserve();
        ctx.Writer.WriteObject(id, b);
        ctx.NewFields.Add(id);
        return id;
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }

    private static ByteBuf Str(ByteBuf b, string s)
    {
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(s), hex: true);
        return b;
    }
}
