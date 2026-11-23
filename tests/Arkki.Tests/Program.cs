using System.Text;
using Arkki.Finvoice;
using Arkki.Pdf;
using Arkki.Pdf.Barcodes;
using Arkki.Pdf.Fonts;
using Arkki.Pdf.Layout;

// A dependency-free test runner: the library has no NuGet dependencies and neither do its tests.
int failed = 0, passed = 0;
void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; return; }
    failed++;
    Console.WriteLine($"FAIL  {name}{(detail is null ? "" : "  -> " + detail)}");
}
void Equal<T>(string name, T expected, T actual) => Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected '{expected}', got '{actual}'");

string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "Arkki.slnx"))) root = Path.GetDirectoryName(root)!;

// ---- Bank barcode: every worked example of Finance Finland's Bank Bar Code Guide 5.3 --------------
(string Iban, decimal Amount, string Reference, string? Due, string Data, int Check)[] vectors =
[
    ("FI79 4405 2020 0360 82", 4883.15m, "86851 62596 19897", "2010-06-12", "479440520200360820048831500000000868516259619897100612", 40),
    ("FI58 1017 1000 0001 22", 482.99m, "55958 22432 94671", "2012-01-31", "458101710000001220004829900000000559582243294671120131", 55),
    ("FI02 5000 4640 0013 02", 693.80m, "69 87567 20834 35364", "2011-07-24", "402500046400013020006938000000069875672083435364110724", 14),
    ("FI15 6601 0001 5306 41", 7444.54m, "7 75847 47906 47489", "2019-12-19", "415660100015306410074445400000007758474790647489191219", 63),
    ("FI16 8000 1400 0502 67", 935.85m, "78 77767 96566 28687", null, "416800014000502670009358500000078777679656628687000000", 30),
    ("FI73 3131 3001 0000 58", 0.00m, "8 68624", "2013-08-09", "473313130010000580000000000000000000000000868624130809", 68),
    ("FI83 3301 0001 1007 75", 150000.20m, "92125 37425 25398 97737", "2016-05-25", "483330100011007751500002000092125374252539897737160525", 45),
    ("FI39 3636 3002 0924 92", 1.03m, "5907 38390", "2023-03-11", "439363630020924920000010300000000000000590738390230311", 9),
    ("FI92 3939 0001 0033 91", 0.02m, "13 57914", "2099-12-24", "492393900010033910000000200000000000000001357914991224", 30),
    ("FI79 4405 2020 0360 82", 4883.15m, "RF09 8685 1625 9619 897", "2010-06-12", "579440520200360820048831509000000868516259619897100612", 74),
    ("FI58 1017 1000 0001 22", 482.99m, "RF06 5595 8224 3294 671", "2010-01-31", "558101710000001220004829906000000559582243294671100131", 31),
    ("FI02 5000 4640 0013 02", 693.80m, "RF61 6987 5672 0839", "2011-07-24", "502500046400013020006938061000000000698756720839110724", 59),
    ("FI15 6601 0001 5306 41", 7444.54m, "RF84 7758 4747 9064 7489", "2019-12-19", "515660100015306410074445484000007758474790647489191219", 16),
    ("FI16 8000 1400 0502 67", 935.85m, "RF60 7877 7679 6566 2868 7", null, "516800014000502670009358560000078777679656628687000000", 15),
    ("FI73 3131 3001 0000 58", 0.00m, "RF10 8686 24", "2013-08-09", "573313130010000580000000010000000000000000868624130809", 91),
    ("FI83 3301 0001 1007 75", 150000.20m, "RF71 9212 5374 2525 3989 7737", "2016-05-25", "583330100011007751500002071092125374252539897737160525", 80),
    ("FI39 3636 3002 0924 92", 1.03m, "RF66 5907 3839 0", "2023-03-11", "539363630020924920000010366000000000000590738390230311", 10),
    ("FI92 3939 0001 0033 91", 0.02m, "RF95 1357 914", "2099-12-24", "592393900010033910000000295000000000000001357914991224", 33),
];
for (int i = 0; i < vectors.Length; i++)
{
    var v = vectors[i];
    string? data = BankBarcode.TryBuild(v.Iban, v.Amount, v.Reference, v.Due is null ? null : DateOnly.Parse(v.Due));
    Equal($"barcode data #{i + 1}", v.Data, data);
    if (data is null) continue;
    int[] symbols = Code128.EncodeSetC(data);
    // The check value printed for version 5 example 3 does not follow from its own data string
    // under ISO/IEC 15417 (the other 17 examples do), so that one value is not asserted.
    if (i != 11) Equal($"barcode check #{i + 1}", v.Check, symbols[^2]);
    Equal($"barcode symbol count #{i + 1}", 30, symbols.Length);
    Equal($"barcode modules #{i + 1}", 332, Code128.TotalModules(Code128.ToModules(symbols)));
    Check($"iban valid #{i + 1}", Iban.IsValid(v.Iban));
    Check($"reference valid #{i + 1}", PaymentReference.IsValid(v.Reference), v.Reference);
}
Equal("amount too large is encoded as zeros", "00000000", BankBarcode.TryBuild("FI2112345600000785", 1_000_000m, "1232", null)![17..25]);
Check("alphanumeric RF reference gives no barcode", BankBarcode.TryBuild("FI2112345600000785", 1m, "RF712348231A", null) is null);
Check("foreign IBAN gives no barcode", BankBarcode.TryBuild("SE4550000000058398257466", 1m, "1232", null) is null);

