using Arkki.Pdf;
using Arkki.Pdf.Layout;

namespace Arkki.Finvoice;

/// <summary>Renders a Finvoice message as a multi-page invoice with a credit transfer form.</summary>
public static class FinvoiceRenderer
{
    public static byte[] RenderToArray(FinvoiceInvoice invoice, InvoiceTheme theme) => Render(invoice, theme).ToArray();

    public static PdfDocument Render(FinvoiceInvoice inv, InvoiceTheme theme)
    {
        var doc = new PdfDocument { Language = "fi-FI", Compression = theme.Compression };
        doc.Info.Title = $"{inv.InvoiceTypeText} {inv.InvoiceNumber}".Trim();
        doc.Info.Author = inv.Seller.Name;
        doc.Info.Creator = "Arkki.Finvoice";
        doc.Info.CreationDate = theme.CreationDate;

        var fonts = new FontFamily(doc.AddFont(theme.RegularFont), doc.AddFont(theme.BoldFont));
        PdfImage? logo = theme.Logo;

        var body = new TextStyle(fonts, 9);
        var bodyBold = body with { Bold = true };
        var small = new TextStyle(fonts, 7.5, Ink: theme.Muted);
        var label = new TextStyle(fonts, 8, Ink: theme.Muted);
        var address = new TextStyle(fonts, 10);
        var title = new TextStyle(fonts, 18, Bold: true, Ink: theme.Accent, LineHeight: 1.1);
        var headerCell = new TextStyle(fonts, 7.5, Bold: true, Ink: theme.Accent);

        var margins = Margins.Mm(20, 12, 15, 10);
        string? reference = inv.EpiReference;
        string? barcode = inv.PaymentIban is { } iban ? BankBarcode.TryBuild(iban, inv.PaymentAmount, reference, inv.PaymentDueDate) : null;

        // ---- Header: logo and title on every page, addresses and details on the first ---------
        Element? Header(PageContext page)
        {
            Element left = logo is not null
                ? new ImageBox(logo, Unit.Mm(theme.LogoMaxWidthMm), Unit.Mm(theme.LogoMaxHeightMm))
                : new TextBlock(inv.Seller.Name, new TextStyle(fonts, 14, Bold: true, Ink: theme.Accent));
            string pageText = page.PageCount > 0 ? $"Sivu {page.PageNumber} ({page.PageCount})" : "Sivu";
            var right = new Column(
                new TextBlock(inv.InvoiceTypeText.ToUpperInvariant(), title, HAlign.Right),
                new TextBlock($"Nro {inv.InvoiceNumber}   ·   {pageText}", small, HAlign.Right));
            var head = new Column(new Row().Add(Len.Mm(95), left).Add(Len.Star(), right));
            if (!page.IsFirst) return head.Add(new Spacer(Unit.Mm(2))).Add(new Rule(theme.RuleColor));

            var recipient = new Column(
                new TextBlock(string.Join(", ", new[] { inv.Seller.Name }.Concat(inv.Seller.AddressLines())), small),
                new Spacer(Unit.Mm(2.5)),
                new TextBlock(inv.Buyer.Name, address with { Bold = true }));
            foreach (string line in inv.Buyer.AddressLines()) recipient.Add(new TextBlock(line, address));
            if (inv.Buyer.ContactPerson is { } contact) recipient.Add(new TextBlock(contact, address));

            var details = new Table([new TableColumn(Len.Mm(30)), new TableColumn(Len.Star())]) { CellPaddingX = 0, CellPaddingY = 1.1 };
            void Detail(string name, string? value, bool strong = false)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    details.AddRow(new TextBlock(name, label), new TextBlock(value, strong ? bodyBold : body));
            }
            Detail("Laskun päiväys", inv.InvoiceDate is { } d ? FiFormat.Date(d) : null);
            Detail("Laskun numero", inv.InvoiceNumber);
            Detail("Eräpäivä", inv.PaymentDueDate is { } due ? FiFormat.Date(due) : null, strong: true);
            Detail("Viitenumero", reference is null ? null : PaymentReference.Format(reference));
            Detail("Maksuehto", inv.PaymentTermsText);
            Detail("Viivästyskorko", inv.OverdueFinePercent is { } fine ? FiFormat.Number(fine, 1, 2) + " %" : inv.OverdueFineText);
            Detail("Ostajan Y-tunnus", inv.Buyer.BusinessId);
            Detail("Viitteenne", inv.BuyerReference);
            Detail("Tilausnumero", inv.OrderIdentifier);
            Detail("Sopimusnumero", inv.AgreementIdentifier);
            Detail("Myyjän yhteyshenkilö", inv.Seller.ContactPerson);

            return head
                .Add(new Spacer(Unit.Mm(11)))
                .Add(new Row().Add(Len.Mm(95), recipient).Add(Len.Star(), details))
                .Add(new Spacer(Unit.Mm(4)));
        }

