using System.Text;
using Arkki.Pdf.Internal;

namespace Arkki.Pdf;

/// <summary>What annotations need while a document is saved: the writer, page numbers, and the form's shared fonts.</summary>
internal sealed class SaveContext(PdfWriter writer, Func<PdfPage, int?> pageId)
{
    private int _helvetica, _zapf;

    public PdfWriter Writer { get; } = writer;
    public Func<PdfPage, int?> PageId { get; } = pageId;

    /// <summary>Fields created in this document, in creation order, for the form's /Fields.</summary>
    public List<int> NewFields { get; } = [];

    /// <summary>Helvetica in WinAnsiEncoding, the font of generated field appearances.</summary>
    public int Helvetica => _helvetica != 0 ? _helvetica : _helvetica = WriteFont("Helvetica", " /Encoding /WinAnsiEncoding");

    /// <summary>ZapfDingbats, for the tick of a check box.</summary>
    public int ZapfDingbats => _zapf != 0 ? _zapf : _zapf = WriteFont("ZapfDingbats", "");

    public bool UsedFormFonts => _helvetica != 0 || _zapf != 0;

    private readonly List<(RadioGroup Group, int Id, List<(int Id, string Value)> Kids)> _radioGroups = [];

    /// <summary>The object number of a radio group's field, reserved when its first button is written.</summary>
    public int RadioParent(RadioGroup group)
    {
        foreach (var g in _radioGroups) if (g.Group == group) return g.Id;
        int id = Writer.Reserve();
        _radioGroups.Add((group, id, []));
        NewFields.Add(id);
        return id;
    }

    /// <summary>The buttons of a radio group written so far.</summary>
    public List<(int Id, string Value)> RadioKids(RadioGroup group) => _radioGroups.First(g => g.Group == group).Kids;

    /// <summary>Writes the fields of the radio groups whose buttons were written; call once after the pages.</summary>
    public void WriteRadioGroups()
    {
        foreach (var (group, id, kids) in _radioGroups) RadioButtonAnnotation.WriteGroup(this, group, id, kids);
        _radioGroups.Clear();
    }

    private int WriteFont(string baseFont, string extra)
    {
        int id = Writer.Reserve();
        Writer.WriteObject(id, Encoding.ASCII.GetBytes($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont}{extra} >>"));
        return id;
    }
}

/// <summary>An annotation added to a page in this document: a link or a form field.</summary>
internal abstract class PageAnnotation(PdfPage page, double x, double y, double width, double height)
{
    public PdfPage Page { get; } = page;

    /// <summary>Writes the annotation and returns its object number, or 0 when it has nothing to write.</summary>
    public abstract int Write(SaveContext ctx, int pageId);

    protected ByteBuf Rect(ByteBuf b)
    {
        var (x1, y1, x2, y2) = Page.ToPdfRect(x, y, width, height);
        return b.Ascii("/Rect [").Num(x1).Sp().Num(y1).Sp().Num(x2).Sp().Num(y2).Ascii("]");
    }

    protected double Width => width;
    protected double Height => height;
}

internal sealed class LinkAnnotation(PdfPage page, double x, double y, double width, double height, string? uri, PdfPage? target, double top)
    : PageAnnotation(page, x, y, width, height)
{
    public override int Write(SaveContext ctx, int pageId)
    {
        var b = new ByteBuf(160);
        b.Ascii("<< /Type /Annot /Subtype /Link ");
        Rect(b).Ascii(" /Border [0 0 0] /P ").Int(pageId).Ascii(" 0 R");
        if (uri is not null)
        {
            b.Ascii(" /A << /S /URI /URI ");
            PdfSerializer.WriteString(b, Encoding.ASCII.GetBytes(uri), hex: false);
            b.Ascii(" >>");
        }
        else
        {
            if (ctx.PageId(target!) is not int targetId) return 0; // the target page was removed
            b.Ascii(" /Dest [").Int(targetId).Ascii(" 0 R /XYZ null ").Num(target!.OriginY + target.Height - top).Ascii(" null]");
        }
        b.Ascii(" >>");
        int id = ctx.Writer.Reserve();
        ctx.Writer.WriteObject(id, b);
        return id;
    }
}
