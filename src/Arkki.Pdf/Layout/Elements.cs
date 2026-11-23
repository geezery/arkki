namespace Arkki.Pdf.Layout;

public enum HAlign { Left, Center, Right }

/// <summary>Outcome of asking an element to lay itself out in a given space.</summary>
public enum LayoutStatus
{
    /// <summary>Everything fitted.</summary>
    Full,
    /// <summary>Part of the element fitted; <see cref="LayoutResult.Remainder"/> continues on the next page.</summary>
    Partial,
    /// <summary>Nothing fitted; the element must move to the next page as a whole.</summary>
    None,
}

public readonly record struct LayoutResult(LayoutStatus Status, Fragment? Fragment, Element? Remainder)
{
    public static LayoutResult None(Element self) => new(LayoutStatus.None, null, self);
    public static LayoutResult Full(Fragment fragment) => new(LayoutStatus.Full, fragment, null);
}

/// <summary>A laid-out, positioned-on-demand piece of an element: knows its size and how to draw itself.</summary>
public abstract class Fragment
{
    public double Width { get; protected init; }
    public double Height { get; protected init; }
    public abstract void Draw(PdfCanvas canvas, double x, double y);
}

/// <summary>
/// A node of the document tree. Elements are plain data: they can be built in code, by a fluent
/// chain of modifiers, or from a stored template. Layout is a pure function of the element and
/// the space offered, which is what makes pagination and testing simple.
/// </summary>
public abstract class Element
{
    public abstract LayoutResult Layout(double width, double height);
}

public sealed record FontFamily(PdfFont Regular, PdfFont Bold);

public sealed record TextStyle(FontFamily Family, double Size = 9, bool Bold = false, Color? Ink = null, double LineHeight = 1.25)
{
    public PdfFont Font => Bold ? Family.Bold : Family.Regular;
    public Color InkColor => Ink ?? Color.Black;
    public double LineAdvance => Size * LineHeight;
}

/// <summary>A column width: a fixed size in points or a share of the remaining space.</summary>
public readonly record struct Len(double Value, bool IsStar)
{
    public static Len Pt(double points) => new(points, false);
    public static Len Mm(double mm) => new(Unit.Mm(mm), false);
    public static Len Star(double weight = 1) => new(weight, true);

    internal static double[] Resolve(IReadOnlyList<Len> lens, double available)
    {
        double fixedSum = 0, stars = 0;
        foreach (Len l in lens) { if (l.IsStar) stars += l.Value; else fixedSum += l.Value; }
        double rest = Math.Max(0, available - fixedSum);
        var result = new double[lens.Count];
        for (int i = 0; i < lens.Count; i++)
            result[i] = lens[i].IsStar ? (stars > 0 ? rest * lens[i].Value / stars : 0) : lens[i].Value;
        return result;
    }
}

// ---- Text ------------------------------------------------------------------------------------

/// <summary>A paragraph of single-style text. Wraps to the offered width and can split between lines.</summary>
public sealed class TextBlock : Element
{
    private List<string>? _lines;
    private double _wrappedWidth = -1;
    private int _start;

    public TextBlock(string text, TextStyle style, HAlign align = HAlign.Left)
    {
        Text = text; Style = style; Align = align;
    }

    public string Text { get; }
    public TextStyle Style { get; }
    public HAlign Align { get; }

    public override LayoutResult Layout(double width, double height)
    {
        if (_lines is null || _wrappedWidth != width)
        {
            _lines = TextWrapper.Wrap(Text, Style.Font, Style.Size, width);
            _wrappedWidth = width;
        }
        int remaining = _lines.Count - _start;
        double advance = Style.LineAdvance;
        int fits = (int)Math.Floor((height + 0.001) / advance);
        if (remaining > 0 && fits <= 0) return LayoutResult.None(this);
        int take = Math.Min(fits, remaining);
        var fragment = new TextFragment(_lines, _start, take, Style, Align, width);
        if (take == remaining) return LayoutResult.Full(fragment);
        var rest = new TextBlock(Text, Style, Align) { _lines = _lines, _wrappedWidth = width, _start = _start + take };
        return new LayoutResult(LayoutStatus.Partial, fragment, rest);
    }

    private sealed class TextFragment : Fragment
    {
        private readonly List<string> _lines;
        private readonly int _start, _count;
        private readonly TextStyle _style;
        private readonly HAlign _align;

        public TextFragment(List<string> lines, int start, int count, TextStyle style, HAlign align, double width)
        {
            _lines = lines; _start = start; _count = count; _style = style; _align = align;
            Width = width;
            Height = count * style.LineAdvance;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            PdfFont font = _style.Font;
            double size = _style.Size, advance = _style.LineAdvance;
            // Centre the font's ascent+descent box inside the line box.
            double baseline = (advance - (font.Ascent - font.Descent) * size) / 2 + font.Ascent * size;
            canvas.SetFillColor(_style.InkColor);
            for (int i = 0; i < _count; i++)
            {
                string line = _lines[_start + i];
                if (line.Length == 0) continue;
                double dx = _align switch
                {
                    HAlign.Right => Width - font.Width(line, size),
                    HAlign.Center => (Width - font.Width(line, size)) / 2,
                    _ => 0,
                };
                canvas.Text(font, size, x + dx, y + i * advance + baseline, line);
            }
        }
    }
}

