using Arkki.Pdf;
using Arkki.Pdf.Layout;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Paragraphs that mix bold, italic and regular text.</summary>
internal static class RichTextTests
{
    public static void Run()
    {
        var doc = new PdfDocument();
        PdfFont regular = doc.AddFont(TestData.Regular), bold = doc.AddFont(TestData.Bold);
        var family = new FontFamily(regular, bold);
        var body = new TextStyle(family, 10);

        Check("italic falls back to the upright face", (body with { Italic = true }).Font == regular);
        Check("bold italic falls back to bold", (body with { Bold = true, Italic = true }).Font == bold);
        var withItalic = new FontFamily(regular, bold, Italic: bold);
        Check("italic face is used when given", (body with { Family = withItalic, Italic = true }).Font == bold);

        // Words wrap across span boundaries, and a word split between styles stays one word.
        var spans = new List<TextSpan>();
        for (int i = 0; i < 30; i++) spans.Add(new TextSpan(i % 2 == 0 ? "plain words " : "bold", i % 2 == 0 ? body : body with { Bold = true }));
        var mixed = new TextBlock(spans);
        LayoutResult r = mixed.Layout(150, 1000);
        Equal("mixed paragraph fits", LayoutStatus.Full, r.Status);
        Check("mixed paragraph wraps to several lines", r.Fragment!.Height > 3 * body.LineAdvance, r.Fragment.Height.ToString());
        Equal("text of a mixed paragraph", "a b", new TextBlock([new TextSpan("a ", body), new TextSpan("b", body with { Bold = true })]).Text);

        // A one-style block written through the span path renders the same as before.
        var single = new TextBlock("Same text", body);
        Equal("single-style block has one span", 1, single.Spans.Count);
        Throws<ArgumentException>("empty span list is rejected", () => new TextBlock([]));
    }
}
