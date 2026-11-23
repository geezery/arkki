namespace Arkki.Pdf.Layout;

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
