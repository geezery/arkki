namespace Arkki.Pdf.Barcodes;

/// <summary>
/// Code 128 encoder (ISO/IEC 15417). Produces bar/space module widths that
/// <see cref="PdfCanvas.Bars"/> draws as vector rectangles, so the symbol is resolution independent.
/// </summary>
public static class Code128
{
    public const int StartB = 104, StartC = 105, Stop = 106;

    // Bar/space widths for symbol values 0..106; each is 11 modules except the stop pattern (13).
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    /// <summary>
    /// Symbol values for an even-length digit string in character set C:
    /// start, one value per digit pair, modulo 103 check character, stop.
    /// </summary>
    public static int[] EncodeSetC(ReadOnlySpan<char> digits)
    {
        if (digits.Length == 0 || (digits.Length & 1) != 0) throw new ArgumentException("Character set C needs an even number of digits.", nameof(digits));
        var values = new int[digits.Length / 2 + 3];
        values[0] = StartC;
        for (int i = 0; i < digits.Length; i += 2)
        {
            int hi = digits[i] - '0', lo = digits[i + 1] - '0';
            if ((uint)hi > 9 || (uint)lo > 9) throw new ArgumentException("Character set C accepts digits only.", nameof(digits));
            values[i / 2 + 1] = hi * 10 + lo;
        }
        values[^2] = CheckValue(values.AsSpan(0, values.Length - 2));
        values[^1] = Stop;
        return values;
    }

    /// <summary>Modulo 103 check value over the start character and data values.</summary>
    public static int CheckValue(ReadOnlySpan<int> startAndData)
    {
        long sum = startAndData[0];
        for (int i = 1; i < startAndData.Length; i++) sum += (long)startAndData[i] * i;
        return (int)(sum % 103);
    }

    /// <summary>Alternating bar/space widths in modules, starting with a bar.</summary>
    public static byte[] ToModules(ReadOnlySpan<int> values)
    {
        int n = 0;
        foreach (int v in values) n += Patterns[v].Length;
        var modules = new byte[n];
        int k = 0;
        foreach (int v in values)
            foreach (char c in Patterns[v]) modules[k++] = (byte)(c - '0');
        return modules;
    }

    public static int TotalModules(ReadOnlySpan<byte> modules)
    {
        int total = 0;
        foreach (byte m in modules) total += m;
        return total;
    }
}