// ---- References, IBAN, formats ---------------------------------------------------------------------
Equal("national check digit", "1232", PaymentReference.CreateNational("123"));
Check("invalid national reference", !PaymentReference.IsValidNational("1233"));
Equal("national to RF", "RF09868516259619897", PaymentReference.ToRf("868516259619897"));
Equal("national print format", "86851 62596 19897", PaymentReference.Format("868516259619897"));
Equal("national print format, uneven", "69 87567 20834 35364", PaymentReference.Format("0006987567208343 5364"));
Equal("RF print format", "RF60 7877 7679 6566 2868 7", PaymentReference.Format("RF60787776796566286 87"));
Equal("iban format", "FI21 1234 5600 0007 85", Iban.Format("fi2112345600000785"));
Check("iban invalid", !Iban.IsValid("FI2112345600000786"));
Equal("money", "1 234 567,80", FiFormat.Money(1234567.8m));
Equal("money negative", "-0,50", FiFormat.Money(-0.5m));
Equal("number trims", "2,5", FiFormat.Number(2.500m, 0, 3));
Equal("date", "1.2.2026", FiFormat.Date(new DateOnly(2026, 2, 1)));

// ---- Code 128 pattern table ------------------------------------------------------------------------
for (int v = 0; v <= 106; v++)
    Equal($"code128 pattern {v} width", v == 106 ? 13 : 11, Code128.TotalModules(Code128.ToModules([v])));

// ---- Finvoice reader -------------------------------------------------------------------------------
string xmlPath = Path.Combine(root, "samples", "data", "finvoice-sample.xml");
FinvoiceInvoice inv = FinvoiceReader.Load(xmlPath);
Equal("finvoice version", "3.0", inv.Version);
Equal("finvoice seller", "Esimerkki Konepaja Oy", inv.Seller.Name);
Equal("finvoice buyer town", "Jyväskylä", inv.Buyer.Town);
Equal("finvoice rows", 44, inv.Rows.Count);
Equal("finvoice due date", new DateOnly(2026, 12, 7), inv.DueDate);
Equal("finvoice iban", "FI2112345600000785", inv.PaymentIban);
Check("finvoice reference valid", inv.EpiReference is not null && PaymentReference.IsValid(inv.EpiReference));
Equal("finvoice row sum equals total", inv.TotalVatExcluded, inv.Rows.Sum(r => r.VatExcludedAmount ?? 0));
Equal("finvoice vat sum equals total", inv.TotalVatIncluded, inv.VatSpecifications.Sum(s => s.BaseAmount + s.VatAmount));

