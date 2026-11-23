using Arkki.Finvoice;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>References, IBAN and number formats.</summary>
internal static class PaymentFormatTests
{
    public static void Run()
    {
        Equal("national check digit", "1232", PaymentReference.CreateNational("123"));
        Check("invalid national reference", !PaymentReference.IsValidNational("1233"));
        Equal("national to RF", "RF09868516259619897", PaymentReference.ToRf("868516259619897"));
        Equal("national print format", "86851 62596 19897", PaymentReference.Format("868516259619897"));
        Equal("national print format, uneven", "69 87567 20834 35364", PaymentReference.Format("0006987567208343 5364"));
        Equal("RF print format", "RF60 7877 7679 6566 2868 7", PaymentReference.Format("RF60787776796566286 87"));
        Equal("iban format", "FI21 1234 5600 0007 85", Iban.Format("fi2112345600000785"));
        Check("iban invalid", !Iban.IsValid("FI2112345600000786"));
        Equal("money", "1 234 567,80", FiFormat.Money(1234567.8m));
        Equal("money negative", "-0,50", FiFormat.Money(-0.5m));
        Equal("number trims", "2,5", FiFormat.Number(2.500m, 0, 3));
        Equal("date", "1.2.2026", FiFormat.Date(new DateOnly(2026, 2, 1)));
    }
}
