namespace Arkki.Pdf;

/// <summary>An RGB color with components in the 0..1 range.</summary>
public readonly record struct Color(double R, double G, double B)
{
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color White = new(1, 1, 1);
    public static Color Gray(double level) => new(level, level, level);

    /// <summary>Parses "#RRGGBB" or "RRGGBB".</summary>
    public static Color FromHex(string hex)
    {
        ReadOnlySpan<char> h = hex.AsSpan().TrimStart('#');
        int v = int.Parse(h, System.Globalization.NumberStyles.HexNumber);
        return new(((v >> 16) & 255) / 255.0, ((v >> 8) & 255) / 255.0, (v & 255) / 255.0);
    }
}
