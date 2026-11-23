using Arkki.Pdf;
using Arkki.Pdf.Fonts;

namespace Arkki.Finvoice;

public sealed class InvoiceTheme
{
    /// <summary>Parsed fonts. Parse once at start-up and share the instances between all renders.</summary>
    public required TrueTypeFont RegularFont { get; init; }
    public required TrueTypeFont BoldFont { get; init; }

    /// <summary>Company logo. Load once with <see cref="PdfImage.Load"/> and share between renders.</summary>
    public PdfImage? Logo { get; init; }
    public double LogoMaxWidthMm { get; init; } = 62;
    public double LogoMaxHeightMm { get; init; } = 18;

    public Color Accent { get; init; } = Color.FromHex("#1D3557");
    public Color Muted { get; init; } = Color.Gray(0.42);
    public Color RuleColor { get; init; } = Color.Gray(0.80);

    /// <summary>Labels printed on the invoice. Defaults to <see cref="InvoiceTexts.Finnish"/>.</summary>
    public InvoiceTexts Texts { get; init; } = InvoiceTexts.Finnish;

    /// <summary>Pre-printed texts of the credit transfer form. Defaults to <see cref="GiroTexts.Standard"/>.</summary>
    public GiroTexts GiroTexts { get; init; } = GiroTexts.Standard;

    /// <summary>Print the credit transfer form and bank barcode on the last page.</summary>
    public bool IncludeGiro { get; init; } = true;

    /// <summary>Stream compression: Optimal gives the smallest files, Fastest the highest throughput.</summary>
    public System.IO.Compression.CompressionLevel Compression { get; init; } = System.IO.Compression.CompressionLevel.Optimal;

    /// <summary>Fixed creation timestamp for reproducible output; defaults to the current time.</summary>
    public DateTimeOffset? CreationDate { get; init; }
}
