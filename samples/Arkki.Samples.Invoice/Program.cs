using System.Diagnostics;
using Arkki.Finvoice;
using Arkki.Pdf;
using Arkki.Pdf.Fonts;

// Usage: Arkki.Samples.Invoice [finvoice.xml] [logo.png] [output.pdf]
string root = FindRepositoryRoot();
string xmlPath = args.Length > 0 ? args[0] : Path.Combine(root, "samples", "data", "finvoice-sample.xml");
string logoPath = args.Length > 1 ? args[1] : Path.Combine(root, "samples", "data", "logo.png");
string outPath = args.Length > 2 ? args[2] : Path.Combine(root, "samples", "output", "lasku.pdf");

// Fonts are parsed once and shared by every document rendered in the process.
var theme = new InvoiceTheme
{
    RegularFont = TrueTypeFont.Load(File.ReadAllBytes(Path.Combine(root, "samples", "fonts", "LiberationSans-Regular.ttf"))),
    BoldFont = TrueTypeFont.Load(File.ReadAllBytes(Path.Combine(root, "samples", "fonts", "LiberationSans-Bold.ttf"))),
    Logo = File.Exists(logoPath) ? PdfImage.Load(File.ReadAllBytes(logoPath)) : null,
};

FinvoiceInvoice invoice = FinvoiceReader.Load(xmlPath);
byte[] pdf = FinvoiceRenderer.RenderToArray(invoice, theme);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllBytes(outPath, pdf);
Console.WriteLine($"Wrote {outPath} ({pdf.Length / 1024.0:F1} kB), {invoice.Rows.Count} rows.");

// Rough throughput figures: render the same invoice repeatedly after a warm-up.
foreach (var level in new[] { System.IO.Compression.CompressionLevel.Optimal, System.IO.Compression.CompressionLevel.Fastest })
{
    var t = new InvoiceTheme { RegularFont = theme.RegularFont, BoldFont = theme.BoldFont, Logo = theme.Logo, Compression = level };
    for (int i = 0; i < 30; i++) FinvoiceRenderer.RenderToArray(invoice, t);
    const int runs = 300;
    var sw = Stopwatch.StartNew();
    int bytes = 0;
    for (int i = 0; i < runs; i++) bytes = FinvoiceRenderer.RenderToArray(invoice, t).Length;
    sw.Stop();
    Console.WriteLine($"{level,-8} {sw.Elapsed.TotalMilliseconds / runs:F2} ms per invoice, {bytes / 1024.0:F1} kB (single thread, {runs} runs)");
}

static string FindRepositoryRoot()
{
    string? dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "Arkki.slnx"))) dir = Path.GetDirectoryName(dir);
    return dir ?? Directory.GetCurrentDirectory();
}
