namespace Arkki.Pdf.Layout;

/// <summary>The faces of one typeface. A missing italic face falls back to the upright one.</summary>
public sealed record FontFamily(PdfFont Regular, PdfFont Bold, PdfFont? Italic = null, PdfFont? BoldItalic = null)
{
    public PdfFont Select(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => BoldItalic ?? Bold,
        (false, true) => Italic ?? Regular,
        (true, false) => Bold,
        _ => Regular,
    };
}
