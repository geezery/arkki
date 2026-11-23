# Design notes

## Goals

1. Fast: a typical invoice in single-digit milliseconds, with no global state so documents can
   be rendered in parallel.
2. No engines underneath: no browser, no native libraries, no NuGet dependencies. Native AOT
   and trimming friendly.
3. Exact output for any document, including regulated forms. The library knows nothing about
   invoices or any other document type; the Finnish invoice with its credit transfer form and
   bank barcode is a sample that proves the precision.
4. No dependencies outside the .NET base class library.

## Layers

```
(sample)         Arkki.Samples.Finvoice: reader -> model -> renderer, giro form, bank barcode
Arkki.Pdf        Layout:   Element tree -> Fragments -> pages          (FlowDocument)
                 Canvas:   immediate-mode drawing, top-left origin     (PdfCanvas)
                 Objects:  fonts, images, barcodes                     (PdfFont, PdfImage, barcodes)
                 Writer:   objects, streams, xref                      (PdfWriter, internal)
                 Reader:   xref, objects, page tree of existing files  (PdfReader)
                 Merging:  copy pages and what they reference          (PageImporter, internal)
```

- **Writer.** Objects are serialized straight to the output stream; only offsets are kept.
- **Fonts.** `TrueTypeFont` is parsed once and is immutable. Each document tracks the glyphs it
  uses and embeds a subset as a CID font (Identity-H) with a ToUnicode map, so text stays
  searchable. Subsetting keeps glyph ids, so content streams need no re-mapping.
- **Images.** `PdfImage` is decoded and compressed once, then reused by every document. A PNG
  is stored in the smallest lossless form found: grey for grey RGB, an indexed palette for up to
  256 colours, and Flate with per-row PNG prediction only when that beats plain Flate.
- **Reader.** `PdfReader` parses the cross-reference (tables, streams, `/Prev` chains, hybrid
  files) and falls back to scanning for `n g obj` when it is missing or wrong. Objects are parsed
  on demand; stream data is never decoded except for cross-reference and object streams.
- **Merging.** Imported pages are written by `PageImporter`, which copies each referenced object
  once per source and writes objects with identical bytes once in total, keyed by SHA-256. The
  logo and fonts shared by a batch of invoices are therefore stored once. References to pages
  that are not imported (links, outlines) become null so they do not pull the rest of the source
  in. Thumbnails and application private data (`/Thumb`, `/PieceInfo`) are dropped.
- **Layout.** Every element answers one question: "given this width and height, what fits?"
  The answer is `Full`, `Partial` (with a remainder for the next page) or `None`. Layout
  produces fragments; drawing happens after pagination, so "page 2 (5)" needs no second pass.

## Source layout

Files are grouped by feature, one public type per file. Namespaces are coarser than folders:
each package has its root namespace, and `Arkki.Pdf` adds `Fonts`, `Barcodes`, `Layout` and
`Reading`. Folders below those never add a namespace, so one `using` covers a feature area.

```
src/Arkki.Pdf/
  Document/      PdfDocument, PdfPage, PdfInfo                      (Arkki.Pdf)
  Drawing/       PdfCanvas, Color, Unit, PageSize                   (Arkki.Pdf)
  Fonts/         TrueTypeFont (Arkki.Pdf.Fonts), PdfFont (Arkki.Pdf)
  Images/        PdfImage, lossless image encoder                   (Arkki.Pdf)
  Barcodes/      BarcodeMatrix, IBarcodeSymbology, Code128, Ean13, QrCode (Arkki.Pdf.Barcodes)
  Layout/        element tree and pagination                        (Arkki.Pdf.Layout)
    Core/        Element, Fragment, LayoutResult, Len, modifiers
    Text/        TextBlock, TextStyle, FontFamily, line breaking
    Containers/  Column, Row, Box, KeepTogether
    Content/     Spacer, Rule, ImageBox, BarcodeBox, PageBreak, Custom
    Tables/      Table, TableColumn, ColumnSpan
    Pagination/  FlowDocument, Margins, PageContext
  Reading/       PdfReader, parser, object model                    (Arkki.Pdf.Reading)
  Merging/       PageImporter                                       (Arkki.Pdf.Merging, internal)
  Internal/      PDF object writer, byte buffer
samples/Arkki.Samples.Finvoice/   not part of the library          (Arkki.Samples.Finvoice)
  Payments/      Iban, PaymentReference, BankBarcode
  Formatting/    FiFormat, JSON text loader
  Messages/      Finvoice model and FinvoiceReader
  Giro/          credit transfer form, GiroTexts + giro-texts.json
  Invoices/      FinvoiceRenderer, InvoiceTheme, InvoiceTexts + invoice-texts-fi.json
tests/Arkki.Tests/  one file per feature; sample tests under Samples/
```

