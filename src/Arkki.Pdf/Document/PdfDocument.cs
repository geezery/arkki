using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Arkki.Pdf.Fonts;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Merging;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf;

/// <summary>
/// A PDF document: built from scratch, or opened from an existing file to be stamped, filled, reordered or merged.
/// Add fonts, images and pages, draw on the page canvases, then call <see cref="Save"/>. A document instance is not
/// thread-safe, but any number of documents can be built in parallel and they can share <see cref="TrueTypeFont"/> and
/// <see cref="PdfImage"/> instances.
/// </summary>
public sealed class PdfDocument
{
    /// <summary>The /Producer written into every document's metadata.</summary>
    public const string Producer = "Arkki 1.0";

    private readonly List<PdfPage> _pages = [];
    private readonly List<PdfFont> _fonts = [];
    private readonly List<PdfImage> _images = [];
    private readonly List<PdfAttachment> _attachments = [];
    private readonly List<double> _opacities = [];

    /// <summary>Title, author and the other metadata written to the file.</summary>
    public PdfInfo Info { get; } = new();
    /// <summary>The pages in order.</summary>
    public IReadOnlyList<PdfPage> Pages => _pages;

    /// <summary>Natural language of the document as a BCP 47 tag, e.g. "fi-FI".</summary>
    public string? Language { get; set; }

    /// <summary>How hard content streams and fonts are compressed: Optimal (default) or Fastest for speed.</summary>
    public CompressionLevel Compression { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// When true (default) viewers are told not to scale the page when printing, which keeps
    /// forms and barcodes at their exact physical size.
    /// </summary>
    public bool PrintAtActualSize { get; set; } = true;

    /// <summary>The bookmarks viewers show beside the pages.</summary>
    public PdfOutline Outline { get; } = new();

    /// <summary>The form fields: the imported pages' and the ones added to pages of this document.</summary>
    public PdfForm Form { get; } = new();

    /// <summary>Files embedded in the document.</summary>
    public IReadOnlyList<PdfAttachment> Attachments => _attachments;

    /// <summary>Password protection and permissions; null (default) saves the file unencrypted.</summary>
    public PdfEncryption? Encryption { get; set; }

    /// <summary>Adds a font to the document; only the glyphs used are embedded.</summary>
    public PdfFont AddFont(TrueTypeFont face)
    {
        ArgumentNullException.ThrowIfNull(face);
        var font = new PdfFont(face, "AkF" + (_fonts.Count + 1));
        _fonts.Add(font);
        return font;
    }

    /// <summary>Parses a .ttf or .otf file and adds it. Parse once with <see cref="Fonts.TrueTypeFont.Load"/> to share a font between documents.</summary>
    public PdfFont AddFont(byte[] trueTypeData) => AddFont(TrueTypeFont.Load(trueTypeData));

    /// <summary>Embeds a file in the document. Viewers list it among the document's attachments.</summary>
    public PdfAttachment Attach(string name, byte[] data, string? mimeType = null, string? description = null)
    {
        var a = new PdfAttachment(name, data, mimeType, description);
        _attachments.Add(a);
        return a;
    }

    /// <summary>Resource name of an image in this document; registers the image on first use.</summary>
    internal string ImageName(PdfImage image)
    {
        int index = _images.IndexOf(image);
        if (index < 0) { _images.Add(image); index = _images.Count - 1; }
        return "AkI" + (index + 1);
    }

    /// <summary>Resource name of a graphics state with the given fill and stroke opacity.</summary>
    internal string OpacityName(double opacity)
    {
        int index = _opacities.IndexOf(opacity);
        if (index < 0) { _opacities.Add(opacity); index = _opacities.Count - 1; }
        return "AkG" + (index + 1);
    }

    // ---- Pages -------------------------------------------------------------------------------

    /// <summary>Adds a page of the given size in points.</summary>
    public PdfPage AddPage(double width, double height)
    {
        var page = new PdfPage(this, width, height);
        _pages.Add(page);
        return page;
    }

    /// <summary>Adds a page of the given size, such as <see cref="PageSize.A4"/> or <c>PageSize.Landscape(PageSize.A5)</c>.</summary>
    public PdfPage AddPage((double Width, double Height) size) => AddPage(size.Width, size.Height);

    /// <summary>Adds an A4 page.</summary>
    public PdfPage AddPage() => AddPage(PageSize.A4);

    /// <summary>Inserts a new page at <paramref name="index"/> (zero-based); the pages from there on move one down.</summary>
    public PdfPage InsertPage(int index, (double Width, double Height) size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _pages.Count);
        var page = new PdfPage(this, size.Width, size.Height);
        _pages.Insert(index, page);
        return page;
    }

