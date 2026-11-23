namespace Arkki.Pdf.Layout;

/// <summary>
/// A table cell with its own background and border, painted over the cell's full width and the row's full
/// height, so the cells of one row line up as equal boxes whatever their content. Create one with
/// <see cref="ElementExtensions.CellFill"/>; outside a table it lays out as its content.
/// </summary>
public sealed class CellFill : Element
{
    public CellFill(Element content, Color? background, Color? border = null, double borderWidth = 0.5)
    {
        Content = content;
        Background = background;
        Border = border;
        BorderWidth = borderWidth;
    }

    public Element Content { get; }
    public Color? Background { get; }
    public Color? Border { get; }
    public double BorderWidth { get; }

    public override LayoutResult Layout(double width, double height) => Content.Layout(width, height);
}
