using System.Text;
using Arkki.Samples.Finvoice;
using Arkki.Pdf;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>The invoice renderer end to end: valid, deterministic, thread-safe output.</summary>
internal static class InvoiceRenderTests
{
    public static void Run()
    {
        FinvoiceInvoice inv = TestData.LoadInvoice();
        var theme = new InvoiceTheme
        {
            RegularFont = TestData.Regular, BoldFont = TestData.Bold,
            Logo = PdfImage.Load(File.ReadAllBytes(TestData.LogoPath)),
            CreationDate = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(2)),
        };
        PdfDocument doc = FinvoiceRenderer.Render(inv, theme);
        byte[] pdf = doc.ToArray();
        Check("pdf header", pdf.AsSpan(0, 8).SequenceEqual("%PDF-1.7"u8));
        Check("pdf trailer", Encoding.Latin1.GetString(pdf, pdf.Length - 6, 6) == "%%EOF\n");
        Check("invoice is multi-page", doc.Pages.Count >= 2, doc.Pages.Count.ToString());
        Check("output is deterministic", pdf.AsSpan().SequenceEqual(FinvoiceRenderer.RenderToArray(inv, theme)));
        Check("subset keeps the file small", pdf.Length < 150_000, pdf.Length + " bytes");

        // Parallel rendering shares the parsed fonts and must give identical bytes on every thread.
        var results = new byte[16][];
        Parallel.For(0, results.Length, i => results[i] = FinvoiceRenderer.RenderToArray(inv, theme));
        Check("parallel renders are identical", results.All(r => r.AsSpan().SequenceEqual(pdf)));
    }
}