    /// <summary>Removes the page at <paramref name="index"/>. Links and bookmarks to it are dropped when saving.</summary>
    public void RemovePageAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages.Count);
        _pages.RemoveAt(index);
    }

    /// <summary>Moves a page from one position to another (both zero-based).</summary>
    public void MovePage(int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, _pages.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, _pages.Count);
        PdfPage p = _pages[from];
        _pages.RemoveAt(from);
        _pages.Insert(to, p);
    }

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
        if (_readers.Add(source)) Form.AddRange(source.GetFormFields());
        return page;
    }

    private readonly HashSet<PdfReader> _readers = [];

    /// <summary>
    /// Opens an existing PDF for changes: every page is imported (and can be drawn on), along with the document
    /// information, the language and the form fields. Save writes a new file; the input is not changed.
    /// </summary>
    /// <param name="pdf">The file's bytes.</param>
    /// <param name="password">The user or owner password of an encrypted file.</param>
    public static PdfDocument Open(byte[] pdf, string? password = null)
    {
        PdfReader reader = PdfReader.Open(pdf, password);
        var doc = new PdfDocument { PrintAtActualSize = false, Language = reader.Language };
        PdfInfo info = reader.Info;
        doc.Info.Title = info.Title; doc.Info.Author = info.Author; doc.Info.Subject = info.Subject;
        doc.Info.Keywords = info.Keywords; doc.Info.Creator = info.Creator; doc.Info.CreationDate = info.CreationDate;
        doc.ImportPages(reader);
        return doc;
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

    /// <summary>A new document of the chosen pages (zero-based, in the order given) of <paramref name="pdf"/>.</summary>
    public static PdfDocument ExtractPages(byte[] pdf, params int[] pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        PdfReader reader = PdfReader.Open(pdf);
        var doc = new PdfDocument { PrintAtActualSize = false };
        foreach (int i in pages) doc.ImportPage(reader, i);
        return doc;
    }

    /// <summary>One document per page of <paramref name="pdf"/>.</summary>
    public static IReadOnlyList<PdfDocument> Split(byte[] pdf)
    {
        PdfReader reader = PdfReader.Open(pdf);
        return [.. Enumerable.Range(0, reader.PageCount).Select(i =>
        {
            var doc = new PdfDocument { PrintAtActualSize = false };
            doc.ImportPage(reader, i);
            return doc;
        })];
    }

    // ---- Saving ------------------------------------------------------------------------------

    /// <summary>Writes the document and returns its bytes.</summary>
    public byte[] ToArray()
    {
        using var ms = new MemoryStream(64 * 1024);
        Save(ms);
        return ms.ToArray();
    }

    /// <summary>Writes the document to <paramref name="output"/>, which stays open.</summary>
    public void Save(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_pages.Count == 0) throw new InvalidOperationException("A PDF document needs at least one page.");

        DateTimeOffset created = Info.CreationDate ?? DateTimeOffset.Now;
        string date = FormatDate(created);
        byte[] id16 = SHA256.HashData(Encoding.UTF8.GetBytes($"{Info.Title}|{date}|{_pages.Count}|{ContentFingerprint()}"))[..16];

        var w = new PdfWriter(output) { Compression = Compression };
        int encryptId = 0;
        if (Encryption is not null)
        {
            encryptId = w.Reserve();
            w.Encryptor = Encryption.CreateEncryptor(id16, encryptId);
        }
        int catalogId = w.Reserve(), pagesId = w.Reserve(), infoId = w.Reserve(), resourcesId = w.Reserve();
        var b = new ByteBuf(1024);

        var pageIds = new int[_pages.Count];
        var index = new Dictionary<PdfPage, int>(_pages.Count);
        for (int i = 0; i < _pages.Count; i++) { pageIds[i] = w.Reserve(); index[_pages[i]] = i; }
        var ctx = new SaveContext(w, p => index.TryGetValue(p, out int i) ? pageIds[i] : null);

        PageImporter? importer = null;
        for (int i = 0; i < _pages.Count; i++)
        {
            if (!_pages[i].IsImported) continue;
            importer ??= new PageImporter(w, Compression);
            importer.RegisterPage(_pages[i].Source!, _pages[i].SourcePage!, pageIds[i]);
        }
        if (importer is not null) importer.Overrides = Form.BuildOverrides(ctx);

        // Page content streams: a new page's drawing, an imported page's stamp.
        var contentIds = new int[_pages.Count];
        for (int i = 0; i < _pages.Count; i++)
        {
            if (_pages[i].IsImported && _pages[i].Canvas.IsEmpty) continue;
            contentIds[i] = w.Reserve();
            w.WriteStream(contentIds[i], "", _pages[i].Canvas.Content, compress: Compression != CompressionLevel.NoCompression);
        }

        // Shared resources: only fonts that were actually used get embedded.
        var fonts = new ByteBuf(128);
        foreach (PdfFont font in _fonts)
        {
            if (!font.IsUsed) continue;
            int id = w.Reserve();
            font.Write(w, id);
            fonts.Ascii(" /").Ascii(font.ResourceName).Sp().Int(id).Ascii(" 0 R");
        }
        var xobjects = new ByteBuf(64);
        for (int i = 0; i < _images.Count; i++)
        {
            int id = w.Reserve();
            _images[i].Write(w, id);
            xobjects.Ascii(" /AkI").Int(i + 1).Sp().Int(id).Ascii(" 0 R");
        }
        var states = new ByteBuf(64);
        for (int i = 0; i < _opacities.Count; i++)
            states.Ascii(" /AkG").Int(i + 1).Ascii(" << /ca ").Num(_opacities[i]).Ascii(" /CA ").Num(_opacities[i]).Ascii(" >>");
        var own = new OwnResources(Encoding.ASCII.GetString(fonts.Span), Encoding.ASCII.GetString(xobjects.Span), Encoding.ASCII.GetString(states.Span));

        var resources = new ByteBuf(256);
        resources.Ascii("<< /Font <<").Ascii(own.Fonts).Ascii(" >> /XObject <<").Ascii(own.XObjects).Ascii(" >>");
        if (own.States.Length > 0) resources.Ascii(" /ExtGState <<").Ascii(own.States).Ascii(" >>");
        resources.Ascii(" >>");
        w.WriteObject(resourcesId, resources);

        for (int i = 0; i < _pages.Count; i++)
        {
            PdfPage page = _pages[i];
            var annots = new List<int>();
            foreach (PageAnnotation a in page.Annotations)
            {
                int id = a.Write(ctx, pageIds[i]);
                if (id > 0) annots.Add(id);
            }

            if (page.IsImported)
            {
                importer!.WritePage(page.Source!, page.SourcePage!, pageIds[i], pagesId,
                    new PageExtras(page.Rotation, contentIds[i], own, annots));
                continue;
            }
            b.Reset();
            b.Ascii("<< /Type /Page /Parent ").Int(pagesId).Ascii(" 0 R /MediaBox [0 0 ")
                .Num(page.Width).Sp().Num(page.Height).Ascii("] /Resources ").Int(resourcesId)
                .Ascii(" 0 R /Contents ").Int(contentIds[i]).Ascii(" 0 R");
            if (page.Rotation != 0) b.Ascii(" /Rotate ").Int(page.Rotation);
            if (annots.Count > 0)
            {
                b.Ascii(" /Annots [");
                foreach (int id in annots) b.Int(id).Ascii(" 0 R ");
                b.Ascii("]");
            }
            b.Ascii(" >>");
            w.WriteObject(pageIds[i], b);
        }

        b.Reset();
        b.Ascii("<< /Type /Pages /Count ").Int(_pages.Count).Ascii(" /Kids [");
        foreach (int id in pageIds) b.Int(id).Ascii(" 0 R ");
        b.Ascii("] >>");
        w.WriteObject(pagesId, b);

        int outlineId = WriteOutline(w, ctx);
        int namesId = WriteAttachments(w, created);

        b.Reset();
        b.Ascii("<< /Type /Catalog /Pages ").Int(pagesId).Ascii(" 0 R");
        if (!string.IsNullOrEmpty(Language))
        {
            b.Ascii(" /Lang ");
            PdfSerializer.WriteString(b, PdfSerializer.TextBytes(Language), hex: false);
        }
        if (PrintAtActualSize) b.Ascii(" /ViewerPreferences << /PrintScaling /None >>");
        if (outlineId > 0) b.Ascii(" /Outlines ").Int(outlineId).Ascii(" 0 R /PageMode /UseOutlines");
        if (namesId > 0) b.Ascii(" /Names ").Int(namesId).Ascii(" 0 R");
        WriteAcroForm(b, ctx, importer);
        b.Ascii(" >>");
        w.WriteObject(catalogId, b);

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

        if (Encryption is not null) w.WriteObject(encryptId, w.Encryptor!.Dictionary, encrypt: false);
        w.Finish(catalogId, infoId, id16, encryptId);
    }

    private int WriteOutline(PdfWriter w, SaveContext ctx)
    {
        var items = Outline.Items.Where(i => i.Page is null || ctx.PageId(i.Page) is not null).ToList();
        if (items.Count == 0) return 0;
        int rootId = w.Reserve();
        var (first, last, count) = WriteBookmarks(w, ctx, items, rootId);
        var b = new ByteBuf(64);
        b.Ascii("<< /Type /Outlines /First ").Int(first).Ascii(" 0 R /Last ").Int(last).Ascii(" 0 R /Count ").Int(count).Ascii(" >>");
        w.WriteObject(rootId, b);
        return rootId;
    }

    /// <summary>Writes sibling bookmarks and returns the first and last numbers and how many show when open.</summary>
    private static (int First, int Last, int Visible) WriteBookmarks(PdfWriter w, SaveContext ctx, List<PdfBookmark> items, int parentId)
    {
        var ids = items.Select(_ => w.Reserve()).ToArray();
        int visible = items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            PdfBookmark item = items[i];
            var kids = item.Children.Where(k => k.Page is null || ctx.PageId(k.Page) is not null).ToList();
            var b = new ByteBuf(128);
            b.Ascii("<< /Title ");
            PdfSerializer.WriteString(b, PdfSerializer.TextBytes(item.Title), hex: true);
            b.Ascii(" /Parent ").Int(parentId).Ascii(" 0 R");
            if (i > 0) b.Ascii(" /Prev ").Int(ids[i - 1]).Ascii(" 0 R");
            if (i + 1 < items.Count) b.Ascii(" /Next ").Int(ids[i + 1]).Ascii(" 0 R");
            if (item.Page is not null && ctx.PageId(item.Page) is int pid)
                b.Ascii(" /Dest [").Int(pid).Ascii(" 0 R /XYZ null ").Num(item.Page.OriginY + item.Page.Height - item.Top).Ascii(" null]");
            if (kids.Count > 0)
            {
                var (f, l, c) = WriteBookmarks(w, ctx, kids, ids[i]);
                b.Ascii(" /First ").Int(f).Ascii(" 0 R /Last ").Int(l).Ascii(" 0 R /Count ").Int(item.Open ? c : -c);
                if (item.Open) visible += c;
            }
            b.Ascii(" >>");
            w.WriteObject(ids[i], b);
        }
        return (ids[0], ids[^1], visible);
    }

    private int WriteAttachments(PdfWriter w, DateTimeOffset date)
    {
        if (_attachments.Count == 0) return 0;
        var specs = new List<(string Name, int Id)>();
        foreach (PdfAttachment a in _attachments)
        {
            int fileId = w.Reserve(), specId = w.Reserve();
            string subtype = a.MimeType is null ? "" : " /Subtype /" + EncodeName(a.MimeType);
            w.WriteStream(fileId, $"/Type /EmbeddedFile{subtype} /Params << /Size {a.Data.Length} /ModDate ({FormatDate(date)}) >>", a.Data,
                compress: Compression != CompressionLevel.NoCompression);
            var b = new ByteBuf(128);
            b.Ascii("<< /Type /Filespec /F ");
            PdfSerializer.WriteString(b, Encoding.ASCII.GetBytes(AsciiFileName(a.Name)), hex: false);
            b.Ascii(" /UF ");
            PdfSerializer.WriteString(b, PdfSerializer.TextBytes(a.Name), hex: true);
            if (a.Description is not null)
            {
                b.Ascii(" /Desc ");
                PdfSerializer.WriteString(b, PdfSerializer.TextBytes(a.Description), hex: true);
            }
            b.Ascii(" /EF << /F ").Int(fileId).Ascii(" 0 R /UF ").Int(fileId).Ascii(" 0 R >> /AFRelationship /Unspecified >>");
            w.WriteObject(specId, b);
            specs.Add((a.Name, specId));
        }
        // The name tree's keys must be in byte order; a repeated name gets a numbered key so none is lost.
        var keyed = specs.Select((s, i) => (Key: specs.Take(i).Any(o => o.Name == s.Name) ? $"{s.Name} ({i + 1})" : s.Name, s.Id))
            .OrderBy(s => PdfSerializer.TextBytes(s.Key), ByteOrder.Instance).ToList();
        var n = new ByteBuf(128);
        n.Ascii("<< /EmbeddedFiles << /Names [");
        foreach (var (key, id) in keyed)
        {
            PdfSerializer.WriteString(n, PdfSerializer.TextBytes(key), hex: true);
            n.Sp().Int(id).Ascii(" 0 R ");
        }
        n.Ascii("] >> >>");
        int namesId = w.Reserve();
        w.WriteObject(namesId, n);
        return namesId;
    }

    private void WriteAcroForm(ByteBuf b, SaveContext ctx, PageImporter? importer)
    {
        ctx.WriteRadioGroups();
        var imported = new List<string>();
        string sourceFonts = "";
        if (importer is not null)
        {
            foreach (PdfReader r in _readers)
            {
                if (r.AcroForm is not { } form || !_pages.Any(p => p.Source == r)) continue;
                if (r.Resolve(form.Get("Fields")) is PdfArray fields)
                    foreach (PdfValue f in fields.Items) imported.Add(importer.MapValue(r, f));
                if (sourceFonts.Length == 0 && r.Resolve(form.Get("DR")) is PdfDict dr && r.Resolve(dr.Get("Font")) is PdfDict fd)
                {
                    var fb = new ByteBuf(64);
                    foreach (var e in fd.Entries)
                        if (e.Key is not ("Helv" or "ZaDb")) fb.Ascii(" /").Ascii(e.Key).Sp().Ascii(importer.MapValue(r, e.Value));
                    sourceFonts = Encoding.Latin1.GetString(fb.Span);
                }
            }
        }
        if (ctx.NewFields.Count == 0 && imported.Count == 0) return;
        b.Ascii(" /AcroForm << /Fields [");
        foreach (int id in ctx.NewFields) b.Int(id).Ascii(" 0 R ");
        foreach (string f in imported) b.Ascii(f).Sp();
        b.Ascii("] /DR << /Font << /Helv ").Int(ctx.Helvetica).Ascii(" 0 R /ZaDb ").Int(ctx.ZapfDingbats).Ascii(" 0 R").Ascii(sourceFonts)
            .Ascii(" >> >> /DA (/Helv 0 Tf 0 g) >>");
    }

    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();
        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }

    private static string AsciiFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(c is >= ' ' and <= '~' ? c : '_');
        return sb.ToString();
    }

    internal static string EncodeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (byte c in Encoding.UTF8.GetBytes(name))
        {
            if (c is > 0x20 and < 0x7F && !PdfParser.IsDelimiter(c) && c != '#') sb.Append((char)c);
            else sb.Append('#').Append(c.ToString("X2"));
        }
        return sb.ToString();
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

    internal static string FormatDate(DateTimeOffset d)
    {
        TimeSpan o = d.Offset;
        char sign = o < TimeSpan.Zero ? '-' : '+';
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"D:{d:yyyyMMddHHmmss}{sign}{Math.Abs(o.Hours):00}'{Math.Abs(o.Minutes):00}'");
    }
}

/// <summary>This document's resource entries (the bodies of /Font, /XObject and /ExtGState), for pages that merge them with their own.</summary>
internal sealed record OwnResources(string Fonts, string XObjects, string States);

/// <summary>What an imported page gets on top of its copy: its rotation, a stamp, and annotations made here.</summary>
internal sealed record PageExtras(int Rotation, int OverlayContentId, OwnResources Resources, List<int> Annotations);
