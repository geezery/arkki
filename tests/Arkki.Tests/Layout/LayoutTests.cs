using Arkki.Pdf;
using Arkki.Pdf.Layout;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Text splitting, keep-together and table pagination.</summary>
internal static class LayoutTests
{
    public static void Run()
    {
        var layoutDoc = new PdfDocument();
        var family = new FontFamily(layoutDoc.AddFont(TestData.Regular), layoutDoc.AddFont(TestData.Bold));
        var style = new TextStyle(family, 10);
        var paragraph = new TextBlock(string.Join(" ", Enumerable.Repeat("word", 400)), style);
        LayoutResult partial = paragraph.Layout(200, 50);
        Equal("text splits across pages", LayoutStatus.Partial, partial.Status);
        Equal("text fragment height is whole lines", 4 * style.LineAdvance, partial.Fragment!.Height);
        Equal("keep-together refuses to split", LayoutStatus.None, paragraph.KeepTogether().Layout(200, 50).Status);
        var table = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Pt(60), HAlign.Right)]);
        table.SetHeader(style, "A", "B");
        for (int i = 0; i < 100; i++) table.AddRow(style, "row " + i, i.ToString());
        new FlowDocument { Content = table }.RenderTo(layoutDoc);
        Check("table flows over several pages", layoutDoc.Pages.Count > 1, layoutDoc.Pages.Count.ToString());
    }
}

/// <summary>Cells spanning columns and forced page breaks.</summary>
internal static class SpanAndBreakTests
{
    public static void Run()
    {
        var doc = new PdfDocument();
        var style = new TextStyle(new FontFamily(doc.AddFont(TestData.Regular), doc.AddFont(TestData.Bold)), 10);
        TableColumn[] columns = [new(Len.Pt(100)), new(Len.Pt(50)), new(Len.Pt(50))];
        string text = "a group heading that is wide";
        var plain = new Table(columns).AddRow(new TextBlock(text, style), new Spacer(0), new Spacer(0));
        var spanned = new Table(columns).AddRow(new TextBlock(text, style).SpanColumns(3));
        double plainHeight = plain.Layout(200, 500).Fragment!.Height, spannedHeight = spanned.Layout(200, 500).Fragment!.Height;
        Check("a spanned cell wraps to the combined width", spannedHeight < plainHeight, $"{spannedHeight} vs {plainHeight}");
        Equal("a spanned cell is one line", style.LineAdvance + 5, spannedHeight);
        new Table(columns).AddRow(new TextBlock("x", style).SpanColumns(2), new TextBlock("y", style));
        Throws<ArgumentException>("spans must cover the columns", () => new Table(columns).AddRow(new TextBlock("x", style).SpanColumns(2)));
        Throws<ArgumentException>("spans must not overflow", () => new Table(columns).AddRow(new TextBlock("x", style).SpanColumns(2), new Spacer(0), new Spacer(0)));
        Throws<ArgumentOutOfRangeException>("a span covers at least one column", () => new Spacer(0).SpanColumns(0));

        // Fonts belong to one document, so every flow renders into the same one.
        var flows = new PdfDocument();
        var flowStyle = new TextStyle(new FontFamily(flows.AddFont(TestData.Regular), flows.AddFont(TestData.Bold)), 10);
        int Pages(Element content)
        {
            int before = flows.Pages.Count;
            new FlowDocument { Content = content }.RenderTo(flows);
            return flows.Pages.Count - before;
        }
        Equal("page break starts a new page", 2, Pages(new Column(new TextBlock("one", flowStyle), new PageBreak(), new TextBlock("two", flowStyle))));
        Equal("a trailing page break adds no empty page", 1, Pages(new Column(new TextBlock("one", flowStyle), new PageBreak())));
        Equal("page breaks work inside nested containers", 3, Pages(new Column(
            new Column(new TextBlock("one", flowStyle), new PageBreak()).Padding(4),
            new TextBlock("two", flowStyle), new PageBreak(), new TextBlock("three", flowStyle))));

        var reader = PdfReader.Open(flows.ToArray());
        Equal("text after the break is on page 2", "two", reader.ExtractText(1).Trim());
    }
}
