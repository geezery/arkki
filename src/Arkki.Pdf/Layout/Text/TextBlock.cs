namespace Arkki.Pdf.Layout;

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
