namespace Arkki.Pdf;

/// <summary>Unit conversions. All Arkki coordinates are in PDF points (1/72 inch).</summary>
public static class Unit
{
    public const double PointsPerMm = 72.0 / 25.4;
    public static double Mm(double mm) => mm * PointsPerMm;
    public static double Inch(double inches) => inches * 72.0;
}