Printed texts are data, not code. In the sample, the invoice labels and the form's pre-printed wording are
embedded JSON files loaded into `InvoiceTexts` and `GiroTexts`; a language is one more JSON file
passed to `InvoiceTexts.Load`, and single labels can be changed with a `with` expression. Unknown
or missing keys fail at load time.

## API model: a data tree with chainable modifiers, not nested lambdas

Three styles were considered.

| Style | Pros | Cons |
|---|---|---|
| Imperative canvas only | Fastest, total control | No pagination; every document is hand-positioned |
| Nested-lambda fluent DSL (`page.Content().Column(c => c.Item().Row(r => ...))`) | Reads top-down, discoverable | Closures allocate; the tree only exists while the lambdas run, so it cannot be stored, inspected or produced from a template; awkward to debug |
| Declarative element tree with chainable modifiers | The tree is plain data: testable, cacheable, buildable from JSON/DB templates; modifiers still chain (`table.Padding(4).KeepTogether()`) | Slightly more `new` in user code |

Arkki uses the third style, with the canvas available underneath (`Custom` element, page
overlays) for fixed forms such as the giro. The deciding reason is the ERP use case: tenant
specific invoice templates must be storable as data, which a lambda DSL cannot express. A
lambda-style builder can be added later as a thin layer that produces the same tree.

## Sample: credit transfer form, specification and measured result

Sources: Finance Finland, *Tilisiirto-opas* section 2.16 (form model and dimensions) and
*Pankkiviivakoodi-opas / Bank Bar Code Guide* 5.3.

| Item | Specification | Rendered (600 dpi measurement) |
|---|---|---|
| Form size | 210 mm × 101.6 mm (4") | bottom 101.6 mm of A4 |
| Label column | 8/10" = 20.32 mm | 20.320 |
| Centre line | + 36/10" = 111.76 mm | 111.760 |
| Right edge | + 36/10" = 203.20 mm | 203.20 |
| Row lines | 8/12", 15/12", 31/12", 35/12" = 16.933, 31.750, 65.617, 74.083 mm | 16.933, 31.750, 65.617, 74.083 |
| Signature guide line | 29/12" = 61.383 mm | 61.362 |
| Line weights | thin 0.13 mm, thick 0.50 mm | 0.127, 0.508 |
| Guide texts | 7 pt; title 8 pt; legal text 6 pt | as specified |
| Printed data | at least 9 pt | 10 pt |
| Barcode | Code 128 set C, 54 digits, length 70–105 mm, height 10.0–12.7 mm, quiet zones 20 mm | 104.0 mm × 12.0 mm, quiet zones 20.3 / 22.3 mm |

Open points, to be checked against a printed original:

- The model drawing does not dimension the dividers after "Viitenumero / Eräpäivä" and before
  "Euro". They are placed at 6/10" and 19/10" from the centre line, which matches the
  drawing's proportions.
- Positions of the guide texts inside their cells follow the drawing by eye.
- `PrintScaling /None` is set so viewers print at actual size; printing with "fit to page"
  would shrink the form and the barcode.
- The sample Finvoice file has not been validated against the Finvoice 3.0 XSD.

## Roadmap

Library:

1. Table rows that split across pages.
2. PDF/A-3b (XMP, output intent) and embedded file attachments; veraPDF in CI.
3. Kerning (GPOS/kern), hyphenation and CFF-based OpenType fonts.
4. Templates as data (JSON) and a lambda-style builder on top of the element tree.
5. Tagged PDF / PDF/UA.

Finvoice sample: payment QR on the form, Finvoice XML attached to a PDF/A-3 invoice, Swedish
and English texts.
