using System.Text;
using Arkki.Pdf.Internal;

namespace Arkki.Pdf;

/// <summary>What annotations need while a document is saved: the writer, page numbers, and the form's shared fonts.</summary>
internal sealed class SaveContext(PdfWriter writer, Func<PdfPage, int?> pageId)
{
    private int _helvetica, _zapf;

    public PdfWriter Writer { get; } = writer;
    public Func<PdfPage, int?> PageId { get; } = pageId;

    /// <summary>The top of the tree of fields created in this document, in creation order, for the form's /Fields.</summary>
    public List<int> NewFields { get; } = [];

    // A dotted name is a path: "customer.name" is the field "name" under the node "customer".
    private readonly List<(string Path, int Id, int Parent, List<int> Kids)> _fieldNodes = [];

    /// <summary>
    /// Where a field with this full name goes: the object number of its parent node (0 at the top) and its own
    /// partial name, the part after the last dot. Nodes are reserved here and written by <see cref="WriteFieldTree"/>.
    /// </summary>
    public (int Parent, string Partial) FieldPlace(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot < 0 ? (0, fullName) : (Node(fullName[..dot]), fullName[(dot + 1)..]);
    }

    /// <summary>Puts a written field under its parent node, or at the top of the form.</summary>
    public void AddField(int id, int parent)
    {
        if (parent == 0) NewFields.Add(id);
        else _fieldNodes.First(n => n.Id == parent).Kids.Add(id);
    }

    private int Node(string path)
    {
        foreach (var n in _fieldNodes) if (n.Path == path) return n.Id;
        var (parent, _) = FieldPlace(path);
        int id = Writer.Reserve();
        _fieldNodes.Add((path, id, parent, []));
        AddField(id, parent);
        return id;
    }

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
        AddField(id, FieldPlace(group.Name).Parent);
        return id;
    }

    /// <summary>The buttons of a radio group written so far.</summary>
    public List<(int Id, string Value)> RadioKids(RadioGroup group) => _radioGroups.First(g => g.Group == group).Kids;

    /// <summary>
    /// Writes what the fields hang from, once the pages have written the fields themselves: the radio groups whose
    /// buttons were written, and the nodes of dotted names.
    /// </summary>
    public void WriteFieldTree()
    {
        foreach (var (group, id, kids) in _radioGroups) RadioButtonAnnotation.WriteGroup(this, group, id, kids);
        _radioGroups.Clear();
        foreach (var (path, id, parent, kids) in _fieldNodes)
        {
            var b = new ByteBuf(96);
            b.Ascii("<< /T ");
            PdfSerializer.WriteString(b, PdfSerializer.TextBytes(path[(path.LastIndexOf('.') + 1)..]), hex: true);
            if (parent != 0) b.Ascii(" /Parent ").Int(parent).Ascii(" 0 R");
            b.Ascii(" /Kids [");
            foreach (int kid in kids) b.Int(kid).Ascii(" 0 R ");
            b.Ascii("] >>");
            Writer.WriteObject(id, b);
        }
        _fieldNodes.Clear();
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
