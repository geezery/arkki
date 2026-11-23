namespace Arkki.Pdf.Layout;

public readonly record struct LayoutResult(LayoutStatus Status, Fragment? Fragment, Element? Remainder)
{
    public static LayoutResult None(Element self) => new(LayoutStatus.None, null, self);
    public static LayoutResult Full(Fragment fragment) => new(LayoutStatus.Full, fragment, null);
}
