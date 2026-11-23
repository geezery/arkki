namespace Arkki.Finvoice;

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
