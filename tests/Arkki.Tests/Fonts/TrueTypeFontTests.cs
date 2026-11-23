using Arkki.Pdf;
using Arkki.Pdf.Fonts;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>TrueType parsing.</summary>
internal static class TrueTypeFontTests
{
    public static void Run()
    {
        var regular = TestData.Regular;
        Equal("font units per em", 2048, regular.UnitsPerEm);
        Check("font has Finnish, Estonian, Latvian and Lithuanian letters", "äöåÄÖÅõšžŠŽāēģķļņūąčęėįų€".All(c => regular.HasGlyph(c)));
        Equal("font postscript name", "LiberationSans", regular.PostScriptName);
        Check("a TrueType font is not CFF", !regular.IsCff);

        // Damaged files fail with a clear error, never an index exception.
        byte[] file = File.ReadAllBytes(Path.Combine(TestData.Root, "samples", "fonts", "LiberationSans-Regular.ttf"));
        Throws<InvalidDataException>("an empty font file is rejected", () => TrueTypeFont.Load([]));
        Throws<InvalidDataException>("a truncated font file is rejected", () => TrueTypeFont.Load(file[..2000]));
        Throws<InvalidDataException>("a non-font file is rejected", () => TrueTypeFont.Load(new byte[100]));
        Throws<NotSupportedException>("a font collection is refused with a reason", () => TrueTypeFont.Load([.. "ttcf"u8, .. new byte[100]]));
        var rng = new Random(7);
        int bad = 0;
        for (int i = 0; i < 300; i++)
        {
            byte[] mutated = (byte[])file.Clone();
            for (int j = 0; j < 8; j++) mutated[rng.Next(12, Math.Min(mutated.Length, 400))] = (byte)rng.Next(256);
            try { TrueTypeFont.Load(mutated); }
            catch (InvalidDataException) { }
            catch (NotSupportedException) { }
            catch (Exception e) { bad++; Console.WriteLine("  mutated font: " + e.GetType().Name + " " + e.Message); }
        }
        Equal("mutated font headers fail only with InvalidDataException", 0, bad);

        CffFont();
    }

    /// <summary>An OpenType font with CFF outlines is embedded whole and renders. Uses a system font; skipped without one.</summary>
    private static void CffFont()
    {
        string? path = new[]
        {
            "/usr/share/fonts/opentype/urw-base35/NimbusSans-Regular.otf",
            "/usr/share/fonts/opentype/inter/Inter-Regular.otf",
            "/System/Library/Fonts/Supplemental/Futura.ttc",
        }.FirstOrDefault(p => p.EndsWith(".otf") && File.Exists(p));
        if (path is null) { Console.WriteLine("skip  no CFF OpenType font on this machine: its checks are skipped"); return; }
        TrueTypeFont otf = TrueTypeFont.Load(File.ReadAllBytes(path));
        Check("an OTTO font is CFF", otf.IsCff);
        Check("CFF font maps letters", otf.HasGlyph('A') && otf.HasGlyph('ä'));
        var doc = new PdfDocument();
        PdfFont font = doc.AddFont(otf);
        doc.AddPage().Canvas.Text(font, 14, 50, 80, "CFF Ääkköset 123");
        byte[] pdf = doc.ToArray();
        External.QpdfClean("CFF font", pdf);
        string? text = External.PopplerText(pdf);
        if (text is not null) Check("CFF font text extracts", text.Contains("CFF Ääkköset 123"), text);
        var fonts = External.Run("pdffonts", pdf, "{file}");
        if (fonts is not null) Check("CFF font is embedded as an OpenType CID font", fonts.Value.Output.Contains("CID Type 0C (OT)") && fonts.Value.Output.Contains(" yes "), fonts.Value.Output);
        var px = External.Render(pdf);
        if (px is not null) Check("CFF font draws ink", px.Value.Rgb.Count(b => b < 128) > 100);
    }
}
