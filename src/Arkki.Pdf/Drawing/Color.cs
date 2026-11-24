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
        ArgumentNullException.ThrowIfNull(hex);
        ReadOnlySpan<char> h = hex.AsSpan().Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.AllowHexSpecifier, null, out int v))
            throw new FormatException($"'{hex}' is not a color in the form #RRGGBB.");
        return new(((v >> 16) & 255) / 255.0, ((v >> 8) & 255) / 255.0, (v & 255) / 255.0);
    }
}
