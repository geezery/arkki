namespace Arkki.Finvoice;

public sealed class GiroData
{
    /// <summary>Payee accounts as printed; the barcode can carry only one of them.</summary>
    public List<FinvoiceAccount> Accounts { get; } = [];
    public List<string> PayeeLines { get; } = [];
    public List<string> PayerLines { get; } = [];
    public List<string> MessageLines { get; } = [];
    public string? Reference { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? Amount { get; set; }

    /// <summary>54-digit bank barcode data, or null to leave the barcode out.</summary>
    public string? Barcode { get; set; }
}
