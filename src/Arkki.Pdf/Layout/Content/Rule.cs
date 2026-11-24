namespace Arkki.Pdf.Layout;

/// <summary>A horizontal rule across the available width.</summary>
public sealed class Rule(Color color, double thickness = 0.5) : Element
{
    /// <inheritdoc />
    public override LayoutResult Layout(double width, double height) =>
        thickness > height ? LayoutResult.None(this) : LayoutResult.Full(new RuleFragment(color, thickness, width));

    private sealed class RuleFragment : Fragment
    {
        private readonly Color _color;

        public RuleFragment(Color color, double thickness, double width) { _color = color; Width = width; Height = thickness; }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            canvas.SetStrokeColor(_color);
            canvas.SetLineWidth(Height);
            canvas.Line(x, y + Height / 2, x + Width, y + Height / 2);
        }
    }
}
