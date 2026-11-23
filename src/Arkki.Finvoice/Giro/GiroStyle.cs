using Arkki.Pdf;
using Arkki.Pdf.Layout;

namespace Arkki.Finvoice;

public sealed class GiroStyle
{
    public required FontFamily Fonts { get; init; }

    /// <summary>Colour of the pre-printed parts (lines and guide texts). The guide allows blue or black.</summary>
    public Color FormColor { get; init; } = Color.Black;

    /// <summary>Barcode length in mm. The guide allows 70–105 mm and prefers the long end.</summary>
    public double BarcodeWidthMm { get; init; } = 104;

    /// <summary>Barcode height in mm. The guide allows 10.0–12.7 mm.</summary>
    public double BarcodeHeightMm { get; init; } = 12;
}
