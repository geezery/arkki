using System.Text;

namespace Arkki.Samples.Finvoice;

/// <summary>Finnish number and date formats, independent of the machine's culture settings.</summary>
public static class FiFormat
{
    private const char Nbsp = ' ';

    /// <summary>1 234,56 with a non-breaking space as the thousands separator.</summary>
    public static string Money(decimal value) => Number(value, 2, 2);

    public static string Number(decimal value, int minDecimals, int maxDecimals)
    {
        decimal rounded = decimal.Round(value, maxDecimals, MidpointRounding.AwayFromZero);
        string s = Math.Abs(rounded).ToString("F" + maxDecimals, System.Globalization.CultureInfo.InvariantCulture);
        int dot = s.IndexOf('.');
        string whole = dot < 0 ? s : s[..dot];
        string fraction = dot < 0 ? "" : s[(dot + 1)..];
        while (fraction.Length > minDecimals && fraction.EndsWith('0')) fraction = fraction[..^1];

        var sb = new StringBuilder(s.Length + 6);
        if (rounded < 0) sb.Append('-');
        for (int i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) sb.Append(Nbsp);
            sb.Append(whole[i]);
        }
        if (fraction.Length > 0) sb.Append(',').Append(fraction);
        return sb.ToString();
    }

    /// <summary>Day, month and full year without leading zeros: 1.2.2026.</summary>
    public static string Date(DateOnly date) => $"{date.Day}.{date.Month}.{date.Year}";
}
