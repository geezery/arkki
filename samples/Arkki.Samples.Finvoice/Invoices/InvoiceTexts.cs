using System.Globalization;

namespace Arkki.Samples.Finvoice;

/// <summary>
/// Every label the invoice renderer prints. The texts live in JSON (Finnish ships embedded as
/// <c>invoice-texts-fi.json</c>); load another language with <see cref="Load"/>, or adjust single
/// labels with a <c>with</c> expression: <c>InvoiceTexts.Finnish with { DueDate = "Maksupäivä" }</c>.
/// Values marked as a format take <see cref="string.Format(IFormatProvider, string, object[])"/> placeholders.
/// </summary>
public sealed record InvoiceTexts
{
    private static readonly Lazy<InvoiceTexts> s_finnish = new(() =>
    {
        using Stream json = TextResource.OpenEmbedded("invoice-texts-fi.json");
        return Load(json, "invoice-texts-fi.json");
    });

    /// <summary>The built-in Finnish texts.</summary>
    public static InvoiceTexts Finnish => s_finnish.Value;

    /// <summary>BCP 47 language tag written to the PDF, for example <c>fi-FI</c>.</summary>
    public required string Language { get; init; }

    /// <summary>Format: {0} page number, {1} page count.</summary>
    public required string PageNumber { get; init; }
    /// <summary>Used while the page count is not yet known.</summary>
    public required string PageNumberUnknownCount { get; init; }
    /// <summary>Format: {0} invoice number, in the page header.</summary>
    public required string InvoiceNumberShort { get; init; }

    public required string InvoiceDate { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string DueDate { get; init; }
    public required string Reference { get; init; }
    public required string PaymentTerms { get; init; }
    public required string OverdueInterest { get; init; }
    public required string BuyerBusinessId { get; init; }
    public required string BuyerReference { get; init; }
    public required string OrderNumber { get; init; }
    public required string AgreementNumber { get; init; }
    public required string SellerContact { get; init; }

    /// <summary>Format: {0} business id, in the footer.</summary>
    public required string SellerBusinessId { get; init; }
    /// <summary>Format: {0} VAT number, in the footer.</summary>
    public required string SellerVatNumber { get; init; }
    /// <summary>Format: {0} home town, in the footer.</summary>
    public required string SellerHomeTown { get; init; }
    /// <summary>Format: {0} BIC, in the footer.</summary>
    public required string Bic { get; init; }

    public required string RowArticle { get; init; }
    public required string RowDescription { get; init; }
    public required string RowQuantity { get; init; }
    public required string RowUnit { get; init; }
    public required string RowUnitPrice { get; init; }
    public required string RowVatRate { get; init; }
    /// <summary>Format: {0} currency code.</summary>
    public required string RowAmount { get; init; }

    public required string VatRate { get; init; }
    public required string VatBase { get; init; }
    public required string VatAmount { get; init; }
    public required string VatTotal { get; init; }

    public required string TotalVatExcluded { get; init; }
    public required string TotalVat { get; init; }
    /// <summary>Format: {0} currency code.</summary>
    public required string TotalToPay { get; init; }

    /// <summary>Label of the virtual barcode printed above the credit transfer form.</summary>
    public required string VirtualBarcode { get; init; }
    /// <summary>Format: {0} invoice number, in the message field of the credit transfer form.</summary>
    public required string GiroInvoiceNumber { get; init; }
    /// <summary>Format: {0} invoice date, in the message field of the credit transfer form.</summary>
    public required string GiroInvoiceDate { get; init; }

    /// <summary>Reads texts from JSON with the same keys as the built-in <c>invoice-texts-fi.json</c>.</summary>
    public static InvoiceTexts Load(Stream json, string name = "invoice texts")
    {
        using var r = TextResource.Parse(json, name);
        var texts = new InvoiceTexts
        {
            Language = r.String("language"),
            PageNumber = r.String("pageNumber"),
            PageNumberUnknownCount = r.String("pageNumberUnknownCount"),
            InvoiceNumberShort = r.String("invoiceNumberShort"),
            InvoiceDate = r.String("invoiceDate"),
            InvoiceNumber = r.String("invoiceNumber"),
            DueDate = r.String("dueDate"),
            Reference = r.String("reference"),
            PaymentTerms = r.String("paymentTerms"),
            OverdueInterest = r.String("overdueInterest"),
            BuyerBusinessId = r.String("buyerBusinessId"),
            BuyerReference = r.String("buyerReference"),
            OrderNumber = r.String("orderNumber"),
            AgreementNumber = r.String("agreementNumber"),
            SellerContact = r.String("sellerContact"),
            SellerBusinessId = r.String("sellerBusinessId"),
            SellerVatNumber = r.String("sellerVatNumber"),
            SellerHomeTown = r.String("sellerHomeTown"),
            Bic = r.String("bic"),
            RowArticle = r.String("rowArticle"),
            RowDescription = r.String("rowDescription"),
            RowQuantity = r.String("rowQuantity"),
            RowUnit = r.String("rowUnit"),
            RowUnitPrice = r.String("rowUnitPrice"),
            RowVatRate = r.String("rowVatRate"),
            RowAmount = r.String("rowAmount"),
            VatRate = r.String("vatRate"),
            VatBase = r.String("vatBase"),
            VatAmount = r.String("vatAmount"),
            VatTotal = r.String("vatTotal"),
            TotalVatExcluded = r.String("totalVatExcluded"),
            TotalVat = r.String("totalVat"),
            TotalToPay = r.String("totalToPay"),
            VirtualBarcode = r.String("virtualBarcode"),
            GiroInvoiceNumber = r.String("giroInvoiceNumber"),
            GiroInvoiceDate = r.String("giroInvoiceDate"),
        };
        r.EnsureAllUsed();
        return texts;
    }

    internal static string Format(string format, object? arg0) => string.Format(CultureInfo.InvariantCulture, format, arg0);
    internal static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.InvariantCulture, format, arg0, arg1);
}
