namespace Arkki.Pdf.Barcodes;

/// <summary>
/// EAN-13 encoder (ISO/IEC 15420). Takes 12 digits, or 13 with a check digit that must be right.
/// The digits under the bars are not drawn; set them as text where the layout wants them.
/// </summary>
public sealed class Ean13 : IBarcodeSymbology
{
    private static readonly string[] Left = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    // Which of the six left digits use the even-parity (G) set, chosen by the first digit.
    private static readonly string[] Parity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    /// <inheritdoc />
    public BarcodeMatrix Encode(string text)
    {
        string digits = WithCheckDigit(text);
        var bits = new System.Text.StringBuilder(95);
        bits.Append("101");
        for (int i = 1; i <= 6; i++)
        {
            string l = Left[digits[i] - '0'];
            bits.Append(Parity[digits[0] - '0'][i - 1] == 'L' ? l : Reverse(Invert(l)));
        }
        bits.Append("01010");
        for (int i = 7; i <= 12; i++) bits.Append(Invert(Left[digits[i] - '0']));
        bits.Append("101");

        var m = new BarcodeMatrix(bits.Length, 1, quietZone: 11);
        for (int x = 0; x < bits.Length; x++) m[x, 0] = bits[x] == '1';
        return m;
    }

    /// <summary>Returns the 13 digits, computing the check digit when 12 are given.</summary>
    public static string WithCheckDigit(string text)
    {
        if (text.Length is not (12 or 13) || !text.All(char.IsAsciiDigit))
            throw new ArgumentException("EAN-13 needs 12 or 13 digits.", nameof(text));
        int sum = 0;
        for (int i = 0; i < 12; i++) sum += (text[i] - '0') * (i % 2 == 0 ? 1 : 3);
        char check = (char)('0' + (10 - sum % 10) % 10);
        if (text.Length == 13 && text[12] != check) throw new ArgumentException($"EAN-13 check digit should be {check}.", nameof(text));
        return text[..12] + check;
    }

    private static string Invert(string s) => string.Create(s.Length, s, (span, src) =>
    {
        for (int i = 0; i < src.Length; i++) span[i] = src[i] == '1' ? '0' : '1';
    });

    private static string Reverse(string s) => new([.. s.Reverse()]);
}
