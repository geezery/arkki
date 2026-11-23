using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Arkki.Pdf.Fonts;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Merging;
using Arkki.Pdf.Reading;

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

    private readonly List<double> _opacities = [];

    /// <summary>Resource name of a graphics state with the given fill and stroke opacity.</summary>
    internal string OpacityName(double opacity)
    {
        int index = _opacities.IndexOf(opacity);
        if (index < 0) { _opacities.Add(opacity); index = _opacities.Count - 1; }
        return "Gs" + (index + 1);
    }

    public PdfPage AddPage(double width, double height)
    {
        var page = new PdfPage(this, width, height);
        _pages.Add(page);
        return page;
    }

    public PdfPage AddPage() => AddPage(PageSize.A4.Width, PageSize.A4.Height);

    /// <summary>
    /// Appends copies of all pages of <paramref name="source"/>. The pages keep their content,
    /// resources and annotations exactly; nothing is re-rendered. The reader must stay alive until
    /// the document is saved.
    /// </summary>
    public IReadOnlyList<PdfPage> ImportPages(PdfReader source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var added = new List<PdfPage>(source.PageCount);
        for (int i = 0; i < source.PageCount; i++) added.Add(ImportPage(source, i));
        return added;
    }

    /// <summary>Appends a copy of one page of <paramref name="source"/> (zero-based <paramref name="index"/>).</summary>
    public PdfPage ImportPage(PdfReader source, int index)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, source.PageCount);
        var page = new PdfPage(this, source, index);
        _pages.Add(page);
        return page;
    }

    /// <summary>
    /// Combines whole PDF files into one, in order. Resources that are identical across the
    /// inputs, such as a logo or a font subset repeated in every invoice of a batch, are stored once.
    /// </summary>
    public static PdfDocument Merge(IEnumerable<byte[]> pdfs)
    {
        ArgumentNullException.ThrowIfNull(pdfs);
        var doc = new PdfDocument { PrintAtActualSize = false };
        foreach (byte[] pdf in pdfs) doc.ImportPages(PdfReader.Open(pdf));
        return doc;
    }

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
        PageImporter? importer = null;
        for (int i = 0; i < _pages.Count; i++)
        {
            pageIds[i] = w.Reserve();
            PdfPage page = _pages[i];
            if (page.IsImported)
            {
                if (!page.Canvas.IsEmpty)
                    throw new NotSupportedException("Drawing on an imported page is not supported.");
                importer ??= new PageImporter(w, Compression);
                importer.RegisterPage(page.Source!, page.SourcePage!, pageIds[i]);
                continue;
            }
            contentIds[i] = w.Reserve();
            w.WriteStream(contentIds[i], "", page.Canvas.Content, compress: Compression != CompressionLevel.NoCompression);
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
        resources.Ascii(" >>");
        if (_opacities.Count > 0)
        {
            resources.Ascii(" /ExtGState <<");
            for (int i = 0; i < _opacities.Count; i++)
                resources.Ascii(" /Gs").Int(i + 1).Ascii(" << /ca ").Num(_opacities[i]).Ascii(" /CA ").Num(_opacities[i]).Ascii(" >>");
            resources.Ascii(" >>");
        }
        resources.Ascii(" >>");
        w.WriteObject(resourcesId, resources);

        for (int i = 0; i < _pages.Count; i++)
        {
            if (_pages[i].IsImported)
            {
                importer!.WritePage(_pages[i].Source!, _pages[i].SourcePage!, pageIds[i], pagesId);
                continue;
            }
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
