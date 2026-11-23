using Arkki.Pdf;
using Arkki.Pdf.Layout;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Text splitting, keep-together and table pagination.</summary>
internal static class LayoutTests
{
    public static void Run()
    {
        var layoutDoc = new PdfDocument();
        var family = new FontFamily(layoutDoc.AddFont(Samples.Regular), layoutDoc.AddFont(Samples.Bold));
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
