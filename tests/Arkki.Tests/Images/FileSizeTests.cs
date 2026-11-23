using System.Text;
using Arkki.Pdf;
using Arkki.Pdf.Layout;
using Arkki.Samples.Finvoice;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Output stays small: images are stored in their cheapest lossless form, fonts are subset.</summary>
internal static class FileSizeTests
{
    public static void Run()
    {
        // A grey picture stored as RGB is written as one grey channel.
        byte[] greyPng = PngWriter.Write(300, 200, (x, y) => { byte v = (byte)((x * 7 + y * 13) % 256); return (v, v, v, 255); }, alpha: false);
        string grey = ImageDictionary(PdfImage.Load(greyPng));
        Check("grey RGB image is stored as DeviceGray", grey.Contains("/DeviceGray") && !grey.Contains("/DeviceRGB"), grey);

        // A picture with few colours is written with a palette.
        (byte, byte, byte, byte)[] colours = [(29, 53, 87, 255), (42, 157, 143, 255), (255, 255, 255, 255)];
        byte[] flatPng = PngWriter.Write(300, 200, (x, y) => colours[(x / 20 + y / 20) % 3], alpha: false);
        string flat = ImageDictionary(PdfImage.Load(flatPng));
        Check("image with few colours is indexed", flat.Contains("/Indexed"), flat);

        // A smooth photo-like gradient compresses better with PNG prediction.
        byte[] gradientPng = PngWriter.Write(400, 300, (x, y) => ((byte)x, (byte)y, (byte)((x + y) / 3), 255), alpha: false);
        PdfImage gradient = PdfImage.Load(gradientPng);
        Check("gradient uses PNG prediction", ImageDictionary(gradient).Contains("/Predictor 15"));
        Check("gradient compresses well", gradient.EncodedLength < 400 * 300 * 3 / 20, gradient.EncodedLength + " bytes");

        // Opaque alpha is dropped; real alpha becomes a soft mask.
        byte[] opaquePng = PngWriter.Write(50, 50, (x, y) => ((byte)x, 0, 0, 255), alpha: true);
        Check("fully opaque alpha adds no soft mask", !ImageDictionary(PdfImage.Load(opaquePng)).Contains("/SMask"));
        byte[] alphaPng = PngWriter.Write(50, 50, (x, y) => (200, 0, 0, (byte)(x * 5)), alpha: true);
        Check("alpha channel becomes a soft mask", ImageDictionary(PdfImage.Load(alphaPng)).Contains("/SMask"));

        // The soft mask blends: half-transparent red over a blue square renders purple, a transparent pixel shows the blue.
        byte[] halfPng = PngWriter.Write(20, 20, (x, y) => x < 10 ? ((byte)255, (byte)0, (byte)0, (byte)128) : ((byte)255, (byte)0, (byte)0, (byte)0), alpha: true);
        var blendDoc = new PdfDocument();
        var blendPage = blendDoc.AddPage(100, 100);
        blendPage.Canvas.SetFillColor(new Color(0, 0, 1));
        blendPage.Canvas.FillRect(0, 0, 100, 100);
        blendPage.Canvas.Image(PdfImage.Load(halfPng), 0, 0, 100, 100);
        var px = External.Render(blendDoc.ToArray());
        if (px is not null)
        {
            (byte R, byte G, byte B) At(int x, int y) { int i = (y * px.Value.Width + x) * 3; return (px.Value.Rgb[i], px.Value.Rgb[i + 1], px.Value.Rgb[i + 2]); }
            var half = At(25, 50); var clear = At(75, 50);
            Check("half-transparent PNG blends with what is under it", half.R is > 100 and < 160 && half.B is > 100 and < 160, half.ToString());
            Check("fully transparent PNG pixels show what is under them", clear.R < 20 && clear.B > 235, clear.ToString());
        }

        // Downscaling on load keeps the aspect ratio and shrinks the data.
        PdfImage big = PdfImage.Load(gradientPng);
        PdfImage small = PdfImage.Load(gradientPng, 100, 100);
        Equal("downscaled width", 100, small.Width);
        Equal("downscaled height", 75, small.Height);
        Check("downscaled image is smaller", small.EncodedLength * 4 < big.EncodedLength, $"{small.EncodedLength} vs {big.EncodedLength}");
        Equal("an image within the limits is not resized", 400, PdfImage.Load(gradientPng, 1000, 1000).Width);

        // An image used on several pages is embedded once.
        var reuse = new PdfDocument();
        for (int i = 0; i < 5; i++) reuse.AddPage().Canvas.Image(big, 0, 0, 100, 75);
        byte[] reused = reuse.ToArray();
        Check("image on five pages is embedded once", reused.Length < big.EncodedLength + 3000, $"{reused.Length} bytes, image {big.EncodedLength}");

        // A short text embeds only the glyphs it uses.
        var text = new PdfDocument();
        var family = new FontFamily(text.AddFont(TestData.Regular), text.AddFont(TestData.Bold));
        new FlowDocument { Content = new TextBlock("Hello, world", new TextStyle(family, 12)) }.RenderTo(text);
        byte[] hello = text.ToArray();
        Check("one line of text is a small file", hello.Length < 9_000, hello.Length + " bytes");

        // The three-page sample invoice with its logo stored at 300 dpi.
        var theme = new InvoiceTheme
        {
            RegularFont = TestData.Regular, BoldFont = TestData.Bold,
            Logo = PdfImage.Load(File.ReadAllBytes(TestData.LogoPath), 733, 213),
            CreationDate = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero),
        };
        byte[] invoice = FinvoiceRenderer.RenderToArray(TestData.LoadInvoice(), theme);
        Check("three-page invoice with logo is under 70 kB", invoice.Length < 70_000, invoice.Length + " bytes");

        // A batch of one hundred invoices with the same logo stores the logo once.
        byte[] batch = PdfDocument.Merge(Enumerable.Repeat(invoice, 100)).ToArray();
        Check("100 merged invoices are not 100 times one invoice", batch.Length < invoice.Length * 4, $"{batch.Length} bytes for 100 x {invoice.Length}");
    }

    /// <summary>The dictionary of the image XObject in a one-page document showing the image.</summary>
    private static string ImageDictionary(PdfImage image)
    {
        var doc = new PdfDocument();
        doc.AddPage().Canvas.Image(image, 0, 0, 100, 100);
        string pdf = Encoding.Latin1.GetString(doc.ToArray());
        // The soft mask is written before its image, so the image is the last one.
        int at = pdf.LastIndexOf("/Subtype /Image", StringComparison.Ordinal);
        return at < 0 ? "" : pdf[at..pdf.IndexOf("stream", at, StringComparison.Ordinal)];
    }
}
