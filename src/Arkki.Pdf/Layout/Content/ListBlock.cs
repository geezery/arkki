namespace Arkki.Pdf.Layout;

/// <summary>How a <see cref="ListBlock"/> marks its items.</summary>
public enum ListMarker
{
    /// <summary>A bullet (•).</summary>
    Bullet,
    /// <summary>An en dash (–).</summary>
    Dash,
    /// <summary>1. 2. 3.</summary>
    Number,
    /// <summary>a) b) c)</summary>
    LowerLetter,
    /// <summary>No marker; the indent stays.</summary>
    None,
}

/// <summary>
/// A bulleted or numbered list with a hanging indent: each item's marker sits in the indent and the item's text
/// wraps under itself, not under the marker. An item can be any element (a nested list too) and splits across
/// pages like its content; the marker is drawn with the item's first part only. An item that is itself a list
/// is a nested list: indented, with no marker of its own.
/// <code>new ListBlock(body, ListMarker.Number).Add("First step").Add("Second step")</code>
/// </summary>
public sealed class ListBlock : Element
{
    private readonly List<Element> _items = [];

    /// <param name="style">The style of the markers, and of items added as text.</param>
    /// <param name="marker">How items are marked.</param>
    /// <param name="indent">The width of the marker column in points; items start this far right.</param>
    public ListBlock(TextStyle style, ListMarker marker = ListMarker.Bullet, double indent = 14)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (!double.IsFinite(indent) || indent < 0) throw new ArgumentOutOfRangeException(nameof(indent), "The indent must be zero or more points.");
        if (!Enum.IsDefined(marker)) throw new ArgumentOutOfRangeException(nameof(marker));
        Style = style; Marker = marker; Indent = indent;
    }

    /// <summary>The style of the markers and of items added as text.</summary>
    public TextStyle Style { get; }
    /// <summary>How items are marked.</summary>
    public ListMarker Marker { get; }
    /// <summary>Width of the marker column in points.</summary>
    public double Indent { get; }

    /// <summary>Vertical space between items, in points.</summary>
    public double Spacing { get; init; }

    /// <summary>The number the first item gets in a numbered or lettered list.</summary>
    public int Start { get; init; } = 1;

    /// <summary>Alignment of items added as text.</summary>
    public HAlign Align { get; init; }

    /// <summary>The items, in order.</summary>
    public IReadOnlyList<Element> Items => _items;

    /// <summary>Adds an item in the list's style.</summary>
    public ListBlock Add(string text) => Add(new TextBlock(text, Style, Align));

    /// <summary>Adds an item of any content.</summary>
    public ListBlock Add(Element item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        return this;
    }

    /// <summary>The marker text of the item at <paramref name="index"/> (0-based).</summary>
    public string MarkerText(int index) => Marker switch
    {
        ListMarker.Bullet => "•",
        ListMarker.Dash => "–",
        ListMarker.Number => $"{Start + index}.",
        ListMarker.LowerLetter => Letters(Start + index) + ")",
        _ => "",
    };

    private static string Letters(int n)
    {
        // 1 → a, 26 → z, 27 → aa
        if (n < 1) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string s = "";
        for (; n > 0; n = (n - 1) / 26) s = (char)('a' + (n - 1) % 26) + s;
        return s;
    }

    /// <inheritdoc />
    public override LayoutResult Layout(double width, double height)
    {
        // A nested list is indented under the item before it, with no marker or number of its own.
        int number = 0;
        var column = new Column(_items.Select(item => (Element)new Item(item, item is ListBlock ? "" : MarkerText(number++), Style, Indent))) { Spacing = Spacing };
        return column.Layout(width, height);
    }

    private sealed class Item(Element body, string marker, TextStyle style, double indent) : Element
    {
        public override LayoutResult Layout(double width, double height)
        {
            double bodyWidth = Math.Max(1, width - indent);
            LayoutResult result = body.Layout(bodyWidth, height);
            if (result.Fragment is null) return LayoutResult.None(this);
            var fragment = new ItemFragment(result.Fragment, marker, style, indent, width);
            Element? rest = result.Remainder is null ? null : new Item(result.Remainder, "", style, indent);
            return new LayoutResult(result.Status, fragment, rest);
        }
    }

    private sealed class ItemFragment : Fragment
    {
        private readonly Fragment _body;
        private readonly string _marker;
        private readonly TextStyle _style;
        private readonly double _indent;

        public ItemFragment(Fragment body, string marker, TextStyle style, double indent, double width)
        {
            _body = body; _marker = marker; _style = style; _indent = indent;
            Width = width;
            Height = body.Height;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            if (_marker.Length > 0)
            {
                PdfFont font = _style.Font;
                double advance = _style.LineAdvance;
                double baseline = (advance - (font.Ascent - font.Descent) * _style.Size) / 2 + font.Ascent * _style.Size;
                // Right-aligned in the indent, a third of a line short of the text.
                double gap = _style.Size / 3;
                double markerX = x + Math.Max(0, _indent - gap - font.Width(_marker, _style.Size));
                canvas.SetFillColor(_style.InkColor);
                canvas.Text(font, _style.Size, markerX, y + baseline, _marker);
            }
            _body.Draw(canvas, x + _indent, y);
        }
    }
}
