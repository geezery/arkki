namespace Arkki.Pdf.Layout;

/// <summary>Space between the page edges and the content, in points.</summary>
/// <param name="Left">Left margin.</param>
/// <param name="Top">Top margin; the header sits inside it.</param>
/// <param name="Right">Right margin.</param>
/// <param name="Bottom">Bottom margin; the footer sits inside it.</param>
public readonly record struct Margins(double Left, double Top, double Right, double Bottom)
{
    /// <summary>Margins given in millimetres.</summary>
    public static Margins Mm(double left, double top, double right, double bottom) =>
        new(Unit.Mm(left), Unit.Mm(top), Unit.Mm(right), Unit.Mm(bottom));
}
