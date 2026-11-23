namespace Arkki.Pdf.Layout;

/// <summary>
/// Keeps its child on the same page as the start of the element that follows it in a
/// <see cref="Column"/>, so a heading never ends a page alone. Elsewhere it lays out as its child.
/// </summary>
public sealed class KeepWithNext(Element child) : Element
{
    public Element Child { get; } = child;

    public override LayoutResult Layout(double width, double height) => Child.Layout(width, height);
}
