namespace Arkki.Pdf;

public sealed class PdfInfo
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public string? Keywords { get; set; }
    public string? Creator { get; set; }

    /// <summary>Set to a fixed value to get byte-identical output for identical input.</summary>
    public DateTimeOffset? CreationDate { get; set; }
}
