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

/// <summary>Row styles, keep-with-next, page overlays and opacity.</summary>
internal static class RowStyleTests
{
    public static void Run()
    {
        var doc = new PdfDocument { Compression = System.IO.Compression.CompressionLevel.NoCompression };
        var style = new TextStyle(new FontFamily(doc.AddFont(TestData.Regular), doc.AddFont(TestData.Bold)), 10);
        TableColumn[] columns = [new(Len.Star()), new(Len.Pt(60), HAlign.Right)];
        var table = new Table(columns) { CellPaddingY = 2 };
        table.SetHeader(new RowStyle(Background: Color.Gray(0.9), PaddingTop: 4, PaddingBottom: 4), new TextBlock("A", style), new TextBlock("B", style));
        table.AddRow(new RowStyle(RuleAbove: Color.Black, PaddingTop: 1, PaddingBottom: 1), new TextBlock("x", style), new TextBlock("1", style));
        table.AddRow(new TextBlock("y", style), new TextBlock("2", style));
        table.AddRow(new RowStyle(MinHeight: 40), new TextBlock("z", style), new TextBlock("3", style));
        double h = table.Layout(300, 500).Fragment!.Height;
        Equal("row styles set padding and height", (style.LineAdvance + 8) + (style.LineAdvance + 2) + (style.LineAdvance + 4) + 40, h);

        // Rows kept with the next ones move to the next page as a group.
        var kept = new Table(columns) { CellPaddingY = 0 };
        for (int i = 0; i < 4; i++) kept.AddRow(i == 2 ? new RowStyle(KeepWithNext: 1, PaddingTop: 0, PaddingBottom: 0) : null, new TextBlock("r" + i, style), new TextBlock("", style));
        LayoutResult split = kept.Layout(300, style.LineAdvance * 3.5);
        Equal("keep-with-next pulls the row to the next page", 2 * style.LineAdvance, split.Fragment!.Height);

        // A heading that would end a page moves with the paragraph after it.
        var flow = new Column(new Spacer(80), new TextBlock("Heading", style).KeepWithNext(), new TextBlock(string.Join(" ", Enumerable.Repeat("word", 30)), style).KeepTogether());
        LayoutResult r = flow.Layout(200, 80 + style.LineAdvance * 2);
        Equal("keep-with-next moves the heading", 80.0, r.Fragment!.Height);

        int pages = 0;
        new FlowDocument
        {
            Content = new Column(new TextBlock("one", style), new PageBreak(), new TextBlock("two", style)),
            PageOverlay = (canvas, page) =>
            {
                pages++;
                canvas.Save();
                canvas.SetOpacity(0.25);
                canvas.SetFillColor(new Color(0.9, 0.1, 0.1));
                canvas.FillRect(10, 10, 100, 100);
                canvas.Restore();
            },
        }.RenderTo(doc);
        Equal("page overlay runs on every page", 2, pages);
        string pdf = System.Text.Encoding.Latin1.GetString(doc.ToArray());
        Check("opacity is a graphics state", pdf.Contains("/ExtGState << /AkG1 << /ca 0.25 /CA 0.25 >> >>") && pdf.Contains("/AkG1 gs"));

        // A filled cell paints its whole cell at the row's height, however short its own content is.
        var cardDoc = new PdfDocument { Compression = System.IO.Compression.CompressionLevel.NoCompression };
        var cardStyle = new TextStyle(new FontFamily(cardDoc.AddFont(TestData.Regular), cardDoc.AddFont(TestData.Bold)), 10);
        var cards = new Table([new(Len.Pt(100)), new(Len.Pt(20)), new(Len.Pt(100))]) { CellPaddingX = 0, CellPaddingY = 0 };
        cards.AddRow(
            new TextBlock("short", cardStyle).CellFill(new Color(0, 0, 1)),
            new Spacer(0),
            new TextBlock("one\ntwo\nthree", cardStyle).CellFill(null, Color.Black).SpanColumns(1));
        double rowHeight = cards.Layout(220, 500).Fragment!.Height;
        var page = cardDoc.AddPage();
        cards.Layout(220, 500).Fragment!.Draw(page.Canvas, 0, 0);
        string cardPdf = System.Text.Encoding.Latin1.GetString(cardDoc.ToArray());
        Equal("the row is as tall as its tallest cell", 3 * cardStyle.LineAdvance, rowHeight);
        Check("the short cell is filled to the row's height", cardPdf.Contains($"0 0 100 {Fmt(rowHeight)} re"));
        Check("the bordered cell is stroked at its offset", cardPdf.Contains($"120 0 100 {Fmt(rowHeight)} re"));
    }

    private static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
