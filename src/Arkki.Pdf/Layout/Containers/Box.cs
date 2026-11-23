namespace Arkki.Pdf.Layout;

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