        // ---- Footer: seller details ------------------------------------------------------------
        Element Footer(PageContext page)
        {
            static string Join(params string?[] parts) => string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            FinvoiceAccount? account = inv.SellerAccounts.FirstOrDefault();
            return new Column(
                new Rule(theme.RuleColor),
                new Spacer(Unit.Mm(1.5)),
                new Row { Spacing = Unit.Mm(4) }
                    .Add(Len.Star(1.2), new TextBlock(Join([inv.Seller.Name, .. inv.Seller.AddressLines()]), small))
                    .Add(Len.Star(1.0), new TextBlock(Join(
                        inv.Seller.BusinessId is { } y ? "Y-tunnus " + y : null,
                        inv.Seller.VatNumber is { } vat ? "ALV-nro " + vat : null,
                        inv.SellerHomeTown is { } town ? "Kotipaikka " + town : null), small))
                    .Add(Len.Star(1.7), new TextBlock(Join(inv.SellerPhone, inv.SellerEmail, inv.SellerWeb), small))
                    .Add(Len.Star(1.2), new TextBlock(Join(
                        account is null ? null : Iban.Format(account.Iban),
                        account?.Bic is { } bic ? "BIC " + bic : null,
                        inv.SellerVatRegistrationText), small)));
        }

        // ---- Content: invoice rows, totals, free text --------------------------------------------
        var rows = new Table(
        [
            new TableColumn(Len.Mm(22)),
            new TableColumn(Len.Star()),
            new TableColumn(Len.Mm(16), HAlign.Right),
            new TableColumn(Len.Mm(11)),
            new TableColumn(Len.Mm(22), HAlign.Right),
            new TableColumn(Len.Mm(13), HAlign.Right),
            new TableColumn(Len.Mm(25), HAlign.Right),
        ])
        {
            HeaderRule = theme.Accent,
            RowRule = theme.RuleColor,
            CellPaddingY = 2.6,
        };
        rows.SetHeader(headerCell, "NIMIKE", "KUVAUS", "MÄÄRÄ", "YKS.", "À-HINTA", "ALV %", "VEROTON " + inv.Currency);
        foreach (FinvoiceRow r in inv.Rows)
        {
            Element description = new TextBlock(r.ArticleName ?? "", body);
            if (r.FreeTexts.Count > 0)
                description = new Column(description, new TextBlock(string.Join("\n", r.FreeTexts), small));
            rows.AddRow(
                new TextBlock(r.ArticleIdentifier ?? "", body),
                description,
                new TextBlock(r.Quantity is { } q ? FiFormat.Number(q, 0, 3) : "", body, HAlign.Right),
                new TextBlock(r.Unit ?? "", body),
                new TextBlock(r.UnitPrice is { } p ? FiFormat.Number(p, 2, 4) : "", body, HAlign.Right),
                new TextBlock(r.VatRatePercent is { } v ? FiFormat.Number(v, 0, 2) : "", body, HAlign.Right),
                new TextBlock((r.VatExcludedAmount ?? r.Amount) is { } a ? FiFormat.Money(a) : "", body, HAlign.Right));
        }

