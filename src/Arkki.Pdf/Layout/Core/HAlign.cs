namespace Arkki.Pdf.Layout;

/// <summary>
/// Horizontal alignment. <see cref="Justify"/> applies to text: every wrapped line is stretched to the full width
/// by widening its spaces, except the last line of a paragraph; boxes treat it as <see cref="Left"/>.
/// </summary>
public enum HAlign { Left, Center, Right, Justify }
