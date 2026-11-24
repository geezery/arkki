namespace Arkki.Pdf.Layout;

/// <summary>Vertical white space. Collapses at the bottom of a page instead of forcing a break.</summary>
public sealed class Spacer(double height) : Element
{
    /// <inheritdoc />
    public override LayoutResult Layout(double width, double available) =>
        LayoutResult.Full(new EmptyFragment(width, Math.Max(0, Math.Min(height, available))));

    private sealed class EmptyFragment : Fragment
    {
        public EmptyFragment(double width, double height) { Width = width; Height = height; }
        public override void Draw(PdfCanvas canvas, double x, double y) { }
    }
}
