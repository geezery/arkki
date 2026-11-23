using Arkki.Pdf;
using Arkki.Pdf.Barcodes;

namespace Arkki.Finvoice;

/// <summary>
/// The Finnish credit transfer form (tilisiirtolomake) as specified in Finance Finland's
/// "Tilisiirto-opas", section 2.16. The form is 210 mm × 4 inches and sits at the bottom of the
/// page. Its grid is defined in tenths of an inch horizontally and twelfths of an inch
/// vertically; those values are used here exactly as given in the guide.
/// </summary>
public static class GiroSlip
{
    public const double HeightMm = 101.6;

    // Horizontal grid (mm from the left paper edge).
    internal const double XLabel = 20.32;            // 8/10"
    internal const double XCentre = 111.76;          // + 36/10"
    internal const double XRight = 203.20;           // + 36/10"
    // The guide's model does not dimension the two dividers below; they are placed on the same
    // 1/10" grid to match the proportions of the model drawing.
    internal const double XReferenceValue = XCentre + 15.24;   // 6/10"
    internal const double XEuro = XCentre + 48.26;             // 19/10"

    // Vertical grid (mm from the top of the form), cumulative twelfths of an inch.
    private static double T(int twelfths) => twelfths * 25.4 / 12;
    internal static readonly double YAccount = T(8);      // payee account row: 8/12"
    internal static readonly double YPayee = T(15);       // payee row: 7/12"
    internal static readonly double YReferenceTop = T(27);
    internal static readonly double YSignature = T(29);   // payer block: 14/12"
    internal static readonly double YPayerEnd = T(31);    // signature row: 2/12"
    internal static readonly double YFormEnd = T(35);     // from-account / due date / amount row: 4/12"

    private const double ThinMm = 0.13, ThickMm = 0.50;
    private const double LabelSize = 7, LegalSize = 6, TitleSize = 8, DataSize = 10;

    private static readonly string[] LegalText =
    [
        "Maksu välitetään saajalle",
        "maksujenvälityksen ehtojen mukaisesti ja",
        "vain maksajan ilmoittaman tilinumeron",
        "perusteella.",
        "Betalningen förmedlas till mottagaren enligt",
        "villkoren för betalningsförmedling och",
        "endast till det kontonummer som betalaren",
        "angivit.",
    ];

