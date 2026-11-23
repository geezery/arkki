using Arkki.Finvoice;
using Arkki.Pdf.Barcodes;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Every worked example of Finance Finland's Bank Bar Code Guide 5.3.</summary>
internal static class BankBarcodeTests
{
    public static void Run()
    {
        (string Iban, decimal Amount, string Reference, string? Due, string Data, int Check)[] vectors =
        [
            ("FI79 4405 2020 0360 82", 4883.15m, "86851 62596 19897", "2010-06-12", "479440520200360820048831500000000868516259619897100612", 40),
            ("FI58 1017 1000 0001 22", 482.99m, "55958 22432 94671", "2012-01-31", "458101710000001220004829900000000559582243294671120131", 55),
            ("FI02 5000 4640 0013 02", 693.80m, "69 87567 20834 35364", "2011-07-24", "402500046400013020006938000000069875672083435364110724", 14),
            ("FI15 6601 0001 5306 41", 7444.54m, "7 75847 47906 47489", "2019-12-19", "415660100015306410074445400000007758474790647489191219", 63),
            ("FI16 8000 1400 0502 67", 935.85m, "78 77767 96566 28687", null, "416800014000502670009358500000078777679656628687000000", 30),
            ("FI73 3131 3001 0000 58", 0.00m, "8 68624", "2013-08-09", "473313130010000580000000000000000000000000868624130809", 68),
            ("FI83 3301 0001 1007 75", 150000.20m, "92125 37425 25398 97737", "2016-05-25", "483330100011007751500002000092125374252539897737160525", 45),
            ("FI39 3636 3002 0924 92", 1.03m, "5907 38390", "2023-03-11", "439363630020924920000010300000000000000590738390230311", 9),
            ("FI92 3939 0001 0033 91", 0.02m, "13 57914", "2099-12-24", "492393900010033910000000200000000000000001357914991224", 30),
            ("FI79 4405 2020 0360 82", 4883.15m, "RF09 8685 1625 9619 897", "2010-06-12", "579440520200360820048831509000000868516259619897100612", 74),
            ("FI58 1017 1000 0001 22", 482.99m, "RF06 5595 8224 3294 671", "2010-01-31", "558101710000001220004829906000000559582243294671100131", 31),
            ("FI02 5000 4640 0013 02", 693.80m, "RF61 6987 5672 0839", "2011-07-24", "502500046400013020006938061000000000698756720839110724", 59),
            ("FI15 6601 0001 5306 41", 7444.54m, "RF84 7758 4747 9064 7489", "2019-12-19", "515660100015306410074445484000007758474790647489191219", 16),
            ("FI16 8000 1400 0502 67", 935.85m, "RF60 7877 7679 6566 2868 7", null, "516800014000502670009358560000078777679656628687000000", 15),
            ("FI73 3131 3001 0000 58", 0.00m, "RF10 8686 24", "2013-08-09", "573313130010000580000000010000000000000000868624130809", 91),
            ("FI83 3301 0001 1007 75", 150000.20m, "RF71 9212 5374 2525 3989 7737", "2016-05-25", "583330100011007751500002071092125374252539897737160525", 80),
            ("FI39 3636 3002 0924 92", 1.03m, "RF66 5907 3839 0", "2023-03-11", "539363630020924920000010366000000000000590738390230311", 10),
            ("FI92 3939 0001 0033 91", 0.02m, "RF95 1357 914", "2099-12-24", "592393900010033910000000295000000000000001357914991224", 33),
        ];
        for (int i = 0; i < vectors.Length; i++)
        {
            var v = vectors[i];
            string? data = BankBarcode.TryBuild(v.Iban, v.Amount, v.Reference, v.Due is null ? null : DateOnly.Parse(v.Due));
            Equal($"barcode data #{i + 1}", v.Data, data);
            if (data is null) continue;
            int[] symbols = Code128.EncodeSetC(data);
            // The check value printed for version 5 example 3 does not follow from its own data string
            // under ISO/IEC 15417 (the other 17 examples do), so that one value is not asserted.
            if (i != 11) Equal($"barcode check #{i + 1}", v.Check, symbols[^2]);
            Equal($"barcode symbol count #{i + 1}", 30, symbols.Length);
            Equal($"barcode modules #{i + 1}", 332, Code128.TotalModules(Code128.ToModules(symbols)));
            Check($"iban valid #{i + 1}", Iban.IsValid(v.Iban));
            Check($"reference valid #{i + 1}", PaymentReference.IsValid(v.Reference), v.Reference);
        }
        Equal("amount too large is encoded as zeros", "00000000", BankBarcode.TryBuild("FI2112345600000785", 1_000_000m, "1232", null)![17..25]);
        Check("alphanumeric RF reference gives no barcode", BankBarcode.TryBuild("FI2112345600000785", 1m, "RF712348231A", null) is null);
        Check("foreign IBAN gives no barcode", BankBarcode.TryBuild("SE4550000000058398257466", 1m, "1232", null) is null);
    }
}
