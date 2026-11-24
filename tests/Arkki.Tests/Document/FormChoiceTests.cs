using System.Text;
using Arkki.Pdf;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Radio buttons, combo boxes and list boxes: created here, read back, filled in this and another producer's file.</summary>
internal static class FormChoiceTests
{
    private static readonly string[] Countries = ["Suomi", "Sverige", "Norge"];
    private static readonly string[] Extras = ["Gift wrap", "Insurance", "Express"];

    public static void Run()
    {
        Created();
        RemovedPage();
        Foreign();
        Arguments();
    }

    private static PdfDocument NewForm()
    {
        var doc = new PdfDocument();
        PdfPage page = doc.AddPage(), second = doc.AddPage();
        page.AddRadioButton("delivery", "post", 72, 72, 12, selected: true);
        page.AddRadioButton("delivery", "courier", 72, 92, 12);
        second.AddRadioButton("delivery", "pickup", 72, 72, 12);
        page.AddComboBox("country", 72, 130, 150, 18, Countries, "Suomi");
        page.AddComboBox("title", 72, 160, 150, 18, ["Mr", "Ms"], "Custom", editable: true);
        page.AddListBox("size", 72, 190, 150, 40, ["S", "M", "L"], "M");
        page.AddListBox("extras", 72, 240, 150, 40, Extras, multiSelect: true);
        doc.Form.SetValues("extras", ["Insurance", "Gift wrap"]);
        return doc;
    }

    private static void Created()
    {
        PdfDocument doc = NewForm();
        Equal("a radio group is one field", "delivery,country,title,size,extras", string.Join(",", doc.Form.Fields.Select(f => f.Name)));
        byte[] pdf = doc.ToArray();
        Check("a form with choices is deterministic", pdf.AsSpan().SequenceEqual(NewForm().ToArray()));
        External.QpdfClean("form with radio buttons and choices", pdf);

        var fields = PdfReader.Open(pdf).GetFormFields().ToDictionary(f => f.Name);
        Equal("fields read back", 5, fields.Count);
        PdfFormField delivery = fields["delivery"];
        Check("radio group reads back as one", delivery.Kind == PdfFieldKind.RadioButton && delivery.IsChecked);
        Equal("radio group's value", "post", delivery.Value);
        Equal("radio group's buttons, across pages", "post,courier,pickup", string.Join(",", delivery.Options));
        string raw = Encoding.Latin1.GetString(pdf);
        Equal("one button shows as chosen", 1, Count(raw, "/AS /post"));
        Equal("the other buttons show as off", 2, Count(raw, "/AS /Off"));

        PdfFormField country = fields["country"];
        Check("combo box reads back", country.Kind == PdfFieldKind.Choice && country.IsComboBox && !country.IsEditable && !country.IsMultiSelect);
        Equal("combo box value", "Suomi", country.Value);
        Equal("combo box options", "Suomi,Sverige,Norge", string.Join(",", country.Options));
        Check("editable combo box keeps text that is no option", fields["title"].IsEditable && fields["title"].Value == "Custom");
        Check("list box reads back", fields["size"].Kind == PdfFieldKind.Choice && !fields["size"].IsComboBox && fields["size"].Value == "M");
        Equal("a single value is the only one of Values", "M", string.Join(",", fields["size"].Values));
        Check("multi-select list box reads back", fields["extras"].IsMultiSelect);
        Equal("every chosen value reads back", "Insurance,Gift wrap", string.Join(",", fields["extras"].Values));
        Check("chosen rows are listed in order", raw.Contains("/I [0 1 ]"), "no /I [0 1 ]");

        string? py = External.PyPdf(pdf, "f = pypdf.PdfReader(sys.argv[1]).get_fields()\nprint(f['delivery'].get('/V'), f['country'].get('/V'), f['size'].get('/V'), f['extras'].get('/V'))");
        if (py is not null) Check("pypdf reads the choices", py.Contains("/post Suomi M ['Insurance', 'Gift wrap']"), py);
        string? shown = External.PopplerText(pdf);
        if (shown is not null) Check("combo and list values show on the page", shown.Contains("Suomi") && shown.Contains("Insurance") && shown.Contains("Custom"), shown);

        // Rendered: the chosen button has its dot, the other is an empty ring.
        if (External.Render(pdf) is var (w, _, rgb))
        {
            int Red(int x, int y) => rgb[(y * w + x) * 3];
            Check("the chosen radio button has a dot", Red(78, 78) < 80, $"red {Red(78, 78)}");
            Check("the other radio button is empty", Red(78, 98) > 200, $"red {Red(78, 98)}");
            Check("the radio button has a ring", Red(73, 78) < 230 || Red(72, 78) < 230, $"red {Red(73, 78)}");
            Check("the chosen list row is highlighted", Red(200, 254) < 200 && Red(200, 198) > 240, $"red {Red(200, 254)} and {Red(200, 198)}");
        }

        // Values set before saving.
        PdfDocument early = NewForm();
        early.Form.SetValue("delivery", "pickup");
        early.Form.SetValue("country", "Norge");
        var earlyFields = PdfReader.Open(early.ToArray()).GetFormFields().ToDictionary(f => f.Name);
        Check("values set before saving are saved", earlyFields["delivery"].Value == "pickup" && earlyFields["country"].Value == "Norge");

        // Fill the saved form.
        PdfDocument fill = PdfDocument.Open(pdf);
        fill.Form.SetValue("delivery", "courier");
        fill.Form.SetValue("country", "Norge");
        fill.Form.SetValue("title", "Dr");
        fill.Form.SetValue("size", "L");
        fill.Form.SetValues("extras", ["Express"]);
        Throws<ArgumentException>("a combo box takes only its options", () => fill.Form.SetValue("country", "Danmark"));
        Throws<ArgumentException>("a radio group takes only its buttons", () => fill.Form.SetValue("delivery", "drone"));
        Throws<InvalidOperationException>("a single-select list takes one value", () => fill.Form.SetValues("size", ["S", "M"]));
        Throws<InvalidOperationException>("SetValues is for choices", () => fill.Form.SetValues("delivery", ["post"]));
        byte[] filled = fill.ToArray();
        External.QpdfClean("filled choices", filled);
        var after = PdfReader.Open(filled).GetFormFields().ToDictionary(f => f.Name);
        Equal("filled radio group", "courier", after["delivery"].Value);
        Equal("filled combo box", "Norge", after["country"].Value);
        Equal("filled editable combo box", "Dr", after["title"].Value);
        Equal("filled list box", "L", after["size"].Value);
        Equal("filled multi-select list", "Express", string.Join(",", after["extras"].Values));
        string filledRaw = Encoding.Latin1.GetString(filled);
        Check("the filled combo box shows its value", filledRaw.Contains("(Norge) Tj"));
        Equal("the chosen button changed", 1, Count(filledRaw, "/AS /courier"));
        string? filledText = External.PopplerText(filled);
        if (filledText is not null) Check("filled choices show on the page", filledText.Contains("Norge") && filledText.Contains("Dr"), filledText);

        // Clearing a choice, and turning every radio button off.
        PdfDocument clear = PdfDocument.Open(filled);
        clear.Form.SetValues("extras", []);
        clear.Form.SetValue("delivery", "Off");
        byte[] cleared = clear.ToArray();
        External.QpdfClean("cleared choices", cleared);
        var none = PdfReader.Open(cleared).GetFormFields().ToDictionary(f => f.Name);
        Check("a cleared list has no value", none["extras"].Value is null && none["extras"].Values.Count == 0);
        Check("a radio group can be turned off", !none["delivery"].IsChecked);
        Equal("an untouched field keeps its value", "Norge", none["country"].Value);

        // Encrypted: the option and value strings are encrypted with the rest.
        PdfDocument secret = NewForm();
        secret.Encryption = new PdfEncryption("open");
        byte[] locked = secret.ToArray();
        External.QpdfClean("encrypted form with choices", locked, "open");
        var unlocked = PdfReader.Open(locked, "open").GetFormFields().ToDictionary(f => f.Name);
        Check("choices survive encryption", unlocked["country"].Value == "Suomi" && unlocked["delivery"].Value == "post"
            && string.Join(",", unlocked["extras"].Options) == string.Join(",", Extras));
    }

