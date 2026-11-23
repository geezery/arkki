namespace Arkki.Pdf;

/// <summary>
/// Common paper sizes in points, portrait (width less than height). <see cref="Landscape"/> turns one on its side.
/// ISO 216 A and B series, ISO 269 envelopes, and the North American sizes.
/// </summary>
public static class PageSize
{
    public static readonly (double Width, double Height) A0 = Mm(841, 1189);
    public static readonly (double Width, double Height) A1 = Mm(594, 841);
    public static readonly (double Width, double Height) A2 = Mm(420, 594);
    public static readonly (double Width, double Height) A3 = Mm(297, 420);
    public static readonly (double Width, double Height) A4 = Mm(210, 297);
    public static readonly (double Width, double Height) A5 = Mm(148, 210);
    public static readonly (double Width, double Height) A6 = Mm(105, 148);
    public static readonly (double Width, double Height) A7 = Mm(74, 105);
    public static readonly (double Width, double Height) B4 = Mm(250, 353);
    public static readonly (double Width, double Height) B5 = Mm(176, 250);

    /// <summary>Envelope for an unfolded A4 sheet.</summary>
    public static readonly (double Width, double Height) C4 = Mm(229, 324);
    /// <summary>Envelope for an A4 sheet folded once.</summary>
    public static readonly (double Width, double Height) C5 = Mm(162, 229);
    /// <summary>Envelope for an A4 sheet folded twice.</summary>
    public static readonly (double Width, double Height) C6 = Mm(114, 162);
    /// <summary>The DL envelope, for an A4 sheet folded in three.</summary>
    public static readonly (double Width, double Height) DL = Mm(110, 220);

    public static readonly (double Width, double Height) Letter = (612, 792);
    public static readonly (double Width, double Height) Legal = (612, 1008);
    public static readonly (double Width, double Height) Tabloid = (792, 1224);
    public static readonly (double Width, double Height) Executive = (522, 756);

    /// <summary>The size with its longer side horizontal.</summary>
    public static (double Width, double Height) Landscape((double Width, double Height) size) =>
        size.Width >= size.Height ? size : (size.Height, size.Width);

    /// <summary>The size with its longer side vertical.</summary>
    public static (double Width, double Height) Portrait((double Width, double Height) size) =>
        size.Width <= size.Height ? size : (size.Height, size.Width);

    /// <summary>A custom size in millimetres.</summary>
    public static (double Width, double Height) Mm(double width, double height) => (Unit.Mm(width), Unit.Mm(height));
}
