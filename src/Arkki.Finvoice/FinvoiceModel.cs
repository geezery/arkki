using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Arkki.Finvoice;

public sealed class FinvoiceParty
{
    public string Name { get; set; } = "";
    public string? BusinessId { get; set; }
    public string? VatNumber { get; set; }
    public string? Street { get; set; }
    public string? PostCode { get; set; }
    public string? Town { get; set; }
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? ContactPerson { get; set; }

    public IEnumerable<string> AddressLines()
    {
        if (!string.IsNullOrWhiteSpace(Street)) yield return Street;
        string town = $"{PostCode} {Town}".Trim();
        if (town.Length > 0) yield return town;
        if (!string.IsNullOrWhiteSpace(CountryName) && !string.Equals(CountryCode, "FI", StringComparison.OrdinalIgnoreCase)) yield return CountryName;
    }
}

public sealed record FinvoiceAccount(string Iban, string? Bic);

public sealed record FinvoiceVatSpecification(decimal BaseAmount, decimal RatePercent, decimal VatAmount, string? FreeText);

public sealed class FinvoiceRow
{
    public string? ArticleIdentifier { get; set; }
    public string? ArticleName { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? VatRatePercent { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? VatExcludedAmount { get; set; }
    public decimal? Amount { get; set; }
    public List<string> FreeTexts { get; } = [];
}

/// <summary>The parts of a Finvoice 3.0 message needed to render and pay an invoice.</summary>
public sealed class FinvoiceInvoice
{
    public string? Version { get; set; }
    public FinvoiceParty Seller { get; } = new();
    public FinvoiceParty Buyer { get; } = new();
    public List<FinvoiceAccount> SellerAccounts { get; } = [];
    public string? SellerHomeTown { get; set; }
    public string? SellerVatRegistrationText { get; set; }
    public string? SellerPhone { get; set; }
    public string? SellerEmail { get; set; }
    public string? SellerWeb { get; set; }

    public string? InvoiceTypeCode { get; set; }
    public string InvoiceTypeText { get; set; } = "LASKU";
    public string InvoiceNumber { get; set; } = "";
    public DateOnly? InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? OrderIdentifier { get; set; }
    public string? AgreementIdentifier { get; set; }
    public string? BuyerReference { get; set; }
    public string? SellerReference { get; set; }
    public decimal? TotalVatExcluded { get; set; }
    public decimal? TotalVat { get; set; }
    public decimal? TotalVatIncluded { get; set; }
    public List<FinvoiceVatSpecification> VatSpecifications { get; } = [];
    public List<string> FreeTexts { get; } = [];
    public string? PaymentTermsText { get; set; }
    public string? OverdueFineText { get; set; }
    public decimal? OverdueFinePercent { get; set; }
    public List<FinvoiceRow> Rows { get; } = [];

    // Payment instruction (EpiDetails): what the payer's bank needs.
    public string? EpiBeneficiaryName { get; set; }
    public string? EpiAccountIban { get; set; }
    public string? EpiBic { get; set; }
    public string? EpiReference { get; set; }
    public decimal? EpiAmount { get; set; }
    public DateOnly? EpiDueDate { get; set; }

    /// <summary>Account to pay to: the payment instruction's account, else the first seller account.</summary>
    public string? PaymentIban => EpiAccountIban ?? SellerAccounts.FirstOrDefault()?.Iban;
    public decimal? PaymentAmount => EpiAmount ?? TotalVatIncluded;
    public DateOnly? PaymentDueDate => EpiDueDate ?? DueDate;
}

/// <summary>
/// Reads Finvoice XML into <see cref="FinvoiceInvoice"/>. The reader is tolerant: it picks the
/// elements it knows by name and ignores the rest, so files from different senders load without
/// schema validation.
/// </summary>
public static class FinvoiceReader
{
    static FinvoiceReader()
    {
        // Finvoice files are commonly ISO-8859-15, which .NET only knows via the code pages provider.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static FinvoiceInvoice Load(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Load(fs);
    }

    public static FinvoiceInvoice Load(Stream stream) => Read(XDocument.Load(stream));

    public static FinvoiceInvoice Parse(string xml) => Read(XDocument.Parse(xml));

    private static FinvoiceInvoice Read(XDocument doc)
    {
        // A file may wrap the message in a SOAP envelope; find the Finvoice element wherever it is.
        XElement root = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Finvoice")
            ?? throw new InvalidDataException("The document contains no Finvoice element.");

        var inv = new FinvoiceInvoice { Version = (string?)root.Attribute("Version") };

        if (Child(root, "SellerPartyDetails") is { } seller)
        {
            inv.Seller.BusinessId = Text(seller, "SellerPartyIdentifier");
            inv.Seller.Name = string.Join(" ", Children(seller, "SellerOrganisationName").Select(e => e.Value.Trim()));
            inv.Seller.VatNumber = Text(seller, "SellerOrganisationTaxCode");
            ReadAddress(inv.Seller, Child(seller, "SellerPostalAddressDetails"), "Seller");
        }
        inv.Seller.ContactPerson = Text(root, "SellerContactPersonName");

        if (Child(root, "SellerInformationDetails") is { } info)
        {
            inv.SellerHomeTown = Text(info, "SellerHomeTownName");
            inv.SellerVatRegistrationText = Text(info, "SellerVatRegistrationText");
            inv.SellerPhone = Text(info, "SellerPhoneNumber");
            inv.SellerEmail = Text(info, "SellerCommonEmailaddressIdentifier");
            inv.SellerWeb = Text(info, "SellerWebaddressIdentifier");
            foreach (XElement account in Children(info, "SellerAccountDetails"))
                if (Text(account, "SellerAccountID") is { } iban)
                    inv.SellerAccounts.Add(new FinvoiceAccount(Iban.Normalize(iban), Text(account, "SellerBic")));
        }

        if (Child(root, "BuyerPartyDetails") is { } buyer)
        {
            inv.Buyer.BusinessId = Text(buyer, "BuyerPartyIdentifier");
            inv.Buyer.Name = string.Join(" ", Children(buyer, "BuyerOrganisationName").Select(e => e.Value.Trim()));
            inv.Buyer.VatNumber = Text(buyer, "BuyerOrganisationTaxCode");
            ReadAddress(inv.Buyer, Child(buyer, "BuyerPostalAddressDetails"), "Buyer");
        }
        inv.Buyer.ContactPerson = Text(root, "BuyerContactPersonName");

        if (Child(root, "InvoiceDetails") is { } d)
        {
            inv.InvoiceTypeCode = Text(d, "InvoiceTypeCode");
            inv.InvoiceTypeText = Text(d, "InvoiceTypeText") ?? inv.InvoiceTypeText;
            inv.InvoiceNumber = Text(d, "InvoiceNumber") ?? "";
            inv.InvoiceDate = Date(Child(d, "InvoiceDate"));
            inv.SellerReference = Text(d, "SellerReferenceIdentifier");
            inv.OrderIdentifier = Text(d, "OrderIdentifier");
            inv.AgreementIdentifier = Text(d, "AgreementIdentifier");
            inv.BuyerReference = Text(d, "BuyerReferenceIdentifier");
            XElement? total = Child(d, "InvoiceTotalVatIncludedAmount");
            inv.Currency = (string?)total?.Attribute("AmountCurrencyIdentifier") ?? inv.Currency;
            inv.TotalVatExcluded = Amount(Child(d, "InvoiceTotalVatExcludedAmount"));
            inv.TotalVat = Amount(Child(d, "InvoiceTotalVatAmount"));
            inv.TotalVatIncluded = Amount(total);
            foreach (XElement v in Children(d, "VatSpecificationDetails"))
                inv.VatSpecifications.Add(new FinvoiceVatSpecification(
                    Amount(Child(v, "VatBaseAmount")) ?? 0, Amount(Child(v, "VatRatePercent")) ?? 0,
                    Amount(Child(v, "VatRateAmount")) ?? 0, Text(v, "VatFreeText")));
            inv.FreeTexts.AddRange(Children(d, "InvoiceFreeText").Select(e => e.Value.Trim()).Where(s => s.Length > 0));
            if (Child(d, "PaymentTermsDetails") is { } terms)
            {
                inv.PaymentTermsText = Text(terms, "PaymentTermsFreeText");
                inv.DueDate = Date(Child(terms, "InvoiceDueDate"));
                if (Child(terms, "PaymentOverDueFineDetails") is { } fine)
                {
                    inv.OverdueFineText = Text(fine, "PaymentOverDueFineFreeText");
                    inv.OverdueFinePercent = Amount(Child(fine, "PaymentOverDueFinePercent"));
                }
            }
        }

        foreach (XElement r in Children(root, "InvoiceRow"))
        {
            XElement? quantity = Child(r, "DeliveredQuantity") ?? Child(r, "InvoicedQuantity") ?? Child(r, "OrderedQuantity");
            var row = new FinvoiceRow
            {
                ArticleIdentifier = Text(r, "ArticleIdentifier"),
                ArticleName = Text(r, "ArticleName"),
                Quantity = Amount(quantity),
                Unit = (string?)quantity?.Attribute("QuantityUnitCode"),
                UnitPrice = Amount(Child(r, "UnitPriceAmount")),
                VatRatePercent = Amount(Child(r, "RowVatRatePercent")),
                VatAmount = Amount(Child(r, "RowVatAmount")),
                VatExcludedAmount = Amount(Child(r, "RowVatExcludedAmount")),
                Amount = Amount(Child(r, "RowAmount")),
            };
            row.FreeTexts.AddRange(Children(r, "RowFreeText").Select(e => e.Value.Trim()).Where(s => s.Length > 0));
            inv.Rows.Add(row);
        }

        if (Child(root, "EpiDetails") is { } epi)
        {
            inv.EpiBic = Text(epi, "EpiBfiIdentifier");
            inv.EpiBeneficiaryName = Text(epi, "EpiNameAddressDetails");
            if (Text(epi, "EpiAccountID") is { } account) inv.EpiAccountIban = Iban.Normalize(account);
            inv.EpiReference = Text(epi, "EpiRemittanceInfoIdentifier");
            inv.EpiAmount = Amount(Descendant(epi, "EpiInstructedAmount"));
            inv.EpiDueDate = Date(Descendant(epi, "EpiDateOptionDate"));
        }

        return inv;
    }

    private static void ReadAddress(FinvoiceParty party, XElement? address, string prefix)
    {
        if (address is null) return;
        party.Street = string.Join(", ", Children(address, prefix + "StreetName").Select(e => e.Value.Trim()).Where(s => s.Length > 0));
        party.Town = Text(address, prefix + "TownName");
        party.PostCode = Text(address, prefix + "PostCodeIdentifier");
        party.CountryCode = Text(address, "CountryCode");
        party.CountryName = Text(address, "CountryName");
    }

    private static XElement? Child(XElement parent, string name) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);
    private static IEnumerable<XElement> Children(XElement parent, string name) => parent.Elements().Where(e => e.Name.LocalName == name);
    private static XElement? Descendant(XElement parent, string name) => parent.Descendants().FirstOrDefault(e => e.Name.LocalName == name);

    private static string? Text(XElement parent, string name)
    {
        string? value = Descendant(parent, name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Finvoice amounts use a decimal comma: 1234,56.</summary>
    private static decimal? Amount(XElement? e)
    {
        if (e is null) return null;
        string s = e.Value.Trim().Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? v : null;
    }

    /// <summary>Finvoice dates are CCYYMMDD.</summary>
    private static DateOnly? Date(XElement? e)
    {
        if (e is null) return null;
        return DateOnly.TryParseExact(e.Value.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d) ? d : null;
    }
}
