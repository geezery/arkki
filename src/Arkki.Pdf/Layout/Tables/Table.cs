namespace Arkki.Pdf.Layout;

/// <summary>
/// A table that breaks between rows and repeats its header on every page it continues on.
/// Cells are ordinary elements, so a cell can hold wrapped text, an image or a nested layout.
/// </summary>
public sealed class Table : Element
{
    private readonly List<Element[]> _rows;
    private readonly int _start;

    public Table(IReadOnlyList<TableColumn> columns)
    {
        Columns = columns;
        _rows = [];
    }

    private Table(Table source, int start)
    {
        Columns = source.Columns; Header = source.Header; _rows = source._rows; _start = start;
        CellPaddingX = source.CellPaddingX; CellPaddingY = source.CellPaddingY;
        HeaderBackground = source.HeaderBackground; HeaderRule = source.HeaderRule;
        RowRule = source.RowRule; RuleWidth = source.RuleWidth;
    }

    public IReadOnlyList<TableColumn> Columns { get; }
    public Element[]? Header { get; set; }
    public double CellPaddingX { get; set; } = 3;
    public double CellPaddingY { get; set; } = 2.5;
    public Color? HeaderBackground { get; set; }
    public Color? HeaderRule { get; set; }
    public Color? RowRule { get; set; }
    public double RuleWidth { get; set; } = 0.4;
    public int RowCount => _rows.Count;

    public Table AddRow(params Element[] cells)
    {
        if (cells.Length != Columns.Count) throw new ArgumentException($"Expected {Columns.Count} cells, got {cells.Length}.");
        _rows.Add(cells);
        return this;
    }

    /// <summary>Adds a row of text cells, each aligned according to its column.</summary>
    public Table AddRow(TextStyle style, params string[] cells)
    {
        var elements = new Element[cells.Length];
        for (int i = 0; i < cells.Length; i++) elements[i] = new TextBlock(cells[i], style, Columns[i].Align);
        return AddRow(elements);
    }

    public Table SetHeader(TextStyle style, params string[] cells)
    {
        Header = new Element[cells.Length];
        for (int i = 0; i < cells.Length; i++) Header[i] = new TextBlock(cells[i], style, Columns[i].Align);
        return this;
    }

    public override LayoutResult Layout(double width, double height)
    {
        double[] widths = Len.Resolve(Columns.Select(c => c.Width).ToList(), width);
        var rows = new List<RowLayout>();
        double y = 0;

        if (Header is not null)
        {
            RowLayout? header = LayoutRow(Header, widths, height, isHeader: true);
            if (header is null) return LayoutResult.None(this);
            rows.Add(header.Value with { Y = 0 });
            y = header.Value.Height;
        }

        int index = _start;
        for (; index < _rows.Count; index++)
        {
            RowLayout? row = LayoutRow(_rows[index], widths, height - y, isHeader: false);
            if (row is null) break;
            rows.Add(row.Value with { Y = y });
            y += row.Value.Height;
        }

        if (index == _start && _start < _rows.Count) return LayoutResult.None(this);
        var fragment = new TableFragment(this, rows, widths, width, y);
        return index == _rows.Count
            ? LayoutResult.Full(fragment)
            : new LayoutResult(LayoutStatus.Partial, fragment, new Table(this, index));
    }

    private RowLayout? LayoutRow(Element[] cells, double[] widths, double available, bool isHeader)
    {
        var fragments = new Fragment[cells.Length];
        double rowHeight = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            LayoutResult r = cells[i].Layout(widths[i] - 2 * CellPaddingX, available - 2 * CellPaddingY);
            if (r.Status != LayoutStatus.Full) return null;
            fragments[i] = r.Fragment!;
            rowHeight = Math.Max(rowHeight, r.Fragment!.Height);
        }
        return new RowLayout(fragments, rowHeight + 2 * CellPaddingY, 0, isHeader);
    }

    private readonly record struct RowLayout(Fragment[] Cells, double Height, double Y, bool IsHeader);

    private sealed class TableFragment : Fragment
    {
        private readonly Table _t;
        private readonly List<RowLayout> _rows;
        private readonly double[] _widths;

        public TableFragment(Table table, List<RowLayout> rows, double[] widths, double width, double height)
        {
            _t = table; _rows = rows; _widths = widths; Width = width; Height = height;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            foreach (RowLayout row in _rows)
            {
                double top = y + row.Y;
                if (row.IsHeader && _t.HeaderBackground is { } bg)
                {
                    canvas.SetFillColor(bg);
                    canvas.FillRect(x, top, Width, row.Height);
                }
                double cx = x;
                for (int i = 0; i < row.Cells.Length; i++)
                {
                    row.Cells[i].Draw(canvas, cx + _t.CellPaddingX, top + _t.CellPaddingY);
                    cx += _widths[i];
                }
                Color? rule = row.IsHeader ? _t.HeaderRule : _t.RowRule;
                if (rule is { } color)
                {
                    canvas.SetStrokeColor(color);
                    canvas.SetLineWidth(row.IsHeader ? _t.RuleWidth * 2 : _t.RuleWidth);
                    canvas.Line(x, top + row.Height, x + Width, top + row.Height);
                }
            }
        }
    }
}
