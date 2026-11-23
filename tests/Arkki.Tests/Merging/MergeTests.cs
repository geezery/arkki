using System.Text;
using Arkki.Pdf;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Importing pages and merging files: content survives, and shared resources are stored once.</summary>
internal static class MergeTests
{
    public static void Run()
    {
        // Every kind of input merges into one valid file that reads back with all pages.
        string[] inputs = ["arkki-2pages", "objstm", "uncompressed", "ghostscript", "libreoffice", "incremental", "broken-xref", "broken-objstm", "link"];
        byte[] merged = PdfDocument.Merge(inputs.Select(TestData.Pdf)).ToArray();
        PdfReader back = PdfReader.Open(merged);
        Equal("merged page count", 17, back.PageCount);
        Equal("merged file has one cross-reference table", 1, Count(merged, "\nxref\n"));

        // The same document merged ten times costs little more than once: every page differs
        // only in its content stream, while fonts are shared.
        byte[] one = TestData.Pdf("arkki-2pages");
        byte[] ten = PdfDocument.Merge(Enumerable.Repeat(one, 10)).ToArray();
        Equal("ten copies have twenty pages", 20, PdfReader.Open(ten).PageCount);
        Check("identical resources are stored once", ten.Length < one.Length * 2, $"{ten.Length} bytes for 10 x {one.Length}");

        // Streams stored without compression are compressed on import.
        byte[] raw = TestData.Pdf("uncompressed");
        byte[] imported = PdfDocument.Merge([raw]).ToArray();
        Check("uncompressed input is compressed", imported.Length < raw.Length * 0.85, $"{imported.Length} from {raw.Length}");

        // Importing only the first page must not drag in the second through the link annotation.
        PdfReader link = PdfReader.Open(TestData.Pdf("link"));
        var firstOnly = new PdfDocument();
        firstOnly.ImportPage(link, 0);
        byte[] firstBytes = firstOnly.ToArray();
        var both = new PdfDocument();
        both.ImportPages(link);
        Equal("one imported page", 1, PdfReader.Open(firstBytes).PageCount);
        Check("link to a page that is not imported is dropped", firstBytes.Length < both.ToArray().Length && Count(firstBytes, "Second page") == 0);

        // Imported pages keep their size, and mixing them with new pages works.
        var mixed = new PdfDocument();
        PdfPage cover = mixed.AddPage();
        cover.Canvas.Line(0, 0, 100, 100);
        IReadOnlyList<PdfPage> pages = mixed.ImportPages(PdfReader.Open(TestData.Pdf("libreoffice")));
        Check("imported page reports its size", pages[0].IsImported && Math.Abs(pages[0].Width - 595.3) < 0.1);
        Equal("new and imported pages together", 2, PdfReader.Open(mixed.ToArray()).PageCount);

        var drawn = new PdfDocument();
        drawn.ImportPage(PdfReader.Open(one), 0).Canvas.Line(0, 0, 10, 10);
        Equal("drawing on an imported page is a stamp", 1, PdfReader.Open(drawn.ToArray()).PageCount);
    }

    private static int Count(byte[] haystack, string needle)
    {
        byte[] n = Encoding.Latin1.GetBytes(needle);
        int count = 0;
        for (int i = haystack.AsSpan().IndexOf(n); i >= 0; )
        {
            count++;
            int next = haystack.AsSpan(i + 1).IndexOf(n);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return count;
    }
}
