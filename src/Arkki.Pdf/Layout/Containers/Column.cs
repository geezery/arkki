namespace Arkki.Pdf.Layout;

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