        var vat = new Table(
        [
            new TableColumn(Len.Mm(15), HAlign.Right), new TableColumn(Len.Mm(24), HAlign.Right),
            new TableColumn(Len.Mm(21), HAlign.Right), new TableColumn(Len.Mm(24), HAlign.Right),
        ])
        { HeaderRule = theme.RuleColor, CellPaddingY = 1.6 };
        vat.SetHeader(headerCell, "ALV %", "VEROTON", "ALV", "VEROLLINEN");
        foreach (FinvoiceVatSpecification s in inv.VatSpecifications)
            vat.AddRow(body, FiFormat.Number(s.RatePercent, 0, 2), FiFormat.Money(s.BaseAmount), FiFormat.Money(s.VatAmount), FiFormat.Money(s.BaseAmount + s.VatAmount));

        var totals = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Mm(30), HAlign.Right)]) { CellPaddingY = 1.6 };
        if (inv.TotalVatExcluded is { } excl) totals.AddRow(body, "Veroton yhteensä", FiFormat.Money(excl));
        if (inv.TotalVat is { } vatTotal) totals.AddRow(body, "Arvonlisävero yhteensä", FiFormat.Money(vatTotal));
        var grand = new TextStyle(fonts, 11, Bold: true, Ink: theme.Accent);
        if (inv.TotalVatIncluded is { } incl)
            totals.AddRow(new TextBlock("Maksettava yhteensä " + inv.Currency, grand), new TextBlock(FiFormat.Money(incl), grand, HAlign.Right));

        var content = new Column(rows, new Spacer(Unit.Mm(5)),
            new Row { Spacing = Unit.Mm(8) }.Add(Len.Mm(84), vat).Add(Len.Star(), totals).KeepTogether());
        if (inv.FreeTexts.Count > 0)
            content.Add(new Spacer(Unit.Mm(5))).Add(new TextBlock(string.Join("\n", inv.FreeTexts), body));

        // ---- Credit transfer form on the last page ---------------------------------------------
        const double virtualBarcodeBandMm = 9;
        Action<PdfCanvas, PageContext>? overlay = null;
        if (theme.IncludeGiro)
        {
            var giro = new GiroData { Reference = reference, DueDate = inv.PaymentDueDate, Amount = inv.PaymentAmount, Barcode = barcode };
            if (inv.SellerAccounts.Count > 0) giro.Accounts.AddRange(inv.SellerAccounts);
            else if (inv.EpiAccountIban is { } epiIban) giro.Accounts.Add(new FinvoiceAccount(epiIban, inv.EpiBic));
            giro.PayeeLines.Add(inv.EpiBeneficiaryName ?? inv.Seller.Name);
            giro.PayeeLines.Add(string.Join(", ", inv.Seller.AddressLines()));
            giro.PayerLines.Add(inv.Buyer.Name);
            giro.PayerLines.AddRange(inv.Buyer.AddressLines());
            giro.MessageLines.Add($"Laskun numero / Fakturanummer {inv.InvoiceNumber}");
            if (inv.InvoiceDate is { } invoiceDate) giro.MessageLines.Add($"Laskun päiväys / Fakturadatum {FiFormat.Date(invoiceDate)}");
            var giroStyle = new GiroStyle { Fonts = fonts };

            overlay = (canvas, _) =>
            {
                double slipTop = PageSize.A4.Height - Unit.Mm(GiroSlip.HeightMm);
                if (barcode is not null)
                {
                    canvas.SetFillColor(theme.Muted);
                    canvas.Text(fonts.Regular, 7.5, margins.Left, slipTop - Unit.Mm(3.2), "Virtuaaliviivakoodi");
                    canvas.SetFillColor(Color.Black);
                    canvas.Text(fonts.Regular, 8.5, margins.Left + Unit.Mm(26), slipTop - Unit.Mm(3.2), barcode);
                }
                GiroSlip.Draw(canvas, PageSize.A4.Height, giro, giroStyle);
            };
        }

        new FlowDocument
        {
            Margins = margins,
            Header = Header,
            Footer = Footer,
            Content = content,
            LastPageReserve = theme.IncludeGiro ? Unit.Mm(GiroSlip.HeightMm + virtualBarcodeBandMm) - margins.Bottom : 0,
            LastPageOverlay = overlay,
        }.RenderTo(doc);

        return doc;
    }
}
