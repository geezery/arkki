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
