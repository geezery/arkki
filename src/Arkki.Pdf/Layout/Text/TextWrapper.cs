using System.Text;

namespace Arkki.Pdf.Layout;

/// <summary>One wrapped line: its pieces, and whether it ends a paragraph (a '\n' or the end of the text).</summary>
internal sealed record WrappedLine(List<TextSpan> Pieces, bool EndsParagraph);

internal static class TextWrapper
{
    internal const char SoftHyphen = '­';
    private const double Epsilon = 0.001;

    /// <summary>
    /// Greedy line breaking at spaces. A word that does not fit is hyphenated at a soft hyphen or a point the
    /// <paramref name="hyphenator"/> gives; a word longer than the line with no such point is broken by character.
    /// A line is a list of pieces, each in one style; adjacent text in the same style is one piece.
    /// Runs of spaces collapse to one, and '\n' starts a new line.
    /// </summary>
    public static List<WrappedLine> Wrap(IReadOnlyList<TextSpan> spans, double width, IHyphenator? hyphenator = null)
    {
        var lines = new List<WrappedLine>();
        var line = new List<TextSpan>();
        double lineWidth = 0;
        var word = new List<TextSpan>();
        double wordWidth = 0;
        int wordLength = 0;
        var softBreaks = new List<int>(); // positions in the word (soft hyphens removed) where it may break
        TextStyle? space = null; // style of the space waiting before the next word
        var chars = new StringBuilder();
        TextStyle? charsStyle = null;

        void NewLine(bool endsParagraph)
        {
            lines.Add(new WrappedLine(line, endsParagraph));
            line = [];
            lineWidth = 0;
        }

        void EndChars()
        {
            if (chars.Length == 0) return;
            string text = chars.ToString();
            chars.Clear();
            Append(word, text, charsStyle!);
            wordWidth += charsStyle!.Font.Width(text, charsStyle.Size);
        }

        void EndWord()
        {
            EndChars();
            if (word.Count == 0) { softBreaks.Clear(); return; }
            List<int>? breaks = null;
            while (true)
            {
                double spaceWidth = space is not null && line.Count > 0 ? space.Font.Width(" ", space.Size) : 0;
                if (lineWidth + spaceWidth + wordWidth <= width + Epsilon)
                {
                    if (spaceWidth > 0) Append(line, " ", space!);
                    foreach (TextSpan piece in word) Append(line, piece.Text, piece.Style);
                    lineWidth += spaceWidth + wordWidth;
                    break;
                }
                breaks ??= BreakPoints(word, softBreaks, hyphenator);
                if (TrySplit(word, breaks, width - lineWidth - spaceWidth, out var head, out var tail, out int at))
                {
                    if (spaceWidth > 0) Append(line, " ", space!);
                    foreach (TextSpan piece in head) Append(line, piece.Text, piece.Style);
                    NewLine(endsParagraph: false);
                    word = tail;
                    wordWidth = Measure(tail);
                    breaks = breaks.Where(b => b > at).Select(b => b - at).ToList();
                    space = null;
                    continue;
                }
                if (line.Count > 0) { NewLine(endsParagraph: false); continue; }
                BreakLongWord();
                break;
            }
            word = [];
            wordWidth = 0;
            wordLength = 0;
            softBreaks.Clear();
            space = null;
        }

        void BreakLongWord()
        {
            // Hard-break an over-long word, character by character, across its pieces.
            foreach (TextSpan piece in word)
            {
                foreach (char c in piece.Text)
                {
                    double w = piece.Style.Font.Width(c.ToString(), piece.Style.Size);
                    if (line.Count > 0 && lineWidth + w > width + Epsilon) NewLine(endsParagraph: false);
                    Append(line, c.ToString(), piece.Style);
                    lineWidth += w;
                }
            }
        }

        foreach (TextSpan span in spans)
        {
            foreach (char c in span.Text)
            {
                if (c == '\n')
                {
                    EndWord();
                    NewLine(endsParagraph: true);
                    space = null;
                }
                else if (c == ' ')
                {
                    EndWord();
                    if (line.Count > 0) space ??= span.Style;
                }
                else if (c == SoftHyphen)
                {
                    if (wordLength > 0 && !softBreaks.Contains(wordLength)) softBreaks.Add(wordLength);
                }
                else
                {
                    if (charsStyle != span.Style) { EndChars(); charsStyle = span.Style; }
                    chars.Append(c);
                    wordLength++;
                }
            }
            EndChars();
        }
        EndWord();
        NewLine(endsParagraph: true);
        return lines;
    }

    private static List<int> BreakPoints(List<TextSpan> word, List<int> softBreaks, IHyphenator? hyphenator)
    {
        string text = string.Concat(word.Select(p => p.Text));
        IEnumerable<int> points = softBreaks.Count > 0 || hyphenator is null
            ? softBreaks
            : hyphenator.BreakPoints(text) ?? [];
        // A compound's own hyphen ("work-order") is always a break point.
        var own = Enumerable.Range(1, Math.Max(0, text.Length - 1)).Where(i => text[i - 1] == '-' && char.IsLetterOrDigit(text[i]) && i > 1);
        return points.Concat(own).Where(p => p > 0 && p < text.Length).Distinct().Order().ToList();
    }

    /// <summary>Splits the word at the last break point whose head plus a hyphen fits in <paramref name="room"/>.</summary>
    private static bool TrySplit(List<TextSpan> word, List<int> breaks, double room,
        out List<TextSpan> head, out List<TextSpan> tail, out int at)
    {
        for (int i = breaks.Count - 1; i >= 0; i--)
        {
            (head, tail) = SplitAt(word, breaks[i]);
            TextStyle last = head[^1].Style;
            if (!head[^1].Text.EndsWith('-')) Append(head, "-", last); // after the word's own hyphen, no second one
            if (Measure(head) <= room + Epsilon) { at = breaks[i]; return true; }
        }
        head = tail = [];
        at = 0;
        return false;
    }

    private static (List<TextSpan> Head, List<TextSpan> Tail) SplitAt(List<TextSpan> word, int index)
    {
        var head = new List<TextSpan>();
        var tail = new List<TextSpan>();
        int seen = 0;
        foreach (TextSpan piece in word)
        {
            int cut = Math.Clamp(index - seen, 0, piece.Text.Length);
            if (cut > 0) head.Add(piece with { Text = piece.Text[..cut] });
            if (cut < piece.Text.Length) tail.Add(piece with { Text = piece.Text[cut..] });
            seen += piece.Text.Length;
        }
        return (head, tail);
    }

    private static double Measure(List<TextSpan> pieces) => pieces.Sum(p => p.Style.Font.Width(p.Text, p.Style.Size));

    private static void Append(List<TextSpan> pieces, string text, TextStyle style)
    {
        if (pieces.Count > 0 && pieces[^1].Style == style) pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + text };
        else pieces.Add(new TextSpan(text, style));
    }
}