internal static class TextWrapper
{
    /// <summary>Greedy line breaking at spaces; words longer than the line are broken by character.</summary>
    public static List<string> Wrap(string text, PdfFont font, double size, double width)
    {
        var lines = new List<string>();
        double space = font.Width(" ", size);
        foreach (string paragraph in text.Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            double lineWidth = 0;
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                double w = font.Width(word, size);
                if (line.Length > 0 && lineWidth + space + w > width + 0.001)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    lineWidth = 0;
                }
                if (w > width + 0.001 && line.Length == 0)
                {
                    // Hard-break an over-long word.
                    string rest = word;
                    while (font.Width(rest, size) > width && rest.Length > 1)
                    {
                        int n = rest.Length - 1;
                        while (n > 1 && font.Width(rest.AsSpan(0, n), size) > width) n--;
                        lines.Add(rest[..n]);
                        rest = rest[n..];
                    }
                    line.Append(rest);
                    lineWidth = font.Width(rest, size);
                    continue;
                }
                if (line.Length > 0) { line.Append(' '); lineWidth += space; }
                line.Append(word);
                lineWidth += w;
            }
            lines.Add(line.ToString());
        }
        return lines;
    }
}

// ---- Containers ------------------------------------------------------------------------------

/// <summary>Stacks children vertically. Splits between (and inside) children at page breaks.</summary>
public sealed class Column : Element
{
    public Column(params Element[] children) => Children = [.. children];
    public Column(IEnumerable<Element> children) => Children = [.. children];

    public List<Element> Children { get; }
    public double Spacing { get; init; }

    public Column Add(Element child) { Children.Add(child); return this; }

    public override LayoutResult Layout(double width, double height)
    {
        var placed = new List<(Fragment Fragment, double Y)>();
        double y = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            double gap = placed.Count > 0 ? Spacing : 0;
            LayoutResult r = Children[i].Layout(width, height - y - gap);
            if (r.Status != LayoutStatus.None && r.Fragment is not null)
            {
                placed.Add((r.Fragment, y + gap));
                y += gap + r.Fragment.Height;
            }
            if (r.Status == LayoutStatus.Full) continue;

            if (placed.Count == 0) return LayoutResult.None(this);
            var rest = new List<Element>(Children.Count - i);
            if (r.Remainder is not null) rest.Add(r.Remainder);
            rest.AddRange(Children.Skip(i + 1));
            return new LayoutResult(LayoutStatus.Partial, new StackFragment(placed, width, y), new Column(rest) { Spacing = Spacing });
        }
        return LayoutResult.Full(new StackFragment(placed, width, y));
    }

    private sealed class StackFragment : Fragment
    {
        private readonly List<(Fragment Fragment, double Y)> _items;

        public StackFragment(List<(Fragment, double)> items, double width, double height)
        {
            _items = items; Width = width; Height = height;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            foreach (var (fragment, dy) in _items) fragment.Draw(canvas, x, y + dy);
        }
    }
}

/// <summary>Lays children side by side. A row never splits across pages.</summary>
public sealed class Row : Element
{
    private readonly List<(Len Width, Element Child)> _cells = [];

    public double Spacing { get; init; }

    public Row Add(Len width, Element child) { _cells.Add((width, child)); return this; }

    public override LayoutResult Layout(double width, double height)
    {
        double[] widths = Len.Resolve(_cells.Select(c => c.Width).ToList(), width - Spacing * Math.Max(0, _cells.Count - 1));
        var placed = new List<(Fragment, double)>(_cells.Count);
        double x = 0, rowHeight = 0;
        for (int i = 0; i < _cells.Count; i++)
        {
            LayoutResult r = _cells[i].Child.Layout(widths[i], height);
            if (r.Status != LayoutStatus.Full) return LayoutResult.None(this);
            placed.Add((r.Fragment!, x));
            rowHeight = Math.Max(rowHeight, r.Fragment!.Height);
            x += widths[i] + Spacing;
        }
        return LayoutResult.Full(new RowFragment(placed, width, rowHeight));
    }

    private sealed class RowFragment : Fragment
    {
        private readonly List<(Fragment Fragment, double X)> _items;

        public RowFragment(List<(Fragment, double)> items, double width, double height)
        {
            _items = items; Width = width; Height = height;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            foreach (var (fragment, dx) in _items) fragment.Draw(canvas, x + dx, y);
        }
    }
}

/// <summary>Padding, background and border around a child.</summary>
public sealed class Box : Element
{
    public Box(Element child) => Child = child;

