namespace Arkki.Pdf.Layout;

public sealed record TextStyle(FontFamily Family, double Size = 9, bool Bold = false, Color? Ink = null, double LineHeight = 1.25)
{
    public PdfFont Font => Bold ? Family.Bold : Family.Regular;
    public Color InkColor => Ink ?? Color.Black;
    public double LineAdvance => Size * LineHeight;
}
