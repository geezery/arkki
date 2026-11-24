using System.Diagnostics;
using Arkki.Pdf;
using Arkki.Pdf.Layout;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Unicode edge cases, concurrent use, and a large document's time and memory.</summary>
internal static class ProductionTests
{
    public static void Run()
    {
        Unicode();
        Concurrency();
        LargeDocument();
    }

    private static void Unicode()
    {
        var doc = new PdfDocument();
        PdfFont font = doc.AddFont(TestData.Regular);
        PdfCanvas c = doc.AddPage().Canvas;
        string[] awkward =
        [
            "", " ", "\t", "\0", "emoji 😀 and 👨‍👩‍👧", "unpaired \uD83D surrogate", "low \uDE00 alone", "é combining",
            "zero​width", "CJK 日本語 not in the font", "RTL עברית", "Ω≈ç√∫", "ÅÄÖ åäö Šš Žž Õõ €", new string('x', 20000),
        ];
        int y = 40;
        foreach (string s in awkward)
        {
            bool ok = true;
            try { c.Text(font, 10, 40, y, s); _ = font.Width(s, 10); }
            catch (Exception e) { ok = false; Console.WriteLine("  " + e.GetType().Name + ": " + e.Message); }
            Check($"unicode: drawing {Describe(s)} does not throw", ok);
            y += 14;
        }
        Check("unicode: a surrogate pair is measured as one glyph", Math.Abs(font.Width("😀", 10) - font.Width("😀x", 10) + font.Width("x", 10)) < 0.001);

        var flow = new FlowDocument
        {
            Content = new Column(
                new TextBlock(string.Join(" ", awkward.Where(a => a.Length < 1000)), new TextStyle(new FontFamily(font, font), 10), HAlign.Justify),
                new TextBlock(new string('W', 5000), new TextStyle(new FontFamily(font, font), 10))),
        };
        flow.RenderTo(doc);
        doc.Info.Title = "Otsikko 😀 – ÅÄÖ";
        doc.Info.Author = "Ääkkönen (sulut) \\ kenoviiva";
        byte[] pdf = doc.ToArray();
        External.QpdfClean("unicode edge cases", pdf);
        var reader = PdfReader.Open(pdf);
        Equal("unicode: title with emoji round-trips", "Otsikko 😀 – ÅÄÖ", reader.Info.Title);
        Equal("unicode: author with brackets and backslash round-trips", "Ääkkönen (sulut) \\ kenoviiva", reader.Info.Author);
        string text = reader.ExtractText(0);
        Check("unicode: Nordic and Baltic letters extract", text.Contains("ÅÄÖ åäö Šš Žž Õõ €"), text[..Math.Min(400, text.Length)]);
        string? info = External.PdfInfo(pdf);
        if (info is not null) Check("unicode: pdfinfo reads the emoji title", info.Contains("Otsikko 😀 – ÅÄÖ"), info);
    }

    private static string Describe(string s) => s.Length > 24 ? $"a {s.Length}-character string" : "\"" + string.Concat(s.Select(ch => ch < 32 || char.IsSurrogate(ch) ? $"\\u{(int)ch:X4}" : ch.ToString())) + "\"";

    private static byte[] Build(PdfImage image, int n)
    {
        var doc = new PdfDocument();
        doc.Info.CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        PdfFont font = doc.AddFont(TestData.Regular);
        var style = new TextStyle(new FontFamily(font, doc.AddFont(TestData.Bold)), 9);
        var table = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Pt(80), HAlign.Right)]);
        table.SetHeader(style with { Bold = true }, "Tuote", "Summa");
        for (int i = 0; i < 200; i++) table.AddRow(style, $"Rivi {i} ÅÄÖ", (i * 1.5).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        new FlowDocument { Content = new Column(new ImageBox(image, 100, 50), table) }.RenderTo(doc);
        return doc.ToArray();
    }

    private static void Concurrency()
    {
        // Fonts and images are shared by every document at once; each document is built on its own thread.
        PdfImage image = PdfImage.Load(File.ReadAllBytes(TestData.LogoPath));
        byte[] reference = Build(image, 0);
        var results = new byte[32][];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i => results[i] = Build(image, i));
        Check("concurrency: 32 documents built in parallel with shared fonts and image are identical to one built alone",
            results.All(r => r.AsSpan().SequenceEqual(reference)));
        var texts = new string[16];
        Parallel.For(0, texts.Length, i => texts[i] = PdfReader.Open(reference).ExtractText());
        Check("concurrency: separate readers of the same bytes in parallel agree", texts.All(t => t == texts[0] && t.Contains("Rivi 199 ÅÄÖ")));
        Check("output is deterministic for the same input", Build(image, 1).AsSpan().SequenceEqual(reference));
    }

    private static void LargeDocument()
    {
        int rows = int.TryParse(Environment.GetEnvironmentVariable("ARKKI_LARGE_ROWS"), out int r) ? r : 50_000;
        GC.Collect();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var doc = new PdfDocument();
        PdfFont font = doc.AddFont(TestData.Regular);
        var style = new TextStyle(new FontFamily(font, doc.AddFont(TestData.Bold)), 8);
        var table = new Table([new TableColumn(Len.Pt(60)), new TableColumn(Len.Star()), new TableColumn(Len.Pt(70), HAlign.Right)]);
        table.SetHeader(style with { Bold = true }, "Nro", "Kuvaus", "Summa");
        for (int i = 0; i < rows; i++) table.AddRow(style, i.ToString(), $"Tuote {i} – pitkähkö kuvaus, joka mahtuu riville", (i % 997 * 3.25).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        new FlowDocument { Content = table }.RenderTo(doc);
        string path = Path.Combine(Path.GetTempPath(), $"arkki-large-{Guid.NewGuid():N}.pdf");
        using (var file = File.Create(path)) doc.Save(file);
        double writeSeconds = watch.Elapsed.TotalSeconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        long size = new FileInfo(path).Length;
        int pages = doc.Pages.Count;
        Console.WriteLine($"      large: {rows} rows, {pages} pages, {size / 1024} KB in {writeSeconds:0.0} s, {allocated / (1024 * 1024)} MB allocated");
        Check("large: a 50 000-row table lays out in under 60 s", writeSeconds < 60, $"{writeSeconds:0.0} s");
        Check("large: the file stays small (under 2 KB a page)", size < pages * 2048L, $"{size} bytes for {pages} pages");

        watch.Restart();
        byte[] data = File.ReadAllBytes(path);
        File.Delete(path);
        var reader = PdfReader.Open(data);
        Equal("large: page count reads back", pages, reader.PageCount);
        Check("large: the last row is on the last page", reader.ExtractText(pages - 1).Contains($"Tuote {rows - 1}"));
        Console.WriteLine($"      large: opened and read the last page in {watch.Elapsed.TotalSeconds:0.00} s");
        External.QpdfClean("large document", data);
    }
}
