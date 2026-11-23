# Arkki

Fast, dependency-free PDF generation for .NET, written in C#. No browser engine, no native
binaries, no NuGet dependencies. Licensed under the Mozilla Public License 2.0.

> Working name. Status: early (0.1). The first milestone is done: a multi-page invoice rendered
> from a Finvoice 3.0 file, with a company logo, the Finnish bank barcode and the credit
> transfer form (tilisiirtolomake). See [`docs/sample-invoice.pdf`](docs/sample-invoice.pdf).

## What is in the box

| Project | Contents |
|---|---|
| `Arkki.Pdf` | PDF writer, TrueType embedding with subsetting, PNG/JPEG images, vector Code 128, an immediate-mode canvas and a paginating layout engine |
| `Arkki.Finvoice` | Finvoice 3.0 reader, IBAN and reference number validation, bank barcode data (versions 4 and 5), the credit transfer form and an invoice renderer |

## Quick start

```csharp
using Arkki.Finvoice;
using Arkki.Pdf;
using Arkki.Pdf.Fonts;

// Parse fonts and the logo once; they are immutable and shared by all renders and threads.
var theme = new InvoiceTheme
{
    RegularFont = TrueTypeFont.Load(File.ReadAllBytes("LiberationSans-Regular.ttf")),
    BoldFont = TrueTypeFont.Load(File.ReadAllBytes("LiberationSans-Bold.ttf")),
    Logo = PdfImage.Load(File.ReadAllBytes("logo.png")),
};

FinvoiceInvoice invoice = FinvoiceReader.Load("invoice.xml");
File.WriteAllBytes("invoice.pdf", FinvoiceRenderer.RenderToArray(invoice, theme));
```

Building your own documents uses the same layout elements the invoice is made of:

```csharp
var doc = new PdfDocument();
var fonts = new FontFamily(doc.AddFont(regular), doc.AddFont(bold));
var body = new TextStyle(fonts, 10);

var table = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Mm(30), HAlign.Right)])
    .SetHeader(body with { Bold = true }, "Item", "Amount");
table.AddRow(body, "Machining", "258,00");

new FlowDocument
{
    Header = page => new TextBlock($"Page {page.PageNumber} ({page.PageCount})", body, HAlign.Right),
    Content = new Column(new TextBlock("Hello", body with { Size = 18, Bold = true }), table.Padding(4)),
}.RenderTo(doc);

doc.Save(stream);
```

## Build, test, run

Requires the .NET 10 SDK. Nothing is restored from NuGet.

```
dotnet build Arkki.slnx -c Release
dotnet run -c Release --project tests/Arkki.Tests
dotnet run -c Release --project samples/Arkki.Samples.Invoice
```

The sample writes `samples/output/invoice.pdf` and prints a render time. On a two-core cloud VM the
three-page, 44-row sample invoice takes about 2 ms with `CompressionLevel.Fastest` and about 5 ms
with `Optimal`, single-threaded, at roughly 100 kB including the logo and two subset fonts.

## Verification

- `tests/Arkki.Tests` checks all 18 worked examples of Finance Finland's Bank Bar Code Guide 5.3,
  reference and IBAN validation, the Finvoice reader (UTF-8 and ISO-8859-15), deterministic
  output and parallel rendering.
- `tools/measure_giro.py` renders the form at 600 dpi and measures every line and the barcode
  against the specification. See [`docs/DESIGN.md`](docs/DESIGN.md) for the numbers.

## License

MPL-2.0, see [`LICENSE`](LICENSE). The Liberation Sans fonts under `samples/fonts` are
licensed separately under the SIL Open Font License 1.1.
