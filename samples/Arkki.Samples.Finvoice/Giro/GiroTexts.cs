namespace Arkki.Samples.Finvoice;

/// <summary>
/// The pre-printed texts of the credit transfer form. <see cref="Standard"/> holds the bilingual
/// Finnish and Swedish wording of Finance Finland's model form (embedded as <c>giro-texts.json</c>).
/// The form's layout reserves room for exactly the number of lines each label has there, so
/// replacement texts must keep the same line counts.
/// </summary>
public sealed record GiroTexts
{
    private static readonly Lazy<GiroTexts> s_standard = new(() =>
    {
        using Stream json = TextResource.OpenEmbedded("giro-texts.json");
        return Load(json, "giro-texts.json");
    });

    /// <summary>The wording of the model form.</summary>
    public static GiroTexts Standard => s_standard.Value;

    /// <summary>4 lines, beside the payee account.</summary>
    public required string[] PayeeAccount { get; init; }
    public required string Iban { get; init; }
    /// <summary>2 lines.</summary>
    public required string[] Payee { get; init; }
    /// <summary>6 lines.</summary>
    public required string[] Payer { get; init; }
    /// <summary>2 lines, above and below the signature line.</summary>
    public required string[] Signature { get; init; }
    /// <summary>2 lines.</summary>
    public required string[] FromAccount { get; init; }
    /// <summary>2 lines.</summary>
    public required string[] Reference { get; init; }
    /// <summary>2 lines.</summary>
    public required string[] DueDate { get; init; }
    public required string Euro { get; init; }
    /// <summary>Printed vertically beside the payer block.</summary>
    public required string Title { get; init; }
    /// <summary>8 lines of small print to the right of the barcode.</summary>
    public required string[] LegalText { get; init; }

    /// <summary>Reads texts from JSON with the same keys and line counts as the built-in <c>giro-texts.json</c>.</summary>
    public static GiroTexts Load(Stream json, string name = "giro texts")
    {
        using var r = TextResource.Parse(json, name);
        var texts = new GiroTexts
        {
            PayeeAccount = r.Lines("payeeAccount", 4),
            Iban = r.String("iban"),
            Payee = r.Lines("payee", 2),
            Payer = r.Lines("payer", 6),
            Signature = r.Lines("signature", 2),
            FromAccount = r.Lines("fromAccount", 2),
            Reference = r.Lines("reference", 2),
            DueDate = r.Lines("dueDate", 2),
            Euro = r.String("euro"),
            Title = r.String("title"),
            LegalText = r.Lines("legalText", 8),
        };
        r.EnsureAllUsed();
        return texts;
    }
}