    private static void RemovedPage()
    {
        // The group lists only the buttons of pages still in the document.
        PdfDocument doc = NewForm();
        doc.Form.SetValue("delivery", "pickup");
        doc.RemovePageAt(1);
        byte[] pdf = doc.ToArray();
        External.QpdfClean("radio group after its page was removed", pdf);
        PdfFormField delivery = PdfReader.Open(pdf).GetFormFields().Single(f => f.Name == "delivery");
        Equal("buttons of a removed page are gone", "post,courier", string.Join(",", delivery.Options));
        Check("a choice on a removed page is no choice", !delivery.IsChecked);
    }

    /// <summary>A form as other producers write it: option pairs, a radio group under a parent, chosen rows in /I.</summary>
    private static void Foreign()
    {
        byte[] pdf = BuildPdf(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R 8 0 R 9 0 R] /DA (/Helv 0 Tf 0 g) >> >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Annots [6 0 R 7 0 R 8 0 R 9 0 R] >>",
            "<< /Type /XObject /Subtype /Form /BBox [0 0 12 12] /Length 0 >>\nstream\n\nendstream",
            "<< /FT /Btn /Ff 49152 /T (plan) /V /basic /Kids [6 0 R 7 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /Parent 5 0 R /Rect [20 260 32 272] /AS /basic /AP << /N << /basic 4 0 R /Off 4 0 R >> >> >>",
            "<< /Type /Annot /Subtype /Widget /Parent 5 0 R /Rect [20 240 32 252] /AS /Off /AP << /N << /pro 4 0 R /Off 4 0 R >> >> >>",
            "<< /Type /Annot /Subtype /Widget /FT /Ch /Ff 131072 /T (lang) /Opt [[(fi) (Suomi)] [(sv) (Svenska)]] /V (fi) /DA (/Helv 9 Tf 0 g) /Rect [20 200 140 216] >>",
            "<< /Type /Annot /Subtype /Widget /FT /Ch /Ff 2097152 /T (days) /Opt [(Mon) (Tue) (Wed)] /V [(Mon) (Wed)] /I [0 2] /Rect [20 120 140 180] >>");
        External.QpdfClean("hand-made form", pdf);

