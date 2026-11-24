using Arkki.Pdf;
using Arkki.Pdf.Barcodes;
using Arkki.Pdf.Layout;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Bad arguments to the public API fail at once with a clear exception, not later with a broken file.</summary>
internal static class ValidationTests
{
    public static void Run()
    {
        var doc = new PdfDocument();
        PdfFont font = doc.AddFont(TestData.Regular);
        PdfCanvas c = doc.AddPage().Canvas;
        var family = new FontFamily(font, font);

        Throws<ArgumentOutOfRangeException>("canvas: NaN coordinate", () => c.FillRect(double.NaN, 0, 10, 10));
        Throws<ArgumentOutOfRangeException>("canvas: infinite size", () => c.Line(0, 0, double.PositiveInfinity, 0));
        Throws<ArgumentOutOfRangeException>("canvas: zero font size", () => c.Text(font, 0, 10, 10, "x"));
        Throws<ArgumentOutOfRangeException>("canvas: negative font size", () => c.Text(font, -5, 10, 10, "x"));
        Throws<ArgumentOutOfRangeException>("canvas: negative line width", () => c.SetLineWidth(-1));
        Throws<ArgumentOutOfRangeException>("canvas: NaN opacity", () => c.SetOpacity(double.NaN));
        Throws<ArgumentOutOfRangeException>("canvas: NaN colour", () => c.SetFillColor(new Color(double.NaN, 0, 0)));
        Throws<ArgumentNullException>("canvas: null font", () => c.Text(null!, 10, 10, 10, "x"));
        Throws<ArgumentNullException>("canvas: null image", () => c.Image(null!, 0, 0, 10, 10));
        c.SetFillColor(new Color(2, -1, 0.5)); // out-of-range components are clamped, not refused

        Throws<FormatException>("colour: bad hex", () => Color.FromHex("#12345"));
        Throws<FormatException>("colour: not hex", () => Color.FromHex("zzzzzz"));
        Equal("colour: hex parses", new Color(1, 0, 0), Color.FromHex("#FF0000"));

        Throws<ArgumentOutOfRangeException>("style: zero size", () => new TextStyle(family, 0));
        Throws<ArgumentOutOfRangeException>("style: NaN size through with", () => _ = new TextStyle(family) with { Size = double.NaN });
        Throws<ArgumentOutOfRangeException>("style: zero line height", () => new TextStyle(family, 10, LineHeight: 0));
        Throws<ArgumentNullException>("style: null family", () => new TextStyle(null!));

        Throws<ArgumentOutOfRangeException>("page: zero width", () => doc.AddPage(0, 100));
        Throws<ArgumentOutOfRangeException>("page: larger than PDF allows", () => doc.AddPage(20000, 100));
        Throws<ArgumentException>("text block: no spans", () => new TextBlock([]));
        Throws<NotSupportedException>("image: an unknown format names the supported ones", () => PdfImage.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        Throws<InvalidDataException>("image: truncated PNG", () => PdfImage.Load(File.ReadAllBytes(TestData.LogoPath)[..100]));
        Throws<ArgumentException>("EAN-13: wrong length", () => new Ean13().Encode("123"));
        Throws<ArgumentException>("EAN-13: letters", () => new Ean13().Encode("12345678901AB"));

        // Damaged images: every mutation of the logo loads or fails with InvalidDataException / NotSupportedException.
        byte[] logo = File.ReadAllBytes(TestData.LogoPath);
        var rng = new Random(5);
        int bad = 0;
        for (int i = 0; i < 400; i++)
        {
            byte[] m = (byte[])logo.Clone();
            if (i % 3 == 0) m = m[..rng.Next(9, m.Length)];
            for (int k = 0; k < 4; k++) m[rng.Next(8, m.Length)] = (byte)rng.Next(256);
            try { PdfImage.Load(m); }
            catch (InvalidDataException) { }
            catch (NotSupportedException) { }
            catch (Exception e) { if (bad++ < 3) Console.WriteLine("  image: " + e.GetType().Name + " " + e.Message); }
        }
        Equal("image: 400 damaged PNGs fail only with documented exceptions", 0, bad);
    }
}
