namespace Arkki.Pdf;

/// <summary>The document's metadata (the information dictionary).</summary>
public sealed class PdfInfo
{
    /// <summary>The document's title; viewers show it instead of the file name.</summary>
    public string? Title { get; set; }
    /// <summary>Who wrote the document.</summary>
    public string? Author { get; set; }
    /// <summary>What the document is about.</summary>
    public string? Subject { get; set; }
    /// <summary>Search keywords.</summary>
    public string? Keywords { get; set; }
    /// <summary>The application that created the content (Arkki itself is the producer).</summary>
    public string? Creator { get; set; }

    /// <summary>The program that wrote the file. Read from a file; a saved document always names Arkki.</summary>
    public string? Producer { get; set; }

    /// <summary>When the file was last changed. Read from a file; a saved document uses its creation date.</summary>
    public DateTimeOffset? ModificationDate { get; set; }

    /// <summary>Set to a fixed value to get byte-identical output for identical input.</summary>
    public DateTimeOffset? CreationDate { get; set; }
}