        var fields = PdfReader.Open(pdf).GetFormFields().ToDictionary(f => f.Name);
        Equal("foreign radio group", "basic|basic,pro", fields["plan"].Value + "|" + string.Join(",", fields["plan"].Options));
        Equal("option pairs show their visible text", "Suomi,Svenska", string.Join(",", fields["lang"].Options));
        Equal("the value is the stored one", "fi", fields["lang"].Value);
        Check("foreign combo box", fields["lang"].IsComboBox);
        Equal("foreign multi-select values", "Mon,Wed", string.Join(",", fields["days"].Values));
        Check("foreign multi-select list", fields["days"].IsMultiSelect && !fields["days"].IsComboBox);

        PdfDocument doc = PdfDocument.Open(pdf);
        doc.Form.SetValue("plan", "pro");
        doc.Form.SetValue("lang", "Svenska"); // by the visible text; "sv" is stored
        doc.Form.SetValues("days", ["Tue"]);
        Throws<ArgumentException>("foreign list takes only its options", () => doc.Form.SetValue("days", "Sun"));
        byte[] filled = doc.ToArray();
        External.QpdfClean("filled hand-made form", filled);
        var after = PdfReader.Open(filled).GetFormFields().ToDictionary(f => f.Name);
        Equal("foreign radio group filled", "pro", after["plan"].Value);
        Equal("visible text is stored as its value", "sv", after["lang"].Value);
        Equal("foreign list filled", "Tue", string.Join(",", after["days"].Values));
        string raw = Encoding.Latin1.GetString(filled);
        Check("the combo box shows the visible text", raw.Contains("(Svenska) Tj"));
        Check("the list shows its options", raw.Contains("(Tue) Tj"));
        Check("the old chosen rows are replaced", !raw.Contains("/I [0 2]"));
        string? py = External.PyPdf(filled, "f = pypdf.PdfReader(sys.argv[1]).get_fields()\nprint(f['plan'].get('/V'), f['lang'].get('/V'), f['days'].get('/V'))");
        if (py is not null) Check("pypdf reads the filled hand-made form", py.Contains("/pro sv Tue"), py);

        // Filling by the stored value works too.
        PdfDocument byStored = PdfDocument.Open(pdf);
        byStored.Form.SetValue("lang", "sv");
        Equal("a stored value is accepted", "sv", PdfReader.Open(byStored.ToArray()).GetFormFields().Single(f => f.Name == "lang").Value);
    }

    private static void Arguments()
    {
        var doc = new PdfDocument();
        PdfPage page = doc.AddPage();
        page.AddTextField("name", 10, 10, 100, 18);
        page.AddRadioButton("group", "a", 10, 40, 12);
        Throws<ArgumentException>("radio value: Off is taken", () => page.AddRadioButton("group", "Off", 10, 60, 12));
        Throws<ArgumentException>("radio value: a plain name", () => page.AddRadioButton("group", "two words", 10, 60, 12));
        Throws<ArgumentException>("radio value: once per group", () => page.AddRadioButton("group", "a", 10, 60, 12));
        Throws<ArgumentException>("radio group: the name is another field's", () => page.AddRadioButton("name", "a", 10, 60, 12));
        Throws<ArgumentException>("combo box: the name is taken", () => page.AddComboBox("group", 10, 80, 100, 18, Countries));
        Throws<ArgumentException>("combo box: the value is an option", () => page.AddComboBox("c1", 10, 80, 100, 18, Countries, "Danmark"));
        Throws<ArgumentException>("combo box: options are distinct", () => page.AddComboBox("c2", 10, 80, 100, 18, ["a", "a"]));
        Throws<ArgumentNullException>("list box: options are given", () => page.AddListBox("l1", 10, 80, 100, 40, null!));
        Throws<ArgumentException>("list box: the value is an option", () => page.AddListBox("l2", 10, 80, 100, 40, Countries, "Danmark"));
        Equal("a refused field is not added", "name,group", string.Join(",", doc.Form.Fields.Select(f => f.Name)));
        PdfFormField group = page.AddRadioButton("group", "b", 10, 60, 12, selected: true, readOnly: true);
        Check("the group is returned with its new state", group.Value == "b" && group.ReadOnly && group.Options.Count == 2);
        Check("a read-only group reads back", PdfReader.Open(doc.ToArray()).GetFormFields().Single(f => f.Name == "group").ReadOnly);
    }

    private static int Count(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>A PDF file of these objects, numbered from 1, the first being the catalog.</summary>
    private static byte[] BuildPdf(params string[] objects)
    {
        var sb = new StringBuilder("%PDF-1.7\n");
        var offsets = new int[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i] = sb.Length;
            sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (int o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
