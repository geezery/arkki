using System.Globalization;
using System.Text;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf.Signing.Internal;

/// <summary>
/// Builds the appearance stream of a visible signature inside an incremental update. Text in Helvetica is written
/// straight into the stream. An image, an embedded font or the caller's own drawing is laid out on a one-page Arkki
/// document of the box's size, and that page is copied in as a form the appearance paints first.
/// </summary>
internal static class AppearanceBuilder
{
    private const double Pad = 3;
    private const double Leading = 1.2;

    /// <summary>Writes the appearance into the update and returns its object number.</summary>
    public static int Build(IncrementalUpdate update, PdfSignatureAppearance look, double width, double height, string defaultText)
    {
        string text = look.Text ?? defaultText;
        bool hasText = text.Length > 0;
        PdfImage? image = look.Image is null ? null : PdfImage.Load(look.Image);

        // The image takes the left two fifths when there is text beside it, otherwise the whole box.
        double innerW = Math.Max(0, width - 2 * Pad), innerH = Math.Max(0, height - 2 * Pad);
        double textX = Pad, textW = innerW;
        (double X, double Y, double W, double H) imageBox = default;
        if (image is not null)
        {
            double areaW = hasText ? innerW * 0.4 : innerW;
            double scale = Math.Min(areaW / image.Width, innerH / image.Height);
            imageBox = (Pad + (areaW - image.Width * scale) / 2, Pad + (innerH - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
            if (hasText) { textX = Pad + areaW + Pad; textW = Math.Max(0, width - textX - Pad); }
        }

        var content = new ByteBuf(256);
        var resources = new StringBuilder();
        if (image is not null || look.Font is not null || look.Draw is not null)
        {
            var doc = new PdfDocument();
            PdfCanvas canvas = doc.AddPage(width, height).Canvas;
            if (image is not null && imageBox.W > 0 && imageBox.H > 0) canvas.Image(image, imageBox.X, imageBox.Y, imageBox.W, imageBox.H);
            if (hasText && look.Font is not null)
            {
                PdfFont font = doc.AddFont(look.Font);
                var (lines, size) = Fit(text, textW, innerH, look.FontSize, (s, at) => font.Width(s, at));
                double y = FirstBaseline(lines.Count, size, innerH);
                foreach (string line in lines)
                {
                    canvas.Text(font, size, textX, y, line);
                    y += size * Leading;
                }
            }
            look.Draw?.Invoke(doc, canvas);
            int form = ImportPage(update, doc.ToArray(), width, height);
            content.Ascii("q /Bg Do Q\n");
            resources.Append(CultureInfo.InvariantCulture, $"/XObject << /Bg {form} 0 R >> ");
        }

        if (look.Border)
            content.Ascii("q 0.6 0.6 0.6 RG 1 w 0.5 0.5 ").Num(Math.Max(0, width - 1)).Sp().Num(Math.Max(0, height - 1)).Ascii(" re S Q\n");

        if (hasText && look.Font is null)
        {
            var (lines, size) = Fit(text, textW, innerH, look.FontSize, Helvetica.Width);
            double baseline = FirstBaseline(lines.Count, size, innerH);
            content.Ascii("BT /Helv ").Num(size).Ascii(" Tf 0 g ").Num(size * Leading).Ascii(" TL ").Num(textX).Sp().Num(height - baseline).Ascii(" Td ");
            foreach (string line in lines)
            {
                PdfSerializer.WriteString(content, WinAnsi.Encode(line), hex: false);
                content.Ascii(" Tj T* ");
            }
            content.Ascii("ET\n");
            int helvetica = update.Reserve();
            update.Put(helvetica, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"u8);
            resources.Append(CultureInfo.InvariantCulture, $"/Font << /Helv {helvetica} 0 R >> ");
        }

        int id = update.Reserve();
        update.PutStream(id, string.Create(CultureInfo.InvariantCulture,
            $"/Type /XObject /Subtype /Form /BBox [0 0 {width:0.###} {height:0.###}] /Resources << {resources}>>"), content.Span);
        return id;
    }

    /// <summary>Copies the only page of a document Arkki just wrote into the update as a form XObject.</summary>
    private static int ImportPage(IncrementalUpdate update, byte[] pdf, double width, double height)
    {
        PdfReader source = PdfReader.Open(pdf);
        ReaderPage page = source.Pages[0];
        if (page.Dict.Get("Contents") is not PdfRef contentsRef || source.Resolve(contentsRef) is not PdfStream contents)
            throw new InvalidOperationException("The appearance page has no single content stream.");
        var copied = new Dictionary<int, int>();
        var entries = new ByteBuf(256);
        entries.Ascii(update.ImportEntries(source, contents, copied));
        entries.Ascii(string.Create(CultureInfo.InvariantCulture, $"/Type /XObject /Subtype /Form /BBox [0 0 {width:0.###} {height:0.###}] /Resources "));
        PdfValue resources = source.Resolve(page.Resources) is { } r ? update.Import(source, r, copied) : new PdfDict([]);
        PdfSerializer.Write(entries, resources, IncrementalUpdate.MapRef);
        int id = update.Reserve();
        update.PutStream(id, Encoding.Latin1.GetString(entries.Span), contents.Data.Span);
        return id;
    }

    /// <summary>The first line's baseline, measured down from the box's top, that centres the lines in the box.</summary>
    private static double FirstBaseline(int lines, double size, double innerHeight)
    {
        double block = lines * size * Leading;
        // 0.855: where the baseline sits in a line 1.2 sizes tall, for a font with Helvetica's ascent and descent.
        return Pad + Math.Max(0, (innerHeight - block) / 2) + size * 0.855;
    }

    /// <summary>Wraps the text to the width, at the size asked for or at the largest size up to 12 that fits the height.</summary>
    internal static (List<string> Lines, double Size) Fit(string text, double width, double height, double fixedSize, Func<string, double, double> measure)
    {
        double size = fixedSize > 0 ? fixedSize : 12;
        while (true)
        {
            List<string> lines = Wrap(text, width, size, measure);
            if (fixedSize > 0 || size <= 4 || lines.Count * size * Leading <= height) return (lines, size);
            size -= 0.5;
        }
    }

    private static List<string> Wrap(string text, double width, double size, Func<string, double, double> measure)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string longer = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && measure(longer, size) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = longer;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>The advance widths of Helvetica, the standard font every viewer has, for fitting text without the font file.</summary>
    internal static class Helvetica
    {
        // Characters 32 to 126, in thousandths of the font size (Adobe's Helvetica metrics).
        private static readonly short[] Ascii =
        [
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
        ];

        public static double Width(string text, double size)
        {
            double units = 0;
            foreach (char c in text)
            {
                // Accented letters are as wide as their base letters: capitals about 667, the rest 556.
                units += c is >= ' ' and <= '~' ? Ascii[c - 32] : char.IsUpper(c) ? 667 : 556;
            }
            return units * size / 1000;
        }
    }
}
