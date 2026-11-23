using System.Text;

namespace Arkki.Pdf.Layout;

internal static class TextWrapper
{
    /// <summary>
    /// Greedy line breaking at spaces; words longer than the line are broken by character. A line
    /// is a list of pieces, each in one style; adjacent text in the same style is one piece.
    /// Runs of spaces collapse to one, and '\n' starts a new line.
    /// </summary>
    public static List<List<TextSpan>> Wrap(IReadOnlyList<TextSpan> spans, double width)
    {
        var lines = new List<List<TextSpan>>();
        var line = new List<TextSpan>();
        double lineWidth = 0;
        var word = new List<TextSpan>();
        double wordWidth = 0;
        TextStyle? space = null; // style of the space waiting before the next word
        var chars = new StringBuilder();
        TextStyle? charsStyle = null;

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
            if (word.Count == 0) return;
            double spaceWidth = space is not null && line.Count > 0 ? space.Font.Width(" ", space.Size) : 0;
            if (line.Count > 0 && lineWidth + spaceWidth + wordWidth > width + 0.001)
            {
                lines.Add(line);
                line = [];
                lineWidth = 0;
                spaceWidth = 0;
            }
            if (line.Count == 0 && wordWidth > width + 0.001)
            {
                BreakLongWord();
            }
            else
            {
                if (spaceWidth > 0) Append(line, " ", space!);
                foreach (TextSpan piece in word) Append(line, piece.Text, piece.Style);
                lineWidth += spaceWidth + wordWidth;
            }
            word.Clear();
            wordWidth = 0;
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
                    if (line.Count > 0 && lineWidth + w > width + 0.001)
                    {
                        lines.Add(line);
                        line = [];
                        lineWidth = 0;
                    }
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
                    lines.Add(line);
                    line = [];
                    lineWidth = 0;
                    space = null;
                }
                else if (c == ' ')
                {
                    EndWord();
                    if (line.Count > 0) space ??= span.Style;
                }
                else
                {
                    if (charsStyle != span.Style) { EndChars(); charsStyle = span.Style; }
                    chars.Append(c);
                }
            }
            EndChars();
        }
        EndWord();
        lines.Add(line);
        return lines;
    }

    private static void Append(List<TextSpan> pieces, string text, TextStyle style)
    {
        if (pieces.Count > 0 && pieces[^1].Style == style) pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + text };
        else pieces.Add(new TextSpan(text, style));
    }
}
