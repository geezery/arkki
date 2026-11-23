using System.Text;

namespace Arkki.Samples.Finvoice;

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
