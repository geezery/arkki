# Arkki

Fast, dependency-free PDF generation for .NET, written in C#. No browser engine, no native
binaries, no NuGet dependencies. Licensed under the Mozilla Public License 2.0.

> Working name. Status: early (0.1). The library is `Arkki.Pdf`: a general-purpose PDF writer
> and layout engine with no knowledge of any particular document type. The first milestone,
> proving that it can reproduce a regulated form exactly, is a sample: a multi-page Finvoice
> invoice with a logo, the Finnish bank barcode and the credit transfer form. See
> [`docs/sample-invoice.pdf`](docs/sample-invoice.pdf).

## What is in the box

| Project | Contents |
|---|---|
| `src/Arkki.Pdf` | The library. PDF writer, TrueType embedding with subsetting, PNG/JPEG images, vector Code 128, an immediate-mode canvas and a paginating layout engine |
| `samples/Arkki.Samples.Finvoice` | Sample, not shipped. Finvoice 3.0 reader, IBAN and reference validation, bank barcode, credit transfer form and an invoice renderer. Copy it into your application and adapt it |
| `samples/Arkki.Samples.Invoice` | Console program that renders the sample invoice and times it |

## Quick start

A document is a tree of layout elements rendered onto pages:

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

## Merging PDF files

```csharp
using Arkki.Pdf.Reading;

// Whole files, in order. Pages are copied, not re-rendered.
byte[] batch = PdfDocument.Merge(invoicePdfs).ToArray();

// Or pick pages, from files written by any producer.
var doc = new PdfDocument();
doc.ImportPages(PdfReader.Open(File.ReadAllBytes("agreement.pdf")));
doc.ImportPage(PdfReader.Open(File.ReadAllBytes("appendix.pdf")), 0);
```

The reader handles cross-reference tables and streams, object streams, incremental updates and
damaged cross-references. Encrypted files are rejected.

## Small files

- Fonts are subset to the glyphs a document uses.
- PNG images are stored losslessly in the cheapest form that fits: grey instead of RGB, a palette
  for up to 256 colours, PNG row prediction when it helps, and no soft mask for opaque alpha.
  `PdfImage.Load(data, maxPixelWidth, maxPixelHeight)` shrinks an image larger than it will
  print; at 300 dpi a 60 mm wide logo needs about 710 pixels. JPEG data is embedded as is.
- An image is embedded once however many pages show it.
- Merging stores identical objects once, so a batch of invoices with the same logo carries the
  logo once, and streams that a producer left uncompressed are compressed.

## The invoice sample

```csharp
using Arkki.Pdf;
using Arkki.Pdf.Fonts;
using Arkki.Samples.Finvoice;

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

Its labels are Finnish by default and are loaded from JSON
(`samples/Arkki.Samples.Finvoice/Invoices/invoice-texts-fi.json`), so another language is one
file passed to `InvoiceTexts.Load` and set as `InvoiceTheme.Texts`.

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

- `tests/Arkki.Tests` reads and merges files written by Ghostscript, LibreOffice, qpdf and pypdf
  (see `tests/Arkki.Tests/Data/Pdf`), checks file sizes, and checks all 18 worked examples of Finance Finland's Bank Bar Code Guide 5.3,
  reference and IBAN validation, the Finvoice reader (UTF-8 and ISO-8859-15), deterministic
  output and parallel rendering.
- `tools/measure_giro.py` renders the form at 600 dpi and measures every line and the barcode
  against the specification. See [`docs/DESIGN.md`](docs/DESIGN.md) for the numbers.

## License

MPL-2.0, see [`LICENSE`](LICENSE). The Liberation Sans fonts under `samples/fonts` are
licensed separately under the SIL Open Font License 1.1.
