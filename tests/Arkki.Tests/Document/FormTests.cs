using Arkki.Pdf;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Form fields: created here, read back, filled in this and other producers' files.</summary>
internal static class FormTests
{
    public static void Run()
    {
        // Create a form.
        var doc = new PdfDocument();
        PdfPage page = doc.AddPage();
        page.AddTextField("customer.name", 72, 72, 200, 18, "Matti Meikäläinen");
        page.AddTextField("customer.email", 72, 100, 200, 18);
        page.AddTextField("notes", 72, 130, 300, 60, "Rivi 1\nRivi 2", fontSize: 9, multiline: true);
        page.AddCheckBox("agree", 72, 200, 12, isChecked: true);
        page.AddCheckBox("newsletter", 72, 220, 12);
        Throws<ArgumentException>("field names are unique", () => page.AddTextField("agree", 0, 0, 10, 10));
        byte[] pdf = doc.ToArray();
        External.QpdfClean("new form", pdf);

        IReadOnlyList<PdfFormField> fields = PdfReader.Open(pdf).GetFormFields();
        Equal("all fields read back", "customer.name,customer.email,notes,agree,newsletter", string.Join(",", fields.Select(f => f.Name)));
        Equal("text value reads back", "Matti Meikäläinen", fields[0].Value);
        Equal("multiline value reads back", "Rivi 1\nRivi 2", fields[2].Value);
        Check("checked box reads back", fields[3].IsChecked && fields[3].Kind == PdfFieldKind.CheckBox);
        Check("unchecked box reads back", !fields[4].IsChecked);
        string? py = External.PyPdf(pdf, "print(sorted((k, str(v.get('/V'))) for k, v in pypdf.PdfReader(sys.argv[1]).get_fields().items()))");
        if (py is not null) Check("pypdf reads the fields", py.Contains("('agree', '/Yes')") && py.Contains("('customer.name', 'Matti Meikäläinen')"), py);
        string? shown = External.PopplerText(pdf);
        if (shown is not null) Check("the value shows on the page", shown.Contains("Matti Meikäläinen"), shown);

        // Fill it: values and appearances change, the rest stays.
        PdfDocument fill = PdfDocument.Open(pdf);
        fill.Form.SetValue("customer.email", "matti@example.com");
        fill.Form.SetChecked("newsletter", true);
        fill.Form.SetChecked("agree", false);
        Throws<KeyNotFoundException>("unknown field", () => fill.Form.SetValue("nope", "x"));
        Throws<InvalidOperationException>("a text field is not a check box", () => fill.Form.SetChecked("notes", true));
        byte[] filled = fill.ToArray();
        External.QpdfClean("filled form", filled);
        var after = PdfReader.Open(filled).GetFormFields().ToDictionary(f => f.Name);
        Equal("filled text", "matti@example.com", after["customer.email"].Value);
        Check("filled check box", after["newsletter"].IsChecked && !after["agree"].IsChecked);
        Equal("untouched field keeps its value", "Matti Meikäläinen", after["customer.name"].Value);
        string? filledText = External.PopplerText(filled);
        if (filledText is not null) Check("the filled value shows on the page", filledText.Contains("matti@example.com"), filledText);

        // A form written by another producer (pypdf): a merged field, a check box and a field under a parent.
        PdfReader other = PdfReader.Open(TestData.Pdf("form-pypdf"));
        var otherFields = other.GetFormFields().ToDictionary(f => f.Name);
        Check("other producer's fields by full name", otherFields.ContainsKey("name") && otherFields.ContainsKey("agree") && otherFields.ContainsKey("customer.email"),
            string.Join(",", otherFields.Keys));
        Equal("other producer's value", "Old name", otherFields["name"].Value);
        Equal("check box export value comes from its appearance", "Yes", otherFields["agree"].Options.FirstOrDefault());
        PdfDocument theirs = PdfDocument.Open(TestData.Pdf("form-pypdf"));
        theirs.Form.SetValue("name", "Uusi nimi");
        theirs.Form.SetValue("customer.email", "a@b.fi");
        theirs.Form.SetChecked("agree", true);
        byte[] theirsFilled = theirs.ToArray();
        External.QpdfClean("filled pypdf form", theirsFilled);
        var back = PdfReader.Open(theirsFilled).GetFormFields().ToDictionary(f => f.Name);
        Equal("pypdf form: text filled", "Uusi nimi", back["name"].Value);
        Equal("pypdf form: child field filled", "a@b.fi", back["customer.email"].Value);
        Check("pypdf form: check box on", back["agree"].IsChecked);
        string? pyBack = External.PyPdf(theirsFilled, "f = pypdf.PdfReader(sys.argv[1]).get_fields()\nprint(f['name'].get('/V'), f['customer.email'].get('/V'), f['agree'].get('/V'))");
        if (pyBack is not null) Check("pypdf reads the values we filled", pyBack.Contains("Uusi nimi a@b.fi /Yes"), pyBack);
        string? theirText = External.PopplerText(theirsFilled);
        if (theirText is not null) Check("filled pypdf field shows", theirText.Contains("Uusi nimi"), theirText);

        // A stamp and a form on the same imported page.
        PdfDocument both = PdfDocument.Open(TestData.Pdf("form-pypdf"));
        both.Pages[0].AddTextField("added", 300, 300, 100, 18, "lisätty");
        both.Pages[0].Canvas.Line(0, 0, 50, 50);
        byte[] bothPdf = both.ToArray();
        External.QpdfClean("form plus added field and stamp", bothPdf);
        Equal("added field joins the imported form", 4, PdfReader.Open(bothPdf).GetFormFields().Count);
    }
}
