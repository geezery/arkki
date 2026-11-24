using System.Security.Cryptography.X509Certificates;
using Arkki.Pdf;
using Arkki.Pdf.Barcodes;
using Arkki.Pdf.Layout;
using Arkki.Pdf.Reading;
using Arkki.Pdf.Signing;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>The README's examples, compiled and run, so the README cannot drift from the API.</summary>
internal static class ReadmeTests
{
    public static void Run()
    {
        // Create a document
        var doc = new PdfDocument();
        doc.Info.Title = "Order 1187";
        var fonts = new FontFamily(doc.AddFont(TestData.Regular), doc.AddFont(TestData.Bold));
        var body = new TextStyle(fonts, 10);
        var table = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Mm(30), HAlign.Right)])
            .SetHeader(body with { Bold = true }, "Item", "Amount");
        table.AddRow(body, "Machining", "258,00");
        new FlowDocument
        {
            PageWidth = PageSize.A4.Width, PageHeight = PageSize.A4.Height,
            Header = page => new TextBlock($"Page {page.PageNumber} ({page.PageCount})", body, HAlign.Right),
            Content = new Column(
                new TextBlock("Order 1187", body with { Size = 18, Bold = true }),
                new TextBlock("Long terms of delivery that wrap and are justified …", body, HAlign.Justify),
                new ListBlock(body, ListMarker.Number).Add("Check the parts").Add("Book the time"),
                table) { Spacing = 8 },
        }.RenderTo(doc);
        _ = new TextBlock([new("Due ", body), new("7.12.2026", body with { Bold = true })]);
        _ = PageSize.Landscape(PageSize.A4);
        _ = PageSize.Mm(100, 200);

        PdfFont font = fonts.Select(false, false);
        PdfPage page = doc.AddPage(PageSize.A4);
        page.Canvas.Text(font, 12, Unit.Mm(20), Unit.Mm(30), "Exactly here");
        page.Canvas.Barcode(new Code128().Encode("A-1187"), 20, 60, 120, 30);
        page.AddLink(20, 60, 100, 14, new Uri("https://example.com"));
        byte[] pdf = doc.ToArray();

        // Open, edit and merge
        var edit = PdfDocument.Open(pdf);
        PdfFont stampFont = edit.AddFont(TestData.Regular);
        PdfCanvas c = edit.Pages[0].Canvas;
        c.Save(); c.SetOpacity(0.2); c.TextRotated(stampFont, 60, 150, 500, 45, "DRAFT"); c.Restore();
        edit.AddPage(); edit.AddPage();
        edit.RemovePageAt(3);
        edit.MovePage(0, 2);
        edit.Pages[1].Rotation = 90;
        edit.Outline.Add("Terms", edit.Pages[1]);
        edit.Attach("order.xml", "<order/>"u8.ToArray(), "application/xml");
        byte[] stamped = edit.ToArray();
        _ = PdfDocument.Merge([pdf, stamped]).ToArray();
        _ = PdfDocument.ExtractPages(stamped, 0, 1);

        // Forms
        var formDoc = PdfDocument.Open(TestData.Pdf("form-pypdf"));
        formDoc.Form.SetValue("customer.email", "maija@example.com");
        formDoc.Form.SetChecked("agree", true);
        PdfPage formPage = formDoc.AddPage();
        formPage.AddTextField("email", x: 50, y: 100, width: 200, height: 18);
        formPage.AddCheckBox("newsletter", x: 50, y: 130, size: 12);
        formPage.AddRadioButton("plan", "basic", x: 50, y: 160, size: 12, selected: true);
        formPage.AddRadioButton("plan", "pro", x: 50, y: 180, size: 12);
        formPage.AddComboBox("language", x: 50, y: 210, width: 120, height: 18, ["Suomi", "Svenska", "English"], value: "Suomi");
        formPage.AddListBox("days", x: 50, y: 240, width: 120, height: 40, ["Mon", "Tue", "Wed"], multiSelect: true);
        formDoc.Form.SetValue("plan", "pro");
        formDoc.Form.SetValue("language", "Svenska");
        formDoc.Form.SetValues("days", ["Mon", "Wed"]);
        foreach (PdfFormField f in PdfReader.Open(formDoc.ToArray()).GetFormFields()) _ = $"{f.Name} = {f.Value}";

        // Encryption
        doc.Encryption = new PdfEncryption(userPassword: "open", ownerPassword: "owner", permissions: PdfPermissions.Print | PdfPermissions.Copy);
        var reader = PdfReader.Open(doc.ToArray(), password: "open");

        // Signatures
        var pki = new TestPki();
        byte[] signed = PdfSigner.Sign(pdf, pki.Signer, new PdfSignatureOptions { Reason = "Approved", Location = "Helsinki" });
        foreach (PdfSignatureInfo s in PdfSignatureVerifier.Verify(signed, new X509Certificate2Collection(pki.Root)))
            _ = $"{s.FieldName}: intact {s.IsIntact}, whole {s.CoversWholeDocument}, {s.Level}, trusted {s.ChainTrusted}";

        Check("README examples run", reader.PageCount > 1 && signed.Length > pdf.Length);
    }
}
