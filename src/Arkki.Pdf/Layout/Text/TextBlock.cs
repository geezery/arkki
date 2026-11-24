namespace Arkki.Pdf.Layout;

/// <summary>
/// A paragraph of text in one style or several (<see cref="TextSpan"/>). Wraps to the offered
/// width and can split between lines.
/// </summary>
public sealed class TextBlock : Element
{
    private List<WrappedLine>? _lines;
    private double _wrappedWidth = -1;
    private int _start;

    /// <summary>A paragraph in one style.</summary>
    public TextBlock(string text, TextStyle style, HAlign align = HAlign.Left) : this([new TextSpan(text, style)], align) { }

    /// <summary>A paragraph that mixes styles. Line spacing and the baseline follow the first span's style.</summary>
    public TextBlock(IReadOnlyList<TextSpan> spans, HAlign align = HAlign.Left)
    {
        ArgumentNullException.ThrowIfNull(spans);
        if (spans.Count == 0) throw new ArgumentException("A text block needs at least one span.", nameof(spans));
        Spans = spans; Align = align;
    }

    /// <summary>The runs of text and their styles.</summary>
    public IReadOnlyList<TextSpan> Spans { get; }
    /// <summary>The whole text.</summary>
    public string Text => Spans.Count == 1 ? Spans[0].Text : string.Concat(Spans.Select(s => s.Text));
    /// <summary>The first span's style, which sets line spacing.</summary>
    public TextStyle Style => Spans[0].Style;
    /// <summary>Horizontal alignment of the lines.</summary>
    public HAlign Align { get; }

    /// <summary>Where words may be hyphenated when they do not fit; null breaks only at soft hyphens (U+00AD) and a compound's own hyphen.</summary>
    public IHyphenator? Hyphenator { get; init; }

    /// <inheritdoc />
    public override LayoutResult Layout(double width, double height)
    {
        if (_lines is null || _wrappedWidth != width)
        {
            _lines = TextWrapper.Wrap(Spans, width, Hyphenator);
            _wrappedWidth = width;
        }
        int remaining = _lines.Count - _start;
        double advance = Style.LineAdvance;
        int fits = (int)Math.Floor((height + 0.001) / advance);
        if (remaining > 0 && fits <= 0) return LayoutResult.None(this);
        int take = Math.Min(fits, remaining);
        var fragment = new TextFragment(_lines, _start, take, Style, Align, width);
        if (take == remaining) return LayoutResult.Full(fragment);
        var rest = new TextBlock(Spans, Align) { Hyphenator = Hyphenator, _lines = _lines, _wrappedWidth = width, _start = _start + take };
        return new LayoutResult(LayoutStatus.Partial, fragment, rest);
    }

    private sealed class TextFragment : Fragment
    {
        private readonly List<WrappedLine> _lines;
        private readonly int _start, _count;
        private readonly TextStyle _style;
        private readonly HAlign _align;

        public TextFragment(List<WrappedLine> lines, int start, int count, TextStyle style, HAlign align, double width)
        {
            _lines = lines; _start = start; _count = count; _style = style; _align = align;
            Width = width;
            Height = count * style.LineAdvance;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            PdfFont font = _style.Font;
            double advance = _style.LineAdvance;
            // Centre the font's ascent+descent box inside the line box.
            double baseline = (advance - (font.Ascent - font.Descent) * _style.Size) / 2 + font.Ascent * _style.Size;
            for (int i = 0; i < _count; i++)
            {
                WrappedLine wrapped = _lines[_start + i];
                List<TextSpan> line = wrapped.Pieces;
                if (line.Count == 0) continue;
                double lineY = y + i * advance + baseline;
                double lineWidth = 0;
                if (_align != HAlign.Left)
                    foreach (TextSpan piece in line) lineWidth += piece.Style.Font.Width(piece.Text, piece.Style.Size);
                int gaps = _align == HAlign.Justify && !wrapped.EndsParagraph ? line.Sum(p => p.Text.Count(c => c == ' ')) : 0;
                if (gaps > 0)
                {
                    DrawJustified(canvas, line, x, lineY, (Width - lineWidth) / gaps);
                    continue;
                }
                double dx = _align switch
                {
                    HAlign.Right => Width - lineWidth,
                    HAlign.Center => (Width - lineWidth) / 2,
                    _ => 0,
                };
                foreach (TextSpan piece in line)
                {
                    PdfFont pieceFont = piece.Style.Font;
                    canvas.SetFillColor(piece.Style.InkColor);
                    canvas.Text(pieceFont, piece.Style.Size, x + dx, lineY, piece.Text);
                    if (line.Count > 1) dx += pieceFont.Width(piece.Text, piece.Style.Size);
                }
            }
        }

        /// <summary>Draws a line word by word, each space widened by <paramref name="extra"/>. Word spacing (Tw) is not used: it does not apply to the two-byte codes of embedded fonts.</summary>
        private static void DrawJustified(PdfCanvas canvas, List<TextSpan> line, double x, double y, double extra)
        {
            double dx = 0;
            foreach (TextSpan piece in line)
            {
                PdfFont font = piece.Style.Font;
                canvas.SetFillColor(piece.Style.InkColor);
                string[] words = piece.Text.Split(' ');
                for (int w = 0; w < words.Length; w++)
                {
                    if (w > 0) dx += font.Width(" ", piece.Style.Size) + extra;
                    if (words[w].Length == 0) continue;
                    canvas.Text(font, piece.Style.Size, x + dx, y, words[w]);
                    dx += font.Width(words[w], piece.Style.Size);
                }
            }
        }
    }
}
