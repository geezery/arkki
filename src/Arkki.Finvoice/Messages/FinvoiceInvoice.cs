namespace Arkki.Finvoice;

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
