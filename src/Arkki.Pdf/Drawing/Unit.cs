namespace Arkki.Pdf;

/// <summary>Unit conversions. All Arkki coordinates are in PDF points (1/72 inch).</summary>
public static class Unit
{
    /// <summary>Points in a millimetre (72 points to the inch).</summary>
    public const double PointsPerMm = 72.0 / 25.4;
    /// <summary>Millimetres to points.</summary>
    public static double Mm(double mm) => mm * PointsPerMm;
    /// <summary>Inches to points.</summary>
    public static double Inch(double inches) => inches * 72.0;
}
