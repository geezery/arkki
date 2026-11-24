namespace Arkki.Pdf;

/// <summary>A bookmark of the document outline: a title that opens a page, with bookmarks of its own under it.</summary>
public sealed class PdfBookmark
{
    private readonly List<PdfBookmark> _children = [];

    internal PdfBookmark(string title, PdfPage? page, double top)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title; Page = page; Top = top;
    }

    /// <summary>The text shown in the viewer's bookmark panel.</summary>
    public string Title { get; }
    /// <summary>The page the bookmark opens; null for a heading that only groups others.</summary>
    public PdfPage? Page { get; }

    /// <summary>Points from the top of the page that the bookmark scrolls to.</summary>
    public double Top { get; }

    /// <summary>True to show the bookmark's children when the document opens.</summary>
    public bool Open { get; set; }

    /// <summary>Nested bookmarks.</summary>
    public IReadOnlyList<PdfBookmark> Children => _children;

    /// <summary>Adds a bookmark under this one.</summary>
    public PdfBookmark Add(string title, PdfPage page, double top = 0)
    {
        ArgumentNullException.ThrowIfNull(page);
        var b = new PdfBookmark(title, page, top);
        _children.Add(b);
        return b;
    }
}

/// <summary>The outline (bookmarks) viewers show beside the pages.</summary>
public sealed class PdfOutline
{
    private readonly List<PdfBookmark> _items = [];

    /// <summary>The top-level bookmarks.</summary>
    public IReadOnlyList<PdfBookmark> Items => _items;

    /// <summary>Adds a top-level bookmark to <paramref name="page"/>, <paramref name="top"/> points from its top.</summary>
    public PdfBookmark Add(string title, PdfPage page, double top = 0)
    {
        ArgumentNullException.ThrowIfNull(page);
        var b = new PdfBookmark(title, page, top);
        _items.Add(b);
        return b;
    }
}

/// <summary>A file embedded in the document, listed by viewers among its attachments.</summary>
public sealed class PdfAttachment
{
    internal PdfAttachment(string name, byte[] data, string? mimeType, string? description)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(data);
        Name = name; Data = data; MimeType = mimeType; Description = description;
    }

    /// <summary>The file name shown in the viewer.</summary>
    public string Name { get; }
    /// <summary>The file's contents.</summary>
    public byte[] Data { get; }
    /// <summary>The MIME type, such as application/xml.</summary>
    public string? MimeType { get; }
    /// <summary>A description shown with the attachment.</summary>
    public string? Description { get; }
}
