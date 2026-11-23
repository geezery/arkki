using System.Text;
using Arkki.Samples.Finvoice;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>The Finvoice 3.0 reader, in UTF-8 and ISO-8859-15.</summary>
internal static class FinvoiceReaderTests
{
    public static void Run()
    {
        FinvoiceInvoice inv = TestData.LoadInvoice();
        Equal("finvoice version", "3.0", inv.Version);
        Equal("finvoice seller", "Esimerkki Konepaja Oy", inv.Seller.Name);
        Equal("finvoice buyer town", "Jyväskylä", inv.Buyer.Town);
        Equal("finvoice rows", 44, inv.Rows.Count);
        Equal("finvoice due date", new DateOnly(2026, 12, 7), inv.DueDate);
        Equal("finvoice iban", "FI2112345600000785", inv.PaymentIban);
        Check("finvoice reference valid", inv.EpiReference is not null && PaymentReference.IsValid(inv.EpiReference));
        Equal("finvoice row sum equals total", inv.TotalVatExcluded, inv.Rows.Sum(r => r.VatExcludedAmount ?? 0));
        Equal("finvoice vat sum equals total", inv.TotalVatIncluded, inv.VatSpecifications.Sum(s => s.BaseAmount + s.VatAmount));

        // The same message in ISO-8859-15, the encoding many Finvoice senders use.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string latin = File.ReadAllText(TestData.FinvoicePath).Replace("encoding=\"UTF-8\"", "encoding=\"ISO-8859-15\"");
        FinvoiceInvoice inv15 = FinvoiceReader.Load(new MemoryStream(Encoding.GetEncoding("ISO-8859-15").GetBytes(latin)));
        Equal("finvoice ISO-8859-15", "Åke Öhman", inv15.Buyer.ContactPerson);
    }
}
