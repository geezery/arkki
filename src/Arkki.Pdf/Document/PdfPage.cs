using Arkki.Pdf.Reading;

namespace Arkki.Pdf;

/// <summary>
/// One page of a <see cref="PdfDocument"/>: a new page drawn with <see cref="Canvas"/>, or a page imported from another
/// PDF, which can be drawn on too (stamping: whatever the canvas draws lies on top of the page's own content).
/// </summary>
public sealed class PdfPage
{
    private readonly List<PageAnnotation> _annotations = [];
    private int _rotation;

    internal PdfPage(PdfDocument document, double width, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width > 14400 || height > 14400) throw new ArgumentOutOfRangeException(nameof(width), "A PDF page is at most 14 400 points (200 inches) on a side.");
        Document = document;
        Width = width; Height = height;
        Canvas = new PdfCanvas(document, this, 0, 0, height);
    }

    internal PdfPage(PdfDocument document, PdfReader source, int index)
    {
        Document = document;
        Source = source;
        SourcePage = source.Pages[index];
        double[] box = SourcePage.Box;
        OriginX = Math.Min(box[0], box[2]);
        OriginY = Math.Min(box[1], box[3]);
        (Width, Height) = source.GetPageSize(index);
        _rotation = source.GetPageRotation(index);
        Canvas = new PdfCanvas(document, this, OriginX, OriginY, Height);
    }

    internal PdfDocument Document { get; }

    /// <summary>Page width in points.</summary>
    public double Width { get; }
    /// <summary>Page height in points.</summary>
    public double Height { get; }

    /// <summary>
    /// How viewers turn the page when showing and printing it, clockwise: 0, 90, 180 or 270 degrees. The canvas still
    /// draws in the unrotated page's coordinates. An imported page starts with the rotation it had.
    /// </summary>
    public int Rotation
    {
        get => _rotation;
        set
        {
            int r = ((value % 360) + 360) % 360;
            if (r % 90 != 0) throw new ArgumentOutOfRangeException(nameof(value), "Rotation must be a multiple of 90 degrees.");
            _rotation = r;
        }
    }

    /// <summary>
    /// Drawing surface of the page: origin at the top-left corner, y down, units in points. On an imported page it draws
    /// over the page's existing content.
    /// </summary>
    public PdfCanvas Canvas { get; }

    /// <summary>True for a page copied from another PDF with <see cref="PdfDocument.ImportPages(PdfReader)"/>.</summary>
    public bool IsImported => Source is not null;

    /// <summary>Makes the rectangle a link to a web address (or any URI).</summary>
    public void AddLink(double x, double y, double width, double height, Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _annotations.Add(new LinkAnnotation(this, x, y, width, height, target.AbsoluteUri, null, 0));
    }

    /// <summary>Makes the rectangle a link to a place in this document: <paramref name="top"/> points from the top of <paramref name="target"/>.</summary>
    public void AddLink(double x, double y, double width, double height, PdfPage target, double top = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Document != Document) throw new ArgumentException("The target page belongs to another document.", nameof(target));
        _annotations.Add(new LinkAnnotation(this, x, y, width, height, null, target, top));
    }

    /// <summary>
    /// Adds a text field the reader can type in. <paramref name="name"/> is the field's name in the form; a dot makes it a
    /// child of another name, so "customer.name" and "customer.email" are grouped. <paramref name="fontSize"/> 0 sizes
    /// the text to the box.
    /// </summary>
    public PdfFormField AddTextField(string name, double x, double y, double width, double height, string value = "",
        double fontSize = 10, bool multiline = false, bool readOnly = false, int maxLength = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        var a = new TextFieldAnnotation(this, name, x, y, width, height, value, fontSize, multiline, readOnly, maxLength);
        var field = new PdfFormField(name, PdfFieldKind.Text, value, readOnly, []) { Created = a, Multiline = multiline, FontSize = fontSize };
        Document.Form.Add(field);
        _annotations.Add(a);
        return field;
    }

    /// <summary>Adds a check box of <paramref name="size"/> points square; its value is "Yes" when checked and "Off" when not.</summary>
    public PdfFormField AddCheckBox(string name, double x, double y, double size, bool isChecked = false, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var a = new CheckBoxAnnotation(this, name, x, y, size, isChecked, readOnly);
        var field = new PdfFormField(name, PdfFieldKind.CheckBox, isChecked ? "Yes" : "Off", readOnly, ["Yes"]) { Created = a };
        Document.Form.Add(field);
        _annotations.Add(a);
        return field;
    }

    internal double OriginX { get; }
    internal double OriginY { get; }
    internal IReadOnlyList<PageAnnotation> Annotations => _annotations;
    internal void AddAnnotation(PageAnnotation a) => _annotations.Add(a);
    internal PdfReader? Source { get; }
    internal ReaderPage? SourcePage { get; }

    /// <summary>A rectangle in canvas coordinates (top-left origin, y down) as a PDF rectangle array body.</summary>
    internal (double X1, double Y1, double X2, double Y2) ToPdfRect(double x, double y, double width, double height) =>
        (OriginX + x, OriginY + Height - y - height, OriginX + x + width, OriginY + Height - y);
}
