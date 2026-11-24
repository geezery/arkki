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
    /// Adds a text field the reader can type in. <paramref name="name"/> is the field's name in the form; dots make it a
    /// path, so "customer.name" and "customer.email" are the fields "name" and "email" under one parent, "customer".
    /// <paramref name="fontSize"/> 0 sizes the text to the box.
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

    /// <summary>
    /// Adds a radio button of <paramref name="size"/> points square to the group <paramref name="group"/>; the first
    /// button of a name creates the group, and its buttons can lie on any pages of the document. Choosing a button gives
    /// the group that button's <paramref name="value"/>, which is a plain name: ASCII letters, digits, '.', '_' and '-'.
    /// <paramref name="selected"/> chooses this button; <paramref name="readOnly"/> locks the whole group.
    /// </summary>
    public PdfFormField AddRadioButton(string group, string value, double x, double y, double size, bool selected = false, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(group);
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value == "Off" || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException($"'{value}' cannot be a radio button's value: use ASCII letters, digits, '.', '_' and '-', and not \"Off\".", nameof(value));
        PdfFormField? field = Document.Form.Find(group);
        if (field is null)
        {
            var g = new RadioGroup(group);
            field = new PdfFormField(group, PdfFieldKind.RadioButton, "Off", readOnly, g.Options) { Group = g };
            Document.Form.Add(field);
        }
        else if (field.Group is null) throw new ArgumentException($"The form already has a field named '{group}' that is not a radio group created here.", nameof(group));
        RadioGroup radio = field.Group!;
        if (radio.Options.Contains(value)) throw new ArgumentException($"Radio group '{group}' already has a button '{value}'.", nameof(value));
        radio.Options.Add(value);
        if (readOnly) field.ReadOnly = radio.ReadOnly = true;
        if (selected) { radio.Value = value; field.Value = value; }
        _annotations.Add(new RadioButtonAnnotation(this, radio, value, x, y, size));
        return field;
    }

    /// <summary>
    /// Adds a combo box: a drop-down list of <paramref name="options"/> showing <paramref name="value"/>, the chosen
    /// one (null for none). <paramref name="editable"/> lets the reader also type text that is not an option.
    /// </summary>
    public PdfFormField AddComboBox(string name, double x, double y, double width, double height, IEnumerable<string> options,
        string? value = null, double fontSize = 10, bool editable = false, bool readOnly = false) =>
        AddChoice(name, x, y, width, height, options, value, fontSize, combo: true, editable, multiSelect: false, readOnly);

    /// <summary>
    /// Adds a list box showing <paramref name="options"/> with <paramref name="value"/> chosen (null for none). With
    /// <paramref name="multiSelect"/> the reader can choose several; choose them here with <see cref="PdfForm.SetValues"/>.
    /// Make the box tall enough for its options: a row is 1.25 times the font size.
    /// </summary>
    public PdfFormField AddListBox(string name, double x, double y, double width, double height, IEnumerable<string> options,
        string? value = null, double fontSize = 10, bool multiSelect = false, bool readOnly = false) =>
        AddChoice(name, x, y, width, height, options, value, fontSize, combo: false, editable: false, multiSelect, readOnly);

    private PdfFormField AddChoice(string name, double x, double y, double width, double height, IEnumerable<string> options, string? value,
        double fontSize, bool combo, bool editable, bool multiSelect, bool readOnly)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(fontSize);
        string[] list = [.. options];
        if (list.Any(o => o is null)) throw new ArgumentException("An option is null.", nameof(options));
        if (list.Distinct().Count() != list.Length) throw new ArgumentException("The options repeat a value.", nameof(options));
        if (value is not null && !editable && !list.Contains(value))
            throw new ArgumentException($"'{value}' is not one of the options: {string.Join(", ", list)}.", nameof(value));
        IReadOnlyList<string> selected = value is null ? [] : [value];
        var a = new ChoiceFieldAnnotation(this, name, x, y, width, height, list, fontSize, combo, editable, multiSelect, readOnly) { Selected = selected };
        var field = new PdfFormField(name, PdfFieldKind.Choice, value, readOnly, list)
        {
            Created = a, FontSize = fontSize, IsComboBox = combo, IsEditable = combo && editable, IsMultiSelect = multiSelect,
            Selected = selected,
        };
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
