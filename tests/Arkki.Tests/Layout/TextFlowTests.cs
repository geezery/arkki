using System.Globalization;
using System.Text.RegularExpressions;
using Arkki.Pdf;
using Arkki.Pdf.Layout;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Justified text, hyphenation (soft hyphens and a plugged-in hyphenator) and lists.</summary>
internal static class TextFlowTests
{
    private sealed class TableHyphenator(Dictionary<string, int[]> points) : IHyphenator
    {
        public IEnumerable<int> BreakPoints(string word) => points.TryGetValue(word, out int[]? p) ? p : [];
    }

    public static void Run()
    {
        var doc = new PdfDocument();
        PdfFont regular = doc.AddFont(TestData.Regular), bold = doc.AddFont(TestData.Bold);
        var body = new TextStyle(new FontFamily(regular, bold), 10);
        string Line(WrappedLine l) => string.Concat(l.Pieces.Select(p => p.Text));

        // Paragraph ends are marked: a '\n' and the end of the text, not a wrap.
        var lines = TextWrapper.Wrap([new TextSpan("one two three four five six\nseven", body)], regular.Width("one two three", 10) + 1);
        Equal("wrap: line count", 3, lines.Count);
        Equal("wrap: lines", "one two three|four five six|seven", string.Join("|", lines.Select(Line)));
        Equal("wrap: paragraph ends", "False True True", string.Join(" ", lines.Select(l => l.EndsParagraph)));

        // Soft hyphens: invisible when the word fits, a visible hyphen at the break when it does not.
        string soft = "Huolto­kirja";
        var fits = TextWrapper.Wrap([new TextSpan(soft, body)], 500);
        Equal("soft hyphen is not drawn when the word fits", "Huoltokirja", Line(fits[0]));
        double room = regular.Width("aa Huolto-", 10) + 0.5;
        var broken = TextWrapper.Wrap([new TextSpan("aa " + soft, body)], room);
        Equal("soft hyphen breaks with a hyphen", "aa Huolto-|kirja", string.Join("|", broken.Select(Line)));
        Check("a hyphenated line does not end the paragraph", !broken[0].EndsParagraph);

        // A hyphenator: the last point that fits is used; with none that fits, the word moves down whole.
        var hyph = new TableHyphenator(new() { ["kirjastojärjestelmä"] = [3, 5, 8, 10, 13, 15] });
        double w = regular.Width("xx kirjasto-", 10) + 0.5;
        var h = TextWrapper.Wrap([new TextSpan("xx kirjastojärjestelmä", body)], w, hyph);
        Equal("hyphenator: first line takes the longest head that fits", "xx kirjasto-", Line(h[0]));
        Check("hyphenator: every line fits", h.All(l => regular.Width(Line(l), 10) <= w + 0.01), string.Join("|", h.Select(Line)));
        Equal("hyphenator: the word is kept whole across the lines", "xxkirjastojärjestelmä",
            string.Concat(h.Select(Line)).Replace("-", "").Replace(" ", ""));
        var none = TextWrapper.Wrap([new TextSpan("xx kirjastojärjestelmä", body)], regular.Width("xx kirjastojärjestelmä", 10) - 1);
        Equal("no hyphenator: the word moves to the next line", "xx|kirjastojärjestelmä", string.Join("|", none.Select(Line)));
        var bad = new TableHyphenator(new() { ["abcdef"] = [-3, 0, 6, 99] });
        Equal("hyphenator points out of range are ignored", "abcdef", Line(TextWrapper.Wrap([new TextSpan("abcdef", body)], 500, bad)[0]));

        // A compound's own hyphen is a break point and gets no second hyphen.
        var compound = TextWrapper.Wrap([new TextSpan("ab työ-määräin", body)], Math.Max(regular.Width("ab työ-", 10), regular.Width("määräin", 10)) + 0.5);
        Equal("compound breaks after its own hyphen", "ab työ-|määräin", string.Join("|", compound.Select(Line)));

        // A hyphen across a style change stays one word.
        var mixedWord = TextWrapper.Wrap([new TextSpan("aa Huol", body), new TextSpan("to­kirja", body with { Bold = true })],
            regular.Width("aa Huol", 10) + bold.Width("to-", 10) + 0.5);
        Equal("mixed-style word hyphenates", "aa Huolto-|kirja", string.Join("|", mixedWord.Select(Line)));
        Equal("the hyphen takes the style it follows", 2, mixedWord[0].Pieces.Count);

        // Justified text: every line but the paragraph's last reaches the right edge.
        string text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";
        var jdoc = new PdfDocument();
        PdfFont jr = jdoc.AddFont(TestData.Regular), jb = jdoc.AddFont(TestData.Bold);
        var jbody = new TextStyle(new FontFamily(jr, jb), 10);
        var justified = new TextBlock(text + "\n" + text, jbody, HAlign.Justify);
        var list = new ListBlock(jbody, ListMarker.Number) { Spacing = 2 }
            .Add("First step").Add("Second step that is long enough to wrap onto a second line under its own text, not under the number, because the item text goes on and on well past the width of one line").Add("Third");
        var bullets = new ListBlock(jbody, ListMarker.Bullet).Add("Bullet one").Add(new ListBlock(jbody, ListMarker.LowerLetter).Add("nested a").Add("nested b"));
        new FlowDocument { Content = new Column(justified, list, bullets) { Spacing = 8 } }.RenderTo(jdoc);
        byte[] pdf = jdoc.ToArray();
        External.QpdfClean("justified text and lists", pdf);
        string? bbox = External.Run("pdftotext", pdf, "-bbox", "{file}", "-")?.Output;
        if (bbox is not null)
        {
            var words = Regex.Matches(bbox, "<word xMin=\"([\\d.]+)\" yMin=\"([\\d.]+)\" xMax=\"([\\d.]+)\" yMax=\"[\\d.]+\">([^<]*)</word>")
                .Select(m => (X0: D(m.Groups[1].Value), Y: D(m.Groups[2].Value), X1: D(m.Groups[3].Value), Text: m.Groups[4].Value)).ToList();
            var byLine = words.GroupBy(x => Math.Round(x.Y)).OrderBy(g => g.Key).Select(g => g.OrderBy(x => x.X0).ToList()).ToList();
            double left = byLine[0][0].X0;
            double right = byLine.Take(3).Max(l => l[^1].X1);
            var paragraphLines = byLine.TakeWhile(l => l[0].X0 < left + 1).ToList();
            int firstEnd = paragraphLines.FindIndex(l => l[^1].Text.EndsWith("aliqua."));
            Check("justify: wrapped lines end at the right edge",
                paragraphLines.Take(firstEnd).All(l => Math.Abs(l[^1].X1 - right) < 1.5), string.Join(" ", paragraphLines.Select(l => l[^1].X1.ToString("0.0", CultureInfo.InvariantCulture))));
            Check("justify: the paragraph's last line is not stretched", paragraphLines[firstEnd][^1].X1 < right - 20);
            var numbered = byLine.First(l => l[0].Text == "2.");
            var wrapped = byLine[byLine.IndexOf(numbered) + 1];
            Check("list: wrapped item text hangs under the text, not the number", Math.Abs(wrapped[0].X0 - numbered[1].X0) < 0.5 && wrapped[0].X0 > numbered[0].X1);
            Check("list: markers are right-aligned", Math.Abs(byLine.First(l => l[0].Text == "1.")[0].X1 - numbered[0].X1) < 0.5);
            var bulletLine = byLine[^3];
            var nestedLine = byLine[^2];
            Check("list: nested list is indented under its item", nestedLine[0].Text == "a)" && nestedLine[0].X0 > bulletLine[1].X0 - 0.5,
                $"{nestedLine[0].Text}@{nestedLine[0].X0} vs {bulletLine[1].Text}@{bulletLine[1].X0}");
        }
        string? poppler = External.PopplerText(pdf);
        if (poppler is not null) Check("lists: marker text is extracted", poppler.Contains("1. First step") && poppler.Contains("• Bullet one") && poppler.Contains("b) nested b"), poppler);

        Equal("letters after z", "aa)", new ListBlock(body, ListMarker.LowerLetter, 14) { Start = 27 }.MarkerText(0));
        Equal("numbering start", "5.", new ListBlock(body, ListMarker.Number) { Start = 5 }.MarkerText(0));
        Throws<ArgumentOutOfRangeException>("negative list indent is rejected", () => new ListBlock(body, indent: -1));

        // A list splits across pages and only the item's first part carries the marker.
        var longItem = new ListBlock(body).Add(string.Join(" ", Enumerable.Repeat("word", 400)));
        LayoutResult first = longItem.Layout(200, 100);
        Equal("long list item splits", LayoutStatus.Partial, first.Status);
        Check("the rest lays out", first.Remainder!.Layout(200, 10000).Status == LayoutStatus.Full);
    }

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
