namespace Arkki.Pdf.Layout;

/// <summary>Keeps its child on one page: it either fits completely or moves to the next page.</summary>
public sealed class KeepTogether(Element child) : Element
{
    /// <inheritdoc />
    public override LayoutResult Layout(double width, double height)
    {
        LayoutResult r = child.Layout(width, height);
        return r.Status == LayoutStatus.Full ? r : LayoutResult.None(this);
    }
}
