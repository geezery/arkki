namespace Arkki.Pdf.Layout;

/// <summary>
/// A table that breaks between rows and repeats its header on every page it continues on.
/// Cells are ordinary elements, so a cell can hold wrapped text, an image or a nested layout.
/// A cell made with <see cref="ElementExtensions.SpanColumns"/> covers several columns, and a
/// <see cref="RowStyle"/> gives one row its own background, rules and padding.
/// </summary>
public sealed class Table : Element
{
    private readonly List<(Element[] Cells, RowStyle? Style)> _rows;
    private readonly int _start;

    /// <summary>A table with the given columns; add rows with <see cref="AddRow(Element[])"/>.</summary>
    public Table(IReadOnlyList<TableColumn> columns)
    {
        Columns = columns;
        _rows = [];
    }

    private Table(Table source, int start)
    {
        Columns = source.Columns; Header = source.Header; HeaderStyle = source.HeaderStyle; _rows = source._rows; _start = start;
        CellPaddingX = source.CellPaddingX; CellPaddingY = source.CellPaddingY;
        HeaderBackground = source.HeaderBackground; HeaderRule = source.HeaderRule;
        RowRule = source.RowRule; RuleWidth = source.RuleWidth;
    }

    /// <summary>The columns, left to right.</summary>
    public IReadOnlyList<TableColumn> Columns { get; }
    /// <summary>The header cells, repeated at the top of every page; null for none.</summary>
    public Element[]? Header { get; set; }

    /// <summary>The header row's style. When unset, <see cref="HeaderBackground"/> and <see cref="HeaderRule"/> apply.</summary>
    public RowStyle? HeaderStyle { get; set; }
    /// <summary>Space left and right inside each cell, in points.</summary>
    public double CellPaddingX { get; set; } = 3;
    /// <summary>Space above and below inside each cell, in points.</summary>
    public double CellPaddingY { get; set; } = 2.5;
    /// <summary>Fill behind the header row; null for none.</summary>
    public Color? HeaderBackground { get; set; }
    /// <summary>Colour of the line under the header; null for none.</summary>
    public Color? HeaderRule { get; set; }

    /// <summary>A rule under every row that has no <see cref="RowStyle"/> of its own.</summary>
    public Color? RowRule { get; set; }
    /// <summary>Width of rules in points.</summary>
    public double RuleWidth { get; set; } = 0.4;
    /// <summary>Number of body rows.</summary>
    public int RowCount => _rows.Count;

    /// <summary>Adds a row. Its cells must cover every column exactly once, counting spans.</summary>
    public Table AddRow(params Element[] cells) => AddRow(null, cells);

    /// <summary>Adds a row with its own style.</summary>
    public Table AddRow(RowStyle? style, params Element[] cells)
    {
        CheckCovers(cells);
        _rows.Add((cells, style));
        return this;
    }

    private void CheckCovers(Element[] cells)
    {
        int covered = 0;
        foreach (Element cell in cells) covered += Span(cell);
        if (covered != Columns.Count) throw new ArgumentException($"Expected cells covering {Columns.Count} columns, got {covered}.");
    }

    private static CellFill? FillOf(Element cell) => cell switch
    {
        CellFill f => f,
        ColumnSpan s => FillOf(s.Content),
        _ => null,
    };

    private static int Span(Element cell) => cell switch
    {
        ColumnSpan s => s.Columns,
        CellFill f => Span(f.Content),
        _ => 1,
    };

    /// <summary>Adds a row of text cells, each aligned according to its column.</summary>
    public Table AddRow(TextStyle style, params string[] cells)
    {
        var elements = new Element[cells.Length];
        for (int i = 0; i < cells.Length; i++) elements[i] = new TextBlock(cells[i], style, Columns[i].Align);
        return AddRow(elements);
    }

    /// <summary>Sets the header row, repeated on every page.</summary>
    public Table SetHeader(params Element[] cells)
    {
        CheckCovers(cells);
        Header = cells;
        return this;
    }

    /// <summary>Sets the header row with a row style.</summary>
    public Table SetHeader(RowStyle style, params Element[] cells)
    {
        SetHeader(cells);
        HeaderStyle = style;
        return this;
    }

    /// <summary>Sets a header row of text cells in one style.</summary>
    public Table SetHeader(TextStyle style, params string[] cells)
    {
        Header = new Element[cells.Length];
        for (int i = 0; i < cells.Length; i++) Header[i] = new TextBlock(cells[i], style, Columns[i].Align);
        return this;
    }

    private RowStyle EffectiveHeaderStyle =>
        HeaderStyle ?? new RowStyle(Background: HeaderBackground, RuleBelow: HeaderRule, RuleWidth: RuleWidth * 2);

    private RowStyle EffectiveStyle(RowStyle? style) => style ?? new RowStyle(RuleBelow: RowRule, RuleWidth: RuleWidth);

