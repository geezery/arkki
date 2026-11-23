namespace Arkki.Pdf;

public sealed class PdfPage
{
    internal PdfPage(PdfDocument document, double width, double height)
    {
        Width = width; Height = height;
        Canvas = new PdfCanvas(document, height);
    }

    public double Width { get; }
    public double Height { get; }
    public PdfCanvas Canvas { get; }
}
