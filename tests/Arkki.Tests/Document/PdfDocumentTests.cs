using System.Text;
using Arkki.Pdf;
using Arkki.Pdf.Layout;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Document output: valid file structure, deterministic and thread-safe.</summary>
internal static class PdfDocumentTests
{
    public static void Run()
    {
        byte[] pdf = Render();
        Check("pdf header", pdf.AsSpan(0, 8).SequenceEqual("%PDF-1.7"u8));
        Check("pdf trailer", Encoding.Latin1.GetString(pdf, pdf.Length - 6, 6) == "%%EOF\n");
        Check("output is deterministic", pdf.AsSpan().SequenceEqual(Render()));

        // Documents built in parallel share the parsed fonts and must give identical bytes.
        var results = new byte[16][];
        Parallel.For(0, results.Length, i => results[i] = Render());
        Check("parallel renders are identical", results.All(r => r.AsSpan().SequenceEqual(pdf)));
    }

    private static byte[] Render()
    {
        var doc = new PdfDocument();
        doc.Info.Title = "Determinism";
        doc.Info.CreationDate = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var style = new TextStyle(new FontFamily(doc.AddFont(TestData.Regular), doc.AddFont(TestData.Bold)), 10);
        var table = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Pt(60), HAlign.Right)]);
        table.SetHeader(style with { Bold = true }, "Item", "Amount");
        for (int i = 0; i < 80; i++) table.AddRow(style, "Row " + i, (i * 1.5).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        new FlowDocument
        {
            Header = page => new TextBlock($"Page {page.PageNumber} ({page.PageCount})", style, HAlign.Right),
            Content = new Column(new TextBlock("Heading", style with { Size = 16, Bold = true }), table.Padding(4)),
        }.RenderTo(doc);
        return doc.ToArray();
    }
}
