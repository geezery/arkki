# Getting started

## Install

Arkki needs .NET 10.

```
dotnet add package Arkki.Pdf --prerelease
```

For digital signatures, add the second package too:

```
dotnet add package Arkki.Pdf.Signing --prerelease
```

## Bring your own fonts

Arkki never looks for system fonts. You pass a font in as bytes, and Arkki
embeds it in the file, so the document looks the same on every machine and in
every viewer. Any TrueType font (.ttf) works, and so do OpenType fonts with CFF
outlines (.otf).

Bold is its own font file, and so is italic. A `FontFamily` ties them together:

```csharp
var doc = new PdfDocument();
var fonts = new FontFamily(
    doc.AddFont(File.ReadAllBytes("Inter-Regular.ttf")),
    doc.AddFont(File.ReadAllBytes("Inter-Bold.ttf")));
var body = new TextStyle(fonts, 10);
```

If you make many documents, parse each font once and share it. A
`TrueTypeFont` is immutable and safe to use from any number of documents and
threads:

```csharp
TrueTypeFont regular = TrueTypeFont.Load(File.ReadAllBytes("Inter-Regular.ttf"));

var first = new PdfDocument();
PdfFont inFirst = first.AddFont(regular);
var second = new PdfDocument();
PdfFont inSecond = second.AddFont(regular);
```

## Your first document

A document is a tree of layout elements. `FlowDocument` flows the tree onto as
many pages as it needs:

```csharp
var doc = new PdfDocument();
doc.Info.Title = "Order 1187";
var fonts = new FontFamily(
    doc.AddFont(File.ReadAllBytes("Inter-Regular.ttf")),
    doc.AddFont(File.ReadAllBytes("Inter-Bold.ttf")));
var body = new TextStyle(fonts, 10);

var table = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Mm(30), HAlign.Right)])
    .SetHeader(body with { Bold = true }, "Item", "Amount");
table.AddRow(body, "Machining", "258,00");

new FlowDocument
{
    Header = page => new TextBlock($"Page {page.PageNumber} ({page.PageCount})", body, HAlign.Right),
    Content = new Column(
        new TextBlock("Order 1187", body with { Size = 18, Bold = true }),
        new TextBlock("Thank you for your order.", body),
        table) { Spacing = 8 },
}.RenderTo(doc);

using var file = File.Create("order.pdf");
doc.Save(file);              // or doc.ToArray()
```

The namespaces used on these pages:

```csharp
using Arkki.Pdf;            // PdfDocument, PdfPage, PdfCanvas, PdfFont, PdfImage, PageSize, Unit, Color
using Arkki.Pdf.Layout;     // FlowDocument, TextBlock, Table, Column, Row ...
using Arkki.Pdf.Fonts;      // TrueTypeFont
using Arkki.Pdf.Barcodes;   // Code128, Ean13, QrCode
using Arkki.Pdf.Reading;    // PdfReader
using Arkki.Pdf.Signing;    // PdfSigner, PdfSignatureVerifier (the Arkki.Pdf.Signing package)
```

## Where to next

- [[Layout]] for text, lists and tables.
- [[Forms]] to fill a form someone sent you, or to make one.
- [[Editing and Merging]] to work on files that already exist.
- The [samples folder](https://github.com/geezery/arkki/tree/main/samples)
  shows a complete Finnish Finvoice invoice with the bank barcode and the
  credit transfer form. It is an example to copy, not part of the library.
