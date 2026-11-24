namespace Arkki.Pdf.Layout;

/// <summary>Padding, background and border around a child.</summary>
public sealed class Box : Element
{
    /// <summary>Wraps <paramref name="child"/> in a box with no padding, fill or border.</summary>
    public Box(Element child) => Child = child;

    /// <summary>The boxed element.</summary>
    public Element Child { get; }
    /// <summary>Space left of the child, in points.</summary>
    public double Left { get; init; }
    /// <summary>Space above the child, in points.</summary>
    public double Top { get; init; }
    /// <summary>Space right of the child, in points.</summary>
    public double Right { get; init; }
    /// <summary>Space below the child, in points.</summary>
    public double Bottom { get; init; }
    /// <summary>Fill behind the box, padding included; null for none.</summary>
    public Color? Background { get; init; }
    /// <summary>Colour of the border around the box; null for none.</summary>
    public Color? BorderColor { get; init; }
    /// <summary>Border width in points.</summary>
    public double BorderWidth { get; init; } = 0.5;

    /// <inheritdoc />
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