    /// <inheritdoc />
    public override LayoutResult Layout(double width, double height)
    {
        double[] widths = Len.Resolve(Columns.Select(c => c.Width).ToList(), width);
        var rows = new List<RowLayout>();
        double y = 0;

        if (Header is not null)
        {
            RowLayout? header = LayoutRow(Header, EffectiveHeaderStyle, widths, height);
            if (header is null) return LayoutResult.None(this);
            rows.Add(header.Value with { Y = 0 });
            y = header.Value.Height;
        }
        int headerRows = rows.Count;

        int index = _start;
        for (; index < _rows.Count; index++)
        {
            RowLayout? row = LayoutRow(_rows[index].Cells, EffectiveStyle(_rows[index].Style), widths, height - y);
            if (row is null) break;
            rows.Add(row.Value with { Y = y });
            y += row.Value.Height;
        }

        if (index < _rows.Count)
        {
            // Rows that keep with the next ones move to the next page together with them.
            int keep = index;
            for (int j = index - 1; j >= _start; j--)
                if (j + (_rows[j].Style?.KeepWithNext ?? 0) >= keep) keep = j;
            if (keep < index)
            {
                rows.RemoveRange(headerRows + (keep - _start), index - keep);
                index = keep;
                y = rows.Count > 0 ? rows[^1].Y + rows[^1].Height : 0;
            }
        }

        if (index == _start && _start < _rows.Count) return LayoutResult.None(this);
        var fragment = new TableFragment(this, rows, width, y);
        return index == _rows.Count
            ? LayoutResult.Full(fragment)
            : new LayoutResult(LayoutStatus.Partial, fragment, new Table(this, index));
    }

    private RowLayout? LayoutRow(Element[] cells, RowStyle style, double[] widths, double available)
    {
        double top = style.PaddingTop ?? CellPaddingY, bottom = style.PaddingBottom ?? CellPaddingY;
        var fragments = new Fragment[cells.Length];
        var offsets = new double[cells.Length];
        var cellWidths = new double[cells.Length];
        double rowHeight = 0, x = 0;
        int column = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            double cellWidth = 0;
            for (int end = column + Span(cells[i]); column < end; column++) cellWidth += widths[column];
            LayoutResult r = cells[i].Layout(cellWidth - 2 * CellPaddingX, available - top - bottom);
            if (r.Status != LayoutStatus.Full) return null;
            fragments[i] = r.Fragment!;
            offsets[i] = x;
            cellWidths[i] = cellWidth;
            x += cellWidth;
            rowHeight = Math.Max(rowHeight, r.Fragment!.Height);
        }
        double height = Math.Max(rowHeight + top + bottom, style.MinHeight);
        return height > available + 0.001 ? null : new RowLayout(cells, fragments, offsets, cellWidths, style, top, height, 0);
    }

    private readonly record struct RowLayout(
        Element[] Source, Fragment[] Cells, double[] Offsets, double[] Widths, RowStyle Style, double PaddingTop, double Height, double Y);

    private sealed class TableFragment : Fragment
    {
        private readonly Table _t;
        private readonly List<RowLayout> _rows;

        public TableFragment(Table table, List<RowLayout> rows, double width, double height)
        {
            _t = table; _rows = rows; Width = width; Height = height;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            foreach (RowLayout row in _rows)
            {
                double top = y + row.Y;
                RowStyle s = row.Style;
                if (s.Background is { } bg)
                {
                    canvas.SetFillColor(bg);
                    canvas.FillRect(x, top, Width, row.Height);
                }
                for (int i = 0; i < row.Cells.Length; i++)
                {
                    if (FillOf(row.Source[i]) is not { } fill) continue;
                    if (fill.Background is { } cellBg)
                    {
                        canvas.SetFillColor(cellBg);
                        canvas.FillRect(x + row.Offsets[i], top, row.Widths[i], row.Height);
                    }
                    if (fill.Border is { } border)
                    {
                        canvas.SetStrokeColor(border);
                        canvas.SetLineWidth(fill.BorderWidth);
                        canvas.StrokeRect(x + row.Offsets[i], top, row.Widths[i], row.Height);
                    }
                }
                for (int i = 0; i < row.Cells.Length; i++)
                    row.Cells[i].Draw(canvas, x + row.Offsets[i] + _t.CellPaddingX, top + row.PaddingTop);
                if (s.RuleAbove is { } above) Rule(canvas, above, s.RuleWidth, x, top);
                if (s.RuleBelow is { } below) Rule(canvas, below, s.RuleWidth, x, top + row.Height);
            }
        }

        private void Rule(PdfCanvas canvas, Color color, double width, double x, double y)
        {
            canvas.SetStrokeColor(color);
            canvas.SetLineWidth(width);
            canvas.Line(x, y, x + Width, y);
        }
    }
}

/// <summary>
/// How one table row looks: a background, rules above and below it, its own vertical padding
/// (the table's <see cref="Table.CellPaddingY"/> otherwise) and a minimum height.
/// <paramref name="KeepWithNext"/> keeps the row on the same page as that many following rows.
/// </summary>
public sealed record RowStyle(
    Color? Background = null,
    Color? RuleAbove = null,
    Color? RuleBelow = null,
    double RuleWidth = 0.5,
    double? PaddingTop = null,
    double? PaddingBottom = null,
    double MinHeight = 0,
    int KeepWithNext = 0);
