namespace Arkki.Finvoice;

public sealed record FinvoiceVatSpecification(decimal BaseAmount, decimal RatePercent, decimal VatAmount, string? FreeText);
