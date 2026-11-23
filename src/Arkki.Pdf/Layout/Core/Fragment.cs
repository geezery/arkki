namespace Arkki.Pdf.Layout;

/// <summary>A laid-out, positioned-on-demand piece of an element: knows its size and how to draw itself.</summary>
public abstract class Fragment
{
    public double Width { get; protected init; }
    public double Height { get; protected init; }
    public abstract void Draw(PdfCanvas canvas, double x, double y);
}
