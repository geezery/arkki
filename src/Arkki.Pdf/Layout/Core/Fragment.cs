namespace Arkki.Pdf.Layout;

/// <summary>A laid-out, positioned-on-demand piece of an element: knows its size and how to draw itself.</summary>
public abstract class Fragment
{
    /// <summary>Width in points.</summary>
    public double Width { get; protected init; }
    /// <summary>Height in points.</summary>
    public double Height { get; protected init; }
    /// <summary>Draws the fragment with its top-left corner at (x, y).</summary>
    public abstract void Draw(PdfCanvas canvas, double x, double y);
}
