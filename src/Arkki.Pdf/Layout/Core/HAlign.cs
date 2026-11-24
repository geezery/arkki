namespace Arkki.Pdf.Layout;

/// <summary>
/// Horizontal alignment. <see cref="Justify"/> applies to text: every wrapped line is stretched to the full width
/// by widening its spaces, except the last line of a paragraph; boxes treat it as <see cref="Left"/>.
/// </summary>
public enum HAlign
{
    /// <summary>Flush left.</summary>
    Left,
    /// <summary>Centred.</summary>
    Center,
    /// <summary>Flush right.</summary>
    Right,
    /// <summary>Both edges flush; the last line of a paragraph flush left.</summary>
    Justify,
}
