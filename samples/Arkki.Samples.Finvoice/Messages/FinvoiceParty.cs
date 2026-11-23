namespace Arkki.Samples.Finvoice;

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
