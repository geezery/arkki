namespace Arkki.Pdf.Layout;

/// <summary>Ends the current page: whatever follows starts on a new page.</summary>
public sealed class PageBreak : Element
{
    public override LayoutResult Layout(double width, double height) =>
        new(LayoutStatus.Partial, new BreakFragment(width), null);

    private sealed class BreakFragment : Fragment
    {
        public BreakFragment(double width) => Width = width;
        public override void Draw(PdfCanvas canvas, double x, double y) { }
    }
}
