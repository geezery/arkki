namespace Arkki.Pdf.Layout;

/// <summary>The outcome of laying out an element in the space offered.</summary>
/// <param name="Status">Whether all, part or none of the element fit.</param>
/// <param name="Fragment">What fit, ready to draw; null when nothing did.</param>
/// <param name="Remainder">What did not fit, to lay out on the next page; null when everything fit.</param>
public readonly record struct LayoutResult(LayoutStatus Status, Fragment? Fragment, Element? Remainder)
{
    /// <summary>Nothing fits: the whole element waits for the next page.</summary>
    public static LayoutResult None(Element self) => new(LayoutStatus.None, null, self);
    /// <summary>Everything fits in this fragment.</summary>
    public static LayoutResult Full(Fragment fragment) => new(LayoutStatus.Full, fragment, null);
}
