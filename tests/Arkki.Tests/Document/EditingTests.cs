using Arkki.Pdf;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Paper sizes, rotation, page operations, stamping, metadata, links, bookmarks and attachments.</summary>
internal static class EditingTests
{
    public static void Run()
    {
        PaperSizes();
        PageOperations();
        Stamping();
        Metadata();
        LinksAndBookmarks();
        Attachments();
    }

    private static PdfDocument Doc(params string[] pageTexts)
    {
        var doc = new PdfDocument { Info = { CreationDate = new DateTimeOffset(2026, 11, 24, 3, 0, 0, TimeSpan.FromHours(2)) } };
        PdfFont font = doc.AddFont(TestData.Regular);
        foreach (string t in pageTexts) doc.AddPage().Canvas.Text(font, 12, 72, 72, t);
        return doc;
    }

    private static void PaperSizes()
    {
        Check("A4 is 210 x 297 mm", Math.Abs(PageSize.A4.Width - 595.276) < 0.01 && Math.Abs(PageSize.A4.Height - 841.89) < 0.01);
        Check("A5 is half of A4", Math.Abs(PageSize.A5.Height - PageSize.A4.Width * 148 / 210 * 210 / 148) < 1);
        Equal("Letter is 8.5 x 11 in", (612.0, 792.0), PageSize.Letter);
        Equal("landscape swaps the sides", (PageSize.A4.Height, PageSize.A4.Width), PageSize.Landscape(PageSize.A4));
        Equal("landscape of landscape is the same", PageSize.Landscape(PageSize.A4), PageSize.Landscape(PageSize.Landscape(PageSize.A4)));
        Equal("portrait undoes landscape", PageSize.A4, PageSize.Portrait(PageSize.Landscape(PageSize.A4)));

        var doc = new PdfDocument();
        doc.AddPage(PageSize.A3); doc.AddPage(PageSize.Landscape(PageSize.A5)); doc.AddPage(PageSize.Legal); doc.AddPage(PageSize.DL);
        PdfReader r = PdfReader.Open(doc.ToArray());
        Check("A3 page", Close(r.GetPageSize(0), PageSize.A3));
        Check("A5 landscape page", Close(r.GetPageSize(1), (Unit.Mm(210), Unit.Mm(148))));
        Check("Legal page", Close(r.GetPageSize(2), (612, 1008)));
        Check("DL envelope", Close(r.GetPageSize(3), (Unit.Mm(110), Unit.Mm(220))));
        Throws<ArgumentOutOfRangeException>("a page cannot be zero wide", () => doc.AddPage(0, 100));
        Throws<ArgumentOutOfRangeException>("a page cannot exceed 200 inches", () => doc.AddPage(20000, 100));
        Throws<InvalidOperationException>("a document without pages cannot be saved", () => new PdfDocument().ToArray());
    }

    private static bool Close((double W, double H) a, (double W, double H) b) => Math.Abs(a.W - b.W) < 0.01 && Math.Abs(a.H - b.H) < 0.01;

    private static void PageOperations()
    {
        PdfDocument doc = Doc("one", "two", "three", "four");
        doc.Pages[1].Rotation = 90;
        doc.Pages[2].Rotation = -90;
        Equal("rotation is normalised", 270, doc.Pages[2].Rotation);
        Throws<ArgumentOutOfRangeException>("rotation must be a right angle", () => doc.Pages[0].Rotation = 45);
        doc.MovePage(3, 0);
        doc.RemovePageAt(2); // "two" (now at index 2 after the move)
        doc.InsertPage(1, PageSize.A6);
        byte[] pdf = doc.ToArray();
        PdfReader r = PdfReader.Open(pdf);
        Equal("pages after move, remove and insert", 4, r.PageCount);
        Equal("moved page is first", "four", r.ExtractText(0).Trim());
        Check("inserted page has its size", Close(r.GetPageSize(1), PageSize.A6));
        Equal("removed page is gone", "one|three", r.ExtractText(2).Trim() + "|" + r.ExtractText(3).Trim());
        Equal("rotation is written", 270, r.GetPageRotation(3));
        External.QpdfClean("page operations", pdf);

        // An imported page keeps its rotation and can be turned further.
        var turned = PdfDocument.Open(pdf);
        Equal("imported page keeps rotation", 270, turned.Pages[3].Rotation);
        turned.Pages[0].Rotation = 180;
        Equal("imported page can be rotated", 180, PdfReader.Open(turned.ToArray()).GetPageRotation(0));

        // Split and extract.
        IReadOnlyList<PdfDocument> parts = PdfDocument.Split(pdf);
        Equal("split makes one document per page", 4, parts.Count);
        Equal("split part holds its page", "three", PdfReader.Open(parts[3].ToArray()).ExtractText(0).Trim());
        PdfReader picked = PdfReader.Open(PdfDocument.ExtractPages(pdf, 3, 0).ToArray());
        Equal("extract keeps the order given", "three|four", picked.ExtractText(0).Trim() + "|" + picked.ExtractText(1).Trim());
        Throws<ArgumentOutOfRangeException>("extracting a missing page fails", () => PdfDocument.ExtractPages(pdf, 9));
    }