    /// <summary>Draws the form at the bottom of a page of the given height (points).</summary>
    public static void Draw(PdfCanvas c, double pageHeight, GiroData data, GiroStyle style)
    {
        double top = pageHeight - Unit.Mm(HeightMm);
        double X(double mm) => Unit.Mm(mm);
        double Y(double mm) => top + Unit.Mm(mm);
        PdfFont regular = style.Fonts.Regular, bold = style.Fonts.Bold;

        // ---- Pre-printed form ----------------------------------------------------------------
        c.SetStrokeColor(style.FormColor);
        c.SetLineWidth(Unit.Mm(ThinMm));
        c.Line(0, Y(0), X(XRight), Y(0));
        c.Line(X(XLabel + 9), Y(YSignature), X(XCentre - 5), Y(YSignature));

        c.SetLineWidth(Unit.Mm(ThickMm));
        c.Line(0, Y(YAccount), X(XCentre), Y(YAccount));
        c.Line(0, Y(YPayee), X(XCentre), Y(YPayee));
        c.Line(X(XCentre), Y(YReferenceTop), X(XRight), Y(YReferenceTop));
        c.Line(0, Y(YPayerEnd), X(XRight), Y(YPayerEnd));
        c.Line(0, Y(YFormEnd), X(XRight), Y(YFormEnd));
        c.Line(X(XLabel), Y(0), X(XLabel), Y(YPayee));
        c.Line(X(XLabel), Y(YPayerEnd), X(XLabel), Y(YFormEnd));
        c.Line(X(XCentre), Y(0), X(XCentre), Y(YFormEnd));
        c.Line(X(XReferenceValue), Y(YReferenceTop), X(XReferenceValue), Y(YFormEnd));
        c.Line(X(XEuro), Y(YPayerEnd), X(XEuro), Y(YFormEnd));

        c.SetFillColor(style.FormColor);
        double labelRight = X(XLabel - 1.2);
        void Labels(double firstBaselineMm, params string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
                c.TextRight(regular, LabelSize, labelRight, Y(firstBaselineMm + i * 2.8), lines[i]);
        }
        Labels(4.6, "Saajan", "tilinumero", "Mottagarens", "kontonummer");
        Labels(YAccount + 6.2, "Saaja", "Mottagare");
        Labels(YPayee + 4.2, "Maksajan", "nimi ja", "osoite", "Betalarens", "namn och", "adress");
        c.TextRight(regular, LabelSize, labelRight, Y(YSignature - 0.7), "Allekirjoitus");
        c.TextRight(regular, LabelSize, labelRight, Y(YSignature + 2.5), "Underskrift");
        Labels(YPayerEnd + 3.4, "Tililtä nro", "Från konto nr");

        c.Text(regular, LabelSize, X(XLabel + 1.5), Y(3.2), "IBAN");
        c.Text(regular, LabelSize, X(XCentre + 1.3), Y(YReferenceTop + 3.2), "Viitenumero");
        c.Text(regular, LabelSize, X(XCentre + 1.3), Y(YReferenceTop + 6.0), "Ref. nr");
        c.Text(regular, LabelSize, X(XCentre + 1.3), Y(YPayerEnd + 3.2), "Eräpäivä");
        c.Text(regular, LabelSize, X(XCentre + 1.3), Y(YPayerEnd + 6.0), "Förfallodag");
        c.Text(regular, LabelSize, X(XEuro + 1.3), Y(YPayerEnd + 3.2), "Euro");

        // Form title, reading upwards beside the payer block.
        const string title = "TILISIIRTO. GIRERING";
        double titleWidth = bold.Width(title, TitleSize);
        c.TextRotated(bold, TitleSize, X(4.6), Y((YPayee + YPayerEnd) / 2) + titleWidth / 2, 90, title);

        for (int i = 0; i < LegalText.Length; i++)
            c.Text(regular, LegalSize, X(XLabel + 105 + 21.2), Y(YFormEnd + 4.8 + i * 2.45), LegalText[i]);

        // ---- Variable data -------------------------------------------------------------------
        c.SetFillColor(Color.Black);
        double dataLeft = X(XLabel + 2.5);

        double accountBaseline = data.Accounts.Count <= 1 ? 9.8 : 7.0;
        for (int i = 0; i < Math.Min(3, data.Accounts.Count); i++)
        {
            FinvoiceAccount account = data.Accounts[i];
            c.Text(regular, DataSize, dataLeft, Y(accountBaseline + i * 4.2), Iban.Format(account.Iban));
            if (!string.IsNullOrWhiteSpace(account.Bic))
                c.Text(regular, DataSize, X(XLabel + 56), Y(accountBaseline + i * 4.2), account.Bic);
        }

        Lines(c, regular, data.PayeeLines, dataLeft, Y(YAccount + 5.6), maxLines: 2);
        Lines(c, regular, data.PayerLines, dataLeft, Y(YPayee + 5.6), maxLines: 6);
        Lines(c, regular, data.MessageLines, X(XCentre + 3), Y(6.0), maxLines: 12);

        if (!string.IsNullOrWhiteSpace(data.Reference))
            c.Text(regular, DataSize, X(XReferenceValue + 3), Y(YReferenceTop + 5.7), PaymentReference.Format(data.Reference));
        if (data.DueDate is { } due)
            c.Text(regular, DataSize, X(XReferenceValue + 3), Y(YPayerEnd + 5.7), FiFormat.Date(due));
        if (data.Amount is { } amount)
            c.TextRight(regular, DataSize, X(XRight - 4), Y(YPayerEnd + 5.7), FiFormat.Money(amount));

        // ---- Bank barcode --------------------------------------------------------------------
        // Starts 8/10" from the left edge, which doubles as the 20 mm quiet zone; the legal text
        // on the right begins more than 20 mm after the longest allowed symbol.
        if (data.Barcode is { Length: 54 } digits)
        {
            byte[] modules = Code128.ToModules(Code128.EncodeSetC(digits));
            double moduleWidth = Unit.Mm(style.BarcodeWidthMm) / Code128.TotalModules(modules);
            c.Bars(modules, X(XLabel), Y(YFormEnd + 2.2), moduleWidth, Unit.Mm(style.BarcodeHeightMm));
        }
    }

    private static void Lines(PdfCanvas c, PdfFont font, List<string> lines, double x, double firstBaseline, int maxLines)
    {
        for (int i = 0; i < Math.Min(maxLines, lines.Count); i++)
            c.Text(font, DataSize, x, firstBaseline + i * Unit.Mm(4.3), lines[i]);
    }
}
