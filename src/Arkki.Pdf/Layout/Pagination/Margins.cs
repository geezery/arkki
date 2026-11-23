namespace Arkki.Pdf.Layout;

public readonly record struct Margins(double Left, double Top, double Right, double Bottom)
{
    public static Margins Mm(double left, double top, double right, double bottom) =>
        new(Unit.Mm(left), Unit.Mm(top), Unit.Mm(right), Unit.Mm(bottom));
}
