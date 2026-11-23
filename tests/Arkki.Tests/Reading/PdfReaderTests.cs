using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Reading files written by Arkki and by other producers, including damaged ones.</summary>
internal static class PdfReaderTests
{
    public static void Run()
    {
        foreach (string name in (string[])["arkki-2pages", "objstm", "uncompressed", "ghostscript", "incremental", "broken-xref", "broken-objstm", "link"])
        {
            PdfReader reader = PdfReader.Open(TestData.Pdf(name));
            Equal($"read {name}: page count", 2, reader.PageCount);
            var (w, h) = reader.GetPageSize(1);
            Check($"read {name}: A4 page size", Math.Abs(w - 595.28) < 0.1 && Math.Abs(h - 841.89) < 0.1, $"{w} x {h}");
        }

        PdfReader lo = PdfReader.Open(TestData.Pdf("libreoffice"));
        Equal("read libreoffice: page count", 1, lo.PageCount);

        Throws<NotSupportedException>("encrypted file is rejected", () => PdfReader.Open(TestData.Pdf("encrypted")));
        Throws<InvalidDataException>("non-PDF data is rejected", () => PdfReader.Open("hello, world"u8.ToArray()));
        Throws<InvalidDataException>("truncated PDF without catalog is rejected", () => PdfReader.Open("%PDF-1.7\n1 0 obj\n<< >>\nendobj\n"u8.ToArray()));
    }
}
