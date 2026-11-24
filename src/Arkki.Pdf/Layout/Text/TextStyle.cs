namespace Arkki.Pdf.Layout;

/// <summary>How text looks: the font family, size in points, weight, slant, colour and line height (a multiple of the size).</summary>
public sealed record TextStyle(FontFamily Family, double Size = 9, bool Bold = false, Color? Ink = null, double LineHeight = 1.25, bool Italic = false)
{
    private readonly FontFamily _family = Family ?? throw new ArgumentNullException(nameof(Family));
    private readonly double _size = CheckSize(Size);
    private readonly double _lineHeight = CheckLineHeight(LineHeight);

    public FontFamily Family { get => _family; init => _family = value ?? throw new ArgumentNullException(nameof(Family)); }
    public double Size { get => _size; init => _size = CheckSize(value); }
    public double LineHeight { get => _lineHeight; init => _lineHeight = CheckLineHeight(value); }

    private static double CheckSize(double v) =>
        double.IsFinite(v) && v > 0 && v <= 10000 ? v : throw new ArgumentOutOfRangeException(nameof(Size), v, "The font size must be a positive number of points.");

    private static double CheckLineHeight(double v) =>
        double.IsFinite(v) && v > 0 && v <= 100 ? v : throw new ArgumentOutOfRangeException(nameof(LineHeight), v, "The line height must be a positive multiple of the size.");

    public PdfFont Font => Family.Select(Bold, Italic);
    public Color InkColor => Ink ?? Color.Black;
    public double LineAdvance => Size * LineHeight;
}