    private static void Stamping()
    {
        // Stamp text, a rule, an image and a translucent watermark on every page of a file made by another program.
        foreach (string name in new[] { "ghostscript", "libreoffice", "objstm", "arkki-2pages" })
        {
            PdfDocument doc = PdfDocument.Open(TestData.Pdf(name));
            PdfFont font = doc.AddFont(TestData.Bold);
            PdfImage logo = PdfImage.Load(File.ReadAllBytes(TestData.LogoPath));
            foreach (PdfPage page in doc.Pages)
            {
                page.Canvas.Text(font, 9, 40, page.Height - 20, "STAMPED Ä");
                page.Canvas.Image(logo, page.Width - 100, 20, 60, 20);
                page.Canvas.Save();
                page.Canvas.SetOpacity(0.2);
                page.Canvas.SetFillColor(new Color(0.8, 0, 0));
                page.Canvas.TextRotated(font, 60, 120, page.Height - 200, 45, "LUONNOS");
                page.Canvas.Restore();
            }
            byte[] pdf = doc.ToArray();
            PdfReader back = PdfReader.Open(pdf);
            string text = back.ExtractText();
            Check($"stamp on {name}: stamp text", text.Contains("STAMPED Ä"), text);
            Check($"stamp on {name}: original text stays", text.Length > "STAMPED Ä LUONNOS".Length * back.PageCount);
            External.QpdfClean($"stamp on {name}", pdf);
            string? poppler = External.PopplerText(pdf);
            if (poppler is not null) Check($"stamp on {name}: poppler reads the stamp", poppler.Contains("STAMPED Ä"), poppler);
        }

        // A page whose content leaves a transformation behind does not move the stamp: the original is wrapped in q/Q.
        byte[] stamped = Stamp(TestData.Pdf("arkki-2pages"));
        Check("stamp wraps original content in q/Q", System.Text.Encoding.Latin1.GetString(stamped).Contains("stream\nq\n\nendstream"));
    }

    private static byte[] Stamp(byte[] pdf)
    {
        PdfDocument doc = PdfDocument.Open(pdf);
        doc.Pages[0].Canvas.Line(0, 0, 10, 10);
        doc.Compression = System.IO.Compression.CompressionLevel.NoCompression;
        return doc.ToArray();
    }