// The same message in ISO-8859-15, the encoding many Finvoice senders use.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string latin = File.ReadAllText(xmlPath).Replace("encoding=\"UTF-8\"", "encoding=\"ISO-8859-15\"");
FinvoiceInvoice inv15 = FinvoiceReader.Load(new MemoryStream(Encoding.GetEncoding("ISO-8859-15").GetBytes(latin)));
Equal("finvoice ISO-8859-15", "Åke Öhman", inv15.Buyer.ContactPerson);

// ---- Fonts and PDF output --------------------------------------------------------------------------
var regular = TrueTypeFont.Load(File.ReadAllBytes(Path.Combine(root, "samples", "fonts", "LiberationSans-Regular.ttf")));
var bold = TrueTypeFont.Load(File.ReadAllBytes(Path.Combine(root, "samples", "fonts", "LiberationSans-Bold.ttf")));
Equal("font units per em", 2048, regular.UnitsPerEm);
Check("font has Finnish, Estonian, Latvian and Lithuanian letters", "äöåÄÖÅõšžŠŽāēģķļņūąčęėįų€".All(c => regular.HasGlyph(c)));
Equal("font postscript name", "LiberationSans", regular.PostScriptName);

var theme = new InvoiceTheme
{
    RegularFont = regular, BoldFont = bold,
    Logo = PdfImage.Load(File.ReadAllBytes(Path.Combine(root, "samples", "data", "logo.png"))),
    CreationDate = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(2)),
};
PdfDocument doc = FinvoiceRenderer.Render(inv, theme);
byte[] pdf = doc.ToArray();
Check("pdf header", pdf.AsSpan(0, 8).SequenceEqual("%PDF-1.7"u8));
Check("pdf trailer", Encoding.Latin1.GetString(pdf, pdf.Length - 6, 6) == "%%EOF\n");
Check("invoice is multi-page", doc.Pages.Count >= 2, doc.Pages.Count.ToString());
Check("output is deterministic", pdf.AsSpan().SequenceEqual(FinvoiceRenderer.RenderToArray(inv, theme)));
Check("subset keeps the file small", pdf.Length < 150_000, pdf.Length + " bytes");

// Parallel rendering shares the parsed fonts and must give identical bytes on every thread.
var results = new byte[16][];
Parallel.For(0, results.Length, i => results[i] = FinvoiceRenderer.RenderToArray(inv, theme));
Check("parallel renders are identical", results.All(r => r.AsSpan().SequenceEqual(pdf)));

// ---- Layout ----------------------------------------------------------------------------------------
var layoutDoc = new PdfDocument();
var family = new FontFamily(layoutDoc.AddFont(regular), layoutDoc.AddFont(bold));
var style = new TextStyle(family, 10);
var paragraph = new TextBlock(string.Join(" ", Enumerable.Repeat("sana", 400)), style);
LayoutResult partial = paragraph.Layout(200, 50);
Equal("text splits across pages", LayoutStatus.Partial, partial.Status);
Equal("text fragment height is whole lines", 4 * style.LineAdvance, partial.Fragment!.Height);
Equal("keep-together refuses to split", LayoutStatus.None, paragraph.KeepTogether().Layout(200, 50).Status);
var table = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Pt(60), HAlign.Right)]);
table.SetHeader(style, "A", "B");
for (int i = 0; i < 100; i++) table.AddRow(style, "rivi " + i, i.ToString());
new FlowDocument { Content = table }.RenderTo(layoutDoc);
Check("table flows over several pages", layoutDoc.Pages.Count > 1, layoutDoc.Pages.Count.ToString());

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