    public Element Child { get; }
    public double Left { get; init; }
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }
    public Color? Background { get; init; }
    public Color? BorderColor { get; init; }
    public double BorderWidth { get; init; } = 0.5;

    public override LayoutResult Layout(double width, double height)
    {
        LayoutResult r = Child.Layout(width - Left - Right, height - Top - Bottom);
        if (r.Status == LayoutStatus.None) return LayoutResult.None(this);
        var fragment = new BoxFragment(this, r.Fragment!, width);
        Element? rest = r.Remainder is null ? null : new Box(r.Remainder)
        {
            Left = Left, Top = Top, Right = Right, Bottom = Bottom,
            Background = Background, BorderColor = BorderColor, BorderWidth = BorderWidth,
        };
        return new LayoutResult(r.Status, fragment, rest);
    }

    private sealed class BoxFragment : Fragment
    {
        private readonly Box _box;
        private readonly Fragment _child;

        public BoxFragment(Box box, Fragment child, double width)
        {
            _box = box; _child = child; Width = width;
            Height = child.Height + box.Top + box.Bottom;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            if (_box.Background is { } bg)
            {
                canvas.SetFillColor(bg);
                canvas.FillRect(x, y, Width, Height);
            }
            if (_box.BorderColor is { } border)
            {
                canvas.SetStrokeColor(border);
                canvas.SetLineWidth(_box.BorderWidth);
                canvas.StrokeRect(x, y, Width, Height);
            }
            _child.Draw(canvas, x + _box.Left, y + _box.Top);
        }
    }
}

/// <summary>Keeps its child on one page: it either fits completely or moves to the next page.</summary>
public sealed class KeepTogether(Element child) : Element
{
    public override LayoutResult Layout(double width, double height)
    {
        LayoutResult r = child.Layout(width, height);
        return r.Status == LayoutStatus.Full ? r : LayoutResult.None(this);
    }
}

/// <summary>Vertical white space. Collapses at the bottom of a page instead of forcing a break.</summary>
public sealed class Spacer(double height) : Element
{
    public override LayoutResult Layout(double width, double available) =>
        LayoutResult.Full(new EmptyFragment(width, Math.Max(0, Math.Min(height, available))));

    private sealed class EmptyFragment : Fragment
    {
        public EmptyFragment(double width, double height) { Width = width; Height = height; }
        public override void Draw(PdfCanvas canvas, double x, double y) { }
    }
}

/// <summary>A horizontal rule across the available width.</summary>
public sealed class Rule(Color color, double thickness = 0.5) : Element
{
    public override LayoutResult Layout(double width, double height) =>
        thickness > height ? LayoutResult.None(this) : LayoutResult.Full(new RuleFragment(color, thickness, width));

    private sealed class RuleFragment : Fragment
    {
        private readonly Color _color;

        public RuleFragment(Color color, double thickness, double width) { _color = color; Width = width; Height = thickness; }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            canvas.SetStrokeColor(_color);
            canvas.SetLineWidth(Height);
            canvas.Line(x, y + Height / 2, x + Width, y + Height / 2);
        }
    }
}

/// <summary>An image scaled to fit inside a maximum box while keeping its aspect ratio.</summary>
public sealed class ImageBox(PdfImage image, double maxWidth, double maxHeight, HAlign align = HAlign.Left) : Element
{
    public override LayoutResult Layout(double width, double height)
    {
        double scale = Math.Min(Math.Min(maxWidth, width) / image.Width, maxHeight / image.Height);
        double w = image.Width * scale, h = image.Height * scale;
        return h > height + 0.001 ? LayoutResult.None(this) : LayoutResult.Full(new ImageFragment(image, w, h, width, align));
    }

    private sealed class ImageFragment : Fragment
    {
        private readonly PdfImage _image;
        private readonly double _w;
        private readonly HAlign _align;

        public ImageFragment(PdfImage image, double w, double h, double width, HAlign align)
        {
            _image = image; _w = w; _align = align; Width = width; Height = h;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            double dx = _align switch { HAlign.Right => Width - _w, HAlign.Center => (Width - _w) / 2, _ => 0 };
            canvas.Image(_image, x + dx, y, _w, Height);
        }
    }
}

/// <summary>Escape hatch: a fixed-height area drawn by a callback with direct canvas access.</summary>
public sealed class Custom(double fixedHeight, Action<PdfCanvas, double, double, double> draw) : Element
{
    public override LayoutResult Layout(double width, double height) =>
        fixedHeight > height + 0.001 ? LayoutResult.None(this) : LayoutResult.Full(new CustomFragment(draw, width, fixedHeight));

    private sealed class CustomFragment : Fragment
    {
        private readonly Action<PdfCanvas, double, double, double> _draw;

        public CustomFragment(Action<PdfCanvas, double, double, double> draw, double width, double height)
        {
            _draw = draw; Width = width; Height = height;
        }

        public override void Draw(PdfCanvas canvas, double x, double y) => _draw(canvas, x, y, Width);
    }
}
