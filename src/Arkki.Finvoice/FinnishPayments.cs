using System.Numerics;
using System.Text;

namespace Arkki.Finvoice;

/// <summary>IBAN validation and formatting (ISO 13616, modulo 97).</summary>
public static class Iban
{
    public static string Normalize(string iban)
    {
        var sb = new StringBuilder(iban.Length);
        foreach (char c in iban)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    public static bool IsValid(string iban)
    {
        string s = Normalize(iban);
        if (s.Length < 15 || s.Length > 34) return false;
        if (s.StartsWith("FI", StringComparison.Ordinal) && s.Length != 18) return false;
        return Mod97(s[4..] + s[..4]) == 1;
    }

    /// <summary>Groups of four from the left, as printed on the credit transfer form: FI21 1234 5600 0007 85.</summary>
    public static string Format(string iban)
    {
        string s = Normalize(iban);
        var sb = new StringBuilder(s.Length + s.Length / 4);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    internal static int Mod97(string alphanumeric)
    {
        int rem = 0;
        foreach (char c in alphanumeric)
        {
            if (c is >= '0' and <= '9') rem = (rem * 10 + (c - '0')) % 97;
            else if (c is >= 'A' and <= 'Z') rem = (rem * 100 + (c - 'A' + 10)) % 97;
            else return -1;
        }
        return rem;
    }
}

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

/// <summary>
/// Finnish bank barcode (pankkiviivakoodi) data per Finance Finland's Bank Bar Code Guide 5.3:
/// 54 digits, version 4 for a national reference and version 5 for an RF reference.
/// The same digit string is the "virtual barcode" that payers paste into a web bank.
/// </summary>
public static class BankBarcode
{
    public const decimal MaxAmount = 999_999.99m;

    /// <summary>
    /// Builds the 54-digit data string, or returns null when the data cannot be encoded
    /// (non-Finnish IBAN, or a reference that is missing, invalid or not purely numeric).
    /// </summary>
    public static string? TryBuild(string iban, decimal? amount, string? reference, DateOnly? dueDate)
    {
        string account = Iban.Normalize(iban);
        if (account.Length != 18 || !account.StartsWith("FI", StringComparison.Ordinal) || !account.AsSpan(2).ToString().All(char.IsAsciiDigit)) return null;
        if (string.IsNullOrWhiteSpace(reference)) return null;

        string r = PaymentReference.Normalize(reference);
        var sb = new StringBuilder(54);
        bool rf = r.StartsWith("RF", StringComparison.Ordinal);
        sb.Append(rf ? '5' : '4').Append(account, 2, 16);

        // Amounts that do not fit are encoded as zeros; the payer then enters the amount.
        long cents = amount is { } a && a >= 0 && a <= MaxAmount ? (long)decimal.Round(a * 100m, 0, MidpointRounding.AwayFromZero) : 0;
        sb.Append((cents / 100).ToString("000000")).Append((cents % 100).ToString("00"));

        if (rf)
        {
            string body = r[2..];
            if (body.Length is < 3 or > 23 || !body.All(char.IsAsciiDigit)) return null;
            sb.Append(body, 0, 2).Append(body[2..].PadLeft(21, '0'));
        }
        else
        {
            if (r.Length > 20 || !r.All(char.IsAsciiDigit)) return null;
            sb.Append("000").Append(r.PadLeft(20, '0'));
        }

        sb.Append(dueDate is { } d ? d.ToString("yyMMdd") : "000000");
        return sb.Length == 54 ? sb.ToString() : null;
    }
}

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
