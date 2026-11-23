using Arkki.Pdf;
using Arkki.Pdf.Barcodes;
using Arkki.Pdf.Layout;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>EAN-13, Code 128 as a symbology, QR code and drawing barcodes.</summary>
internal static class BarcodeTests
{
    public static void Run()
    {
        Equal("ean-13 check digit", "4006381333931", Ean13.WithCheckDigit("400638133393"));
        Equal("ean-13 accepts a right check digit", "4006381333931", Ean13.WithCheckDigit("4006381333931"));
        Throws<ArgumentException>("ean-13 rejects a wrong check digit", () => Ean13.WithCheckDigit("4006381333932"));
        Throws<ArgumentException>("ean-13 rejects letters", () => new Ean13().Encode("40063813339A"));
        BarcodeMatrix ean = new Ean13().Encode("400638133393");
        Equal("ean-13 is 95 modules", 95, ean.Columns);
        Equal("ean-13 starts with a guard", "101", Row(ean)[..3]);
        Equal("ean-13 centre guard", "01010", Row(ean)[45..50]);
        // First digit 4 gives parity LGLLGG; the second digit 0 in set L is 0001101.
        Equal("ean-13 first left digit", "0001101", Row(ean)[3..10]);

        BarcodeMatrix b = new Code128().Encode("Arkki");
        Equal("code 128 set B width", 7 * 11 + 13, b.Columns);
        BarcodeMatrix c = new Code128().Encode("1234");
        Equal("code 128 picks set C for digits", 4 * 11 + 13, c.Columns);
        Throws<ArgumentException>("code 128 rejects non-ASCII", () => new Code128().Encode("ä"));

        BarcodeMatrix hello = new QrCode(QrErrorCorrection.Q).Encode("HELLO WORLD");
        Equal("qr version 1 is 21 modules", 21, hello.Columns);
        Equal("qr quiet zone", 4, hello.QuietZone);
        Check("qr finder corners are dark", hello[0, 0] && hello[20, 0] && hello[0, 20] && !hello[20, 20]);
        Equal("qr grows with the text", 25, new QrCode(QrErrorCorrection.H).Encode("HELLO WORLD HELLO").Columns);
        Equal("qr version 40", 177, new QrCode(QrErrorCorrection.L).Encode(new string('x', 2953)).Columns);
        Throws<ArgumentException>("qr rejects text that does not fit", () => new QrCode(QrErrorCorrection.L).Encode(new string('x', 2954)));

        var doc = new PdfDocument { Compression = System.IO.Compression.CompressionLevel.NoCompression };
        new FlowDocument
        {
            Content = new Column(
                new BarcodeBox(new QrCode(), "https://example.com/contracts/7", 80, 80),
                new BarcodeBox(new Ean13(), "400638133393", 120, 40)),
        }.RenderTo(doc);
        byte[] pdf = doc.ToArray();
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "barcodes.pdf"), pdf);
        string content = System.Text.Encoding.Latin1.GetString(pdf);
        Check("barcodes draw as filled rectangles", content.Contains(" re ") && content.Contains("f\n"));
        Equal("barcode box takes its size", LayoutStatus.None, new BarcodeBox(new Ean13(), "400638133393", 120, 40).Layout(200, 30).Status);
    }

    private static string Row(BarcodeMatrix m)
    {
        var chars = new char[m.Columns];
        for (int x = 0; x < m.Columns; x++) chars[x] = m[x, 0] ? '1' : '0';
        return new string(chars);
    }
}
