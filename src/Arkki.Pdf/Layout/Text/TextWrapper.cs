namespace Arkki.Pdf.Layout;

internal static class TextWrapper
{
    /// <summary>Greedy line breaking at spaces; words longer than the line are broken by character.</summary>
    public static List<string> Wrap(string text, PdfFont font, double size, double width)
    {
        var lines = new List<string>();
        double space = font.Width(" ", size);
        foreach (string paragraph in text.Split('\n'))
        {
            var line = new System.Text.StringBuilder();
            double lineWidth = 0;
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                double w = font.Width(word, size);
                if (line.Length > 0 && lineWidth + space + w > width + 0.001)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    lineWidth = 0;
                }
                if (w > width + 0.001 && line.Length == 0)
                {
                    // Hard-break an over-long word.
                    string rest = word;
                    while (font.Width(rest, size) > width && rest.Length > 1)
                    {
                        int n = rest.Length - 1;
                        while (n > 1 && font.Width(rest.AsSpan(0, n), size) > width) n--;
                        lines.Add(rest[..n]);
                        rest = rest[n..];
                    }
                    line.Append(rest);
                    lineWidth = font.Width(rest, size);
                    continue;
                }
                if (line.Length > 0) { line.Append(' '); lineWidth += space; }
                line.Append(word);
                lineWidth += w;
            }
            lines.Add(line.ToString());
        }
        return lines;
    }
}