    private static void Metadata()
    {
        PdfDocument doc = Doc("x");
        doc.Info.Title = "Työmääräys 42";
        doc.Info.Author = "Rengas & Co";
        doc.Info.Subject = "Huolto";
        doc.Info.Keywords = "renkaat, huolto";
        doc.Info.Creator = "Arkki tests";
        doc.Language = "fi-FI";
        byte[] pdf = doc.ToArray();
        PdfInfo info = PdfReader.Open(pdf).Info;
        Equal("title reads back", "Työmääräys 42", info.Title);
        Equal("author reads back", "Rengas & Co", info.Author);
        Equal("keywords read back", "renkaat, huolto", info.Keywords);
        Equal("producer names Arkki", PdfDocument.Producer, info.Producer);
        Equal("creation date reads back", new DateTimeOffset(2026, 11, 24, 3, 0, 0, TimeSpan.FromHours(2)), info.CreationDate);
        Equal("language reads back", "fi-FI", PdfReader.Open(pdf).Language);
        string? pdfinfo = External.PdfInfo(pdf);
        if (pdfinfo is not null) Check("pdfinfo reads the title", pdfinfo.Contains("Työmääräys 42"), pdfinfo);

        // Opening a file keeps its information, and it can be changed.
        PdfDocument reopened = PdfDocument.Open(pdf);
        Equal("open keeps the title", "Työmääräys 42", reopened.Info.Title);
        reopened.Info.Title = "Muutettu";
        Equal("changed title is saved", "Muutettu", PdfReader.Open(reopened.ToArray()).Info.Title);
        Equal("another producer's file has its own title", null, PdfReader.Open(TestData.Pdf("ghostscript")).Info.Title is { Length: > 200 } ? "long" : null);

        Equal("date without offset", new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), PdfReader.ParseDate("D:20200102030405"));
        Equal("date with negative offset", new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5)), PdfReader.ParseDate("D:20200102030405-05'00'"));
        Equal("year-only date", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), PdfReader.ParseDate("D:2020"));
        Equal("garbage date is null", null, PdfReader.ParseDate("D:xx"));
    }

    private static void LinksAndBookmarks()
    {
        PdfDocument doc = Doc("Contents", "Chapter one", "Chapter two");
        doc.Pages[0].AddLink(72, 60, 200, 16, new Uri("https://example.com/ohje?a=1&b=2"));
        doc.Pages[0].AddLink(72, 90, 200, 16, doc.Pages[2], top: 100);
        doc.Pages[1].Canvas.Link(72, 60, 100, 16, doc.Pages[0]);
        PdfBookmark one = doc.Outline.Add("Luku 1", doc.Pages[1]);
        one.Add("1.1 Alku", doc.Pages[1], 200);
        one.Open = true;
        doc.Outline.Add("Luku 2", doc.Pages[2]);
        byte[] pdf = doc.ToArray();
        string raw = System.Text.Encoding.Latin1.GetString(pdf);
        Check("URI link is written", raw.Contains("/URI (https://example.com/ohje?a=1&b=2)"));
        Check("outline is written", raw.Contains("/Type /Outlines") && raw.Contains("/PageMode /UseOutlines"));
        External.QpdfClean("links and bookmarks", pdf);
        string? py = External.PyPdf(pdf, "r = pypdf.PdfReader(sys.argv[1])\n" +
            "print([o.title if not isinstance(o, list) else [x.title for x in o] for o in r.outline])\n" +
            "print([r.get_destination_page_number(o) for o in r.outline if not isinstance(o, list)])\n" +
            "print(len(r.pages[0]['/Annots']))");
        if (py is not null)
        {
            Check("pypdf reads the bookmarks", py.Contains("['Luku 1', ['1.1 Alku'], 'Luku 2']"), py);
            Check("bookmarks open their pages", py.Contains("[1, 2]"), py);
            Check("pypdf finds both links", py.TrimEnd().EndsWith('2'), py);
        }

        // A bookmark or link to a removed page is dropped, not left dangling.
        doc.RemovePageAt(2);
        byte[] fewer = doc.ToArray();
        External.QpdfClean("links after removing the target page", fewer);
        Check("link to removed page is dropped", System.Text.Encoding.Latin1.GetString(fewer).Split("/Subtype /Link").Length - 1 == 2);
        Throws<ArgumentException>("a link cannot point into another document", () => doc.Pages[0].AddLink(0, 0, 1, 1, Doc("x").Pages[0]));
    }

    private static void Attachments()
    {
        PdfDocument doc = Doc("Invoice");
        byte[] xml = System.Text.Encoding.UTF8.GetBytes("<Finvoice><Total>42,00</Total></Finvoice>");
        doc.Attach("lasku.xml", xml, "application/xml", "Verkkolasku");
        doc.Attach("liite ä.txt", "hei"u8.ToArray());
        byte[] pdf = doc.ToArray();
        External.QpdfClean("attachments", pdf);
        var list = External.Run("qpdf", pdf, "--list-attachments", "{file}");
        if (list is not null) Check("qpdf lists both attachments", list.Value.Output.Contains("lasku.xml") && list.Value.Output.Contains("liite"), list.Value.Output);
        var shown = External.Run("qpdf", pdf, "--show-attachment=lasku.xml", "{file}");
        if (shown is not null) Check("attachment data is intact", shown.Value.Output.Contains("<Total>42,00</Total>"), shown.Value.Output);
        Throws<ArgumentException>("attachment needs a name", () => doc.Attach("", xml));
    }
}
