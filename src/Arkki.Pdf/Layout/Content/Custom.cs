namespace Arkki.Pdf.Layout;

/// <summary>Escape hatch: a fixed-height area drawn by a callback with direct canvas access.</summary>
public sealed class Custom(double fixedHeight, Action<PdfCanvas, double, double, double> draw) : Element
{
    public override LayoutResult Layout(double width, double height) =>
        fixedHeight > height + 0.001 ? LayoutResult.None(this) : LayoutResult.Full(new CustomFragment(draw, width, fixedHeight));

    private sealed class CustomFragment : Fragment
    {
        private readonly Action<PdfCanvas, double, double, double> _draw;

        public CustomFragment(Action<PdfCanvas, double, double, double> draw, double width, double height)
        {
            _draw = draw; Width = width; Height = height;
        }

        public override void Draw(PdfCanvas canvas, double x, double y) => _draw(canvas, x, y, Width);
    }
}
