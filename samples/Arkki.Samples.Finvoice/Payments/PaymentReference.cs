using System.Text;

namespace Arkki.Samples.Finvoice;

/// <summary>Finnish national reference number (viitenumero) and international RF creditor reference (ISO 11649).</summary>
public static class PaymentReference
{
    public static string Normalize(string reference)
    {
        var sb = new StringBuilder(reference.Length);
        foreach (char c in reference)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    public static bool IsRf(string reference) => Normalize(reference).StartsWith("RF", StringComparison.Ordinal);

    /// <summary>Check digit for a national reference base, using the 7-3-1 weights from the right.</summary>
    public static int NationalCheckDigit(ReadOnlySpan<char> baseDigits)
    {
        ReadOnlySpan<int> weights = [7, 3, 1];
        int sum = 0, k = 0;
        for (int i = baseDigits.Length - 1; i >= 0; i--, k++) sum += (baseDigits[i] - '0') * weights[k % 3];
        return (10 - sum % 10) % 10;
    }

    /// <summary>Appends the check digit to a 3..19 digit base.</summary>
    public static string CreateNational(string baseDigits)
    {
        if (baseDigits.Length is < 3 or > 19 || !baseDigits.All(char.IsAsciiDigit))
            throw new ArgumentException("A national reference base has 3 to 19 digits.", nameof(baseDigits));
        return baseDigits + NationalCheckDigit(baseDigits);
    }

    public static bool IsValidNational(string reference)
    {
        string s = Normalize(reference).TrimStart('0');
        if (s.Length is < 4 or > 20 || !s.All(char.IsAsciiDigit)) return false;
        return NationalCheckDigit(s.AsSpan(0, s.Length - 1)) == s[^1] - '0';
    }

    public static bool IsValidRf(string reference)
    {
        string s = Normalize(reference);
        if (s.Length is < 5 or > 25 || !s.StartsWith("RF", StringComparison.Ordinal)) return false;
        return Iban.Mod97(s[4..] + s[..4]) == 1;
    }

    public static bool IsValid(string reference) => IsRf(reference) ? IsValidRf(reference) : IsValidNational(reference);

    /// <summary>Converts a national reference to an RF reference.</summary>
    public static string ToRf(string national)
    {
        string s = Normalize(national);
        int check = 98 - Iban.Mod97(s + "RF00");
        return $"RF{check:00}{s}";
    }

    /// <summary>
    /// Print format from the credit transfer guide: national references in groups of five from
    /// the right without leading zeros, RF references in groups of four from the left.
    /// </summary>
    public static string Format(string reference)
    {
        string s = Normalize(reference);
        var sb = new StringBuilder(s.Length + 6);
        if (s.StartsWith("RF", StringComparison.Ordinal))
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
        s = s.TrimStart('0');
        int first = s.Length % 5;
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && (i - first) % 5 == 0) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
