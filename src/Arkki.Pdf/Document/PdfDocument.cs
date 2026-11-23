using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Arkki.Pdf.Fonts;
using Arkki.Pdf.Internal;

namespace Arkki.Pdf;

/// <summary>
/// A PDF document under construction. Add fonts, images and pages, draw on the page canvases,
/// then call <see cref="Save"/>. A document instance is not thread-safe, but any number of
/// documents can be built in parallel and they can share <see cref="TrueTypeFont"/> instances.
/// </summary>
public sealed class PdfDocument
{
    public const string Producer = "Arkki 0.1";

    private readonly List<PdfPage> _pages = [];
    private readonly List<PdfFont> _fonts = [];
    private readonly List<PdfImage> _images = [];

    public PdfInfo Info { get; } = new();
    public IReadOnlyList<PdfPage> Pages => _pages;

    /// <summary>Natural language of the document as a BCP 47 tag, e.g. "fi-FI".</summary>
    public string? Language { get; set; }

    public CompressionLevel Compression { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// When true (default) viewers are told not to scale the page when printing, which keeps
    /// forms and barcodes at their exact physical size.
    /// </summary>
    public bool PrintAtActualSize { get; set; } = true;

    public PdfFont AddFont(TrueTypeFont face)
    {
        var font = new PdfFont(face, "F" + (_fonts.Count + 1));
        _fonts.Add(font);
        return font;
    }

    public PdfFont AddFont(byte[] trueTypeData) => AddFont(TrueTypeFont.Load(trueTypeData));

    /// <summary>Resource name of an image in this document; registers the image on first use.</summary>
    internal string ImageName(PdfImage image)
    {
        int index = _images.IndexOf(image);
        if (index < 0) { _images.Add(image); index = _images.Count - 1; }
        return "Im" + (index + 1);
    }

    public PdfPage AddPage(double width, double height)
    {
        var page = new PdfPage(this, width, height);
        _pages.Add(page);
        return page;
    }

    public PdfPage AddPage() => AddPage(PageSize.A4.Width, PageSize.A4.Height);

    public byte[] ToArray()
    {
        using var ms = new MemoryStream(64 * 1024);
        Save(ms);
        return ms.ToArray();
    }

    public void Save(Stream output)
    {
        var w = new PdfWriter(output) { Compression = Compression };
        int catalogId = w.Reserve(), pagesId = w.Reserve(), infoId = w.Reserve(), resourcesId = w.Reserve();
        var b = new ByteBuf(1024);

        // Page content streams.
        var pageIds = new int[_pages.Count];
        var contentIds = new int[_pages.Count];
        for (int i = 0; i < _pages.Count; i++)
        {
            pageIds[i] = w.Reserve();
            contentIds[i] = w.Reserve();
            w.WriteStream(contentIds[i], "", _pages[i].Canvas.Content, compress: Compression != CompressionLevel.NoCompression);
        }

        // Shared resources: only fonts that were actually used get embedded.
        var resources = new ByteBuf(256);
        resources.Ascii("<< /Font <<");
        foreach (PdfFont font in _fonts)
        {
            if (!font.IsUsed) continue;
            int id = w.Reserve();
            font.Write(w, id);
            resources.Ascii(" /").Ascii(font.ResourceName).Sp().Int(id).Ascii(" 0 R");
        }
        resources.Ascii(" >> /XObject <<");
        for (int i = 0; i < _images.Count; i++)
        {
            int id = w.Reserve();
            _images[i].Write(w, id);
            resources.Ascii(" /Im").Int(i + 1).Sp().Int(id).Ascii(" 0 R");
        }
        resources.Ascii(" >> >>");
        w.WriteObject(resourcesId, resources);

        for (int i = 0; i < _pages.Count; i++)
        {
            b.Reset();
            b.Ascii("<< /Type /Page /Parent ").Int(pagesId).Ascii(" 0 R /MediaBox [0 0 ")
                .Num(_pages[i].Width).Sp().Num(_pages[i].Height).Ascii("] /Resources ").Int(resourcesId)
                .Ascii(" 0 R /Contents ").Int(contentIds[i]).Ascii(" 0 R >>");
            w.WriteObject(pageIds[i], b);
        }

        b.Reset();
        b.Ascii("<< /Type /Pages /Count ").Int(_pages.Count).Ascii(" /Kids [");
        foreach (int id in pageIds) b.Int(id).Ascii(" 0 R ");
        b.Ascii("] >>");
        w.WriteObject(pagesId, b);

        b.Reset();
        b.Ascii("<< /Type /Catalog /Pages ").Int(pagesId).Ascii(" 0 R");
        if (!string.IsNullOrEmpty(Language)) b.Ascii(" /Lang (").Ascii(Language).Byte((byte)')');
        if (PrintAtActualSize) b.Ascii(" /ViewerPreferences << /PrintScaling /None >>");
        b.Ascii(" >>");
        w.WriteObject(catalogId, b);

        DateTimeOffset created = Info.CreationDate ?? DateTimeOffset.Now;
        string date = FormatDate(created);
        b.Reset();
        b.Ascii("<<");
        WriteInfo(b, "Title", Info.Title);
        WriteInfo(b, "Author", Info.Author);
        WriteInfo(b, "Subject", Info.Subject);
        WriteInfo(b, "Keywords", Info.Keywords);
        WriteInfo(b, "Creator", Info.Creator);
        WriteInfo(b, "Producer", Producer);
        b.Ascii(" /CreationDate (").Ascii(date).Ascii(") /ModDate (").Ascii(date).Ascii(") >>");
        w.WriteObject(infoId, b);

        byte[] id16 = MD5.HashData(Encoding.UTF8.GetBytes($"{Info.Title}|{date}|{_pages.Count}|{ContentFingerprint()}"));
        w.Finish(catalogId, infoId, id16);
    }

    private long ContentFingerprint()
    {
        long sum = 17;
        foreach (PdfPage p in _pages) sum = sum * 31 + p.Canvas.Content.Length;
        return sum;
    }

    private static void WriteInfo(ByteBuf b, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        b.Ascii(" /").Ascii(key).Sp().TextString(value);
    }

    private static string FormatDate(DateTimeOffset d)
    {
        TimeSpan o = d.Offset;
        char sign = o < TimeSpan.Zero ? '-' : '+';
        return $"D:{d:yyyyMMddHHmmss}{sign}{Math.Abs(o.Hours):00}'{Math.Abs(o.Minutes):00}'";
    }
}
