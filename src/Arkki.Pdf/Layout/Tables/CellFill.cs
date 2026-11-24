namespace Arkki.Pdf.Layout;

/// <summary>
/// A table cell with its own background and border, painted over the cell's full width and the row's full
/// height, so the cells of one row line up as equal boxes whatever their content. Create one with
/// <see cref="ElementExtensions.CellFill"/>; outside a table it lays out as its content.
/// </summary>
public sealed class CellFill : Element
{
    /// <summary>A cell with its own fill and border.</summary>
    public CellFill(Element content, Color? background, Color? border = null, double borderWidth = 0.5)
    {
        Content = content;
        Background = background;
        Border = border;
        BorderWidth = borderWidth;
    }

    /// <summary>The cell's content.</summary>
    public Element Content { get; }
    /// <summary>The cell's fill; null for none.</summary>
    public Color? Background { get; }
    /// <summary>The cell's border colour; null for none.</summary>
    public Color? Border { get; }
    /// <summary>The border width in points.</summary>
    public double BorderWidth { get; }

    /// <inheritdoc />
    public override LayoutResult Layout(double width, double height) => Content.Layout(width, height);
}
