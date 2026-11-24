# Editing and merging

Arkki opens PDF files other programs made. Saving always writes a new file; the
input is not changed.

## Open a file for changes

`PdfDocument.Open` imports every page along with the document information, the
language and the form fields:

```csharp
var doc = PdfDocument.Open(File.ReadAllBytes("contract.pdf"));
Console.WriteLine($"{doc.Pages.Count} pages, title {doc.Info.Title}");
File.WriteAllBytes("copy.pdf", doc.ToArray());
```

An encrypted file takes its password as the second argument; see
[[Encryption and Signing]].

## Stamp: draw over a page

An imported page has a canvas like a new one. Whatever you draw lies on top of
the page's own content:

```csharp
var doc = PdfDocument.Open(File.ReadAllBytes("contract.pdf"));
PdfFont font = doc.AddFont(File.ReadAllBytes("Inter-Bold.ttf"));

PdfCanvas c = doc.Pages[0].Canvas;
c.Save();
c.SetOpacity(0.2);
c.TextRotated(font, 60, 150, 500, 45, "DRAFT");
c.Restore();

File.WriteAllBytes("stamped.pdf", doc.ToArray());
```

The canvas is described in [[Layout]].

## Pages: insert, remove, move, rotate

Page positions are zero-based.

```csharp
var doc = PdfDocument.Open(File.ReadAllBytes("contract.pdf"));

PdfPage cover = doc.InsertPage(0, PageSize.A4);   // a new first page
doc.RemovePageAt(3);
doc.MovePage(0, 2);
doc.Pages[1].Rotation = 90;                       // 0, 90, 180 or 270, clockwise
PdfPage last = doc.AddPage();
```

The canvas of a rotated page still draws in the unrotated page's coordinates.

## Merge, extract and split

```csharp
byte[] batch = PdfDocument.Merge(invoicePdfs).ToArray();

PdfDocument firstTwo = PdfDocument.ExtractPages(pdf, 0, 1);

IReadOnlyList<PdfDocument> onePerPage = PdfDocument.Split(pdf);
```

Merging stores identical objects once, so a logo or a font subset repeated in
every invoice of a batch takes its space one time.

To pick pages from several files into one document, import them from readers:

```csharp
var doc = new PdfDocument();
PdfReader terms = PdfReader.Open(File.ReadAllBytes("terms.pdf"));
doc.ImportPage(terms, 0);                                             // one page
doc.ImportPages(PdfReader.Open(File.ReadAllBytes("appendix.pdf")));   // all of them
byte[] combined = doc.ToArray();
```

Pages keep their content, resources and annotations exactly; nothing is
re-rendered.

## Bookmarks, links and attachments

```csharp
var doc = PdfDocument.Open(File.ReadAllBytes("contract.pdf"));

// Bookmarks: the outline viewers show beside the pages. They nest.
PdfBookmark terms = doc.Outline.Add("Terms", doc.Pages[1]);
terms.Add("Delivery", doc.Pages[1], top: 300);

// Links: to a web address, or to a place in this document.
doc.Pages[0].AddLink(50, 700, 100, 14, new Uri("https://example.com"));
doc.Pages[0].AddLink(50, 720, 100, 14, doc.Pages[1], top: 300);

// Attachments: files embedded in the PDF.
doc.Attach("order.xml", File.ReadAllBytes("order.xml"), "application/xml");
```

## Read without changing

`PdfReader` reads a file's pages, text, metadata and form fields:

```csharp
PdfReader reader = PdfReader.Open(pdf);
Console.WriteLine($"PDF {reader.Version}, {reader.PageCount} pages, title {reader.Info.Title}");

var (width, height) = reader.GetPageSize(0);
string firstPage = reader.ExtractText(0);
string everything = reader.ExtractText();
```

Extracted text comes in drawing order, with a line break where the baseline
moves. It is meant for search and tests, not for rebuilding a layout.

## What the reader handles

Cross-reference tables and streams, object streams, incremental updates,
damaged cross-references (rebuilt by scanning) and encrypted files (RC4
40/128-bit, AES-128, AES-256 with a user or owner password).

A file that cannot be read fails with one of three exceptions and never with a
hang; see [[Troubleshooting]].
