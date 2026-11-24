# Layout

You describe a document as a tree of elements, and `FlowDocument` lays the tree
out over as many pages as it needs. Sizes are in points (1/72 inch);
`Unit.Mm(20)` and `Len.Mm(20)` convert from millimetres.

The snippets on this page use a `body` style made as in [[Getting Started]].

## The page: FlowDocument

```csharp
var doc = new PdfDocument();
new FlowDocument
{
    PageWidth = PageSize.A4.Width, PageHeight = PageSize.A4.Height,   // A4 is the default
    Margins = Margins.Mm(20, 15, 15, 15),                             // left, top, right, bottom
    Header = page => new TextBlock("Order 1187", body),
    Footer = page => new TextBlock($"Page {page.PageNumber} ({page.PageCount})", body, HAlign.Right),
    Content = new TextBlock("Hello", body),
}.RenderTo(doc);
```

The header and footer are functions of the page, so they can show the page
number and the page count, or differ on the first page (`page.IsFirst`) and the
last (`page.IsLast`). Return `null` for a page that has none.

`PageOverlay` draws on every page with the canvas after the content, for
example a watermark:

```csharp
var flow = new FlowDocument
{
    Content = new TextBlock("Hello", body),
    PageOverlay = (canvas, page) =>
    {
        canvas.Save();
        canvas.SetOpacity(0.2);
        canvas.TextRotated(body.Font, 60, 150, 500, 45, "DRAFT");
        canvas.Restore();
    },
};
```

## Paper sizes

`PageSize.A0` to `A7`, `B4`, `B5`, `C4` to `C6`, `DL`, `Letter`, `Legal`,
`Tabloid` and `Executive` are ready. Turn one on its side or make your own:

```csharp
var landscape = PageSize.Landscape(PageSize.A4);
var label = PageSize.Mm(100, 150);

var flow = new FlowDocument
{
    PageWidth = landscape.Width, PageHeight = landscape.Height,
    Content = new TextBlock("Wide", body),
};
```

## Text

A `TextBlock` is one or more paragraphs in one style, or in several:

```csharp
var plain = new TextBlock("Delivered within two weeks of the order.", body);
var centred = new TextBlock("Order confirmation", body with { Size = 18, Bold = true }, HAlign.Center);
var justified = new TextBlock("Long terms of delivery that wrap and are justified …", body, HAlign.Justify);
var mixed = new TextBlock([new("Due ", body), new("7.12.2026", body with { Bold = true })]);
var red = new TextBlock("Overdue", body with { Ink = Color.FromHex("#c0392b") });
```

- Text is set left, right, centred or justified (`HAlign`).
- A soft hyphen (U+00AD) in the text and a compound word's own hyphen are break
  points.
- Arkki ships no hyphenation dictionaries. Plug in your language's rules with
  `IHyphenator`:

```csharp
sealed class MyHyphenator : IHyphenator
{
    // The positions before which a hyphen may break the word: "kirjasto" → 3 and 5 for "kir-jas-to".
    public IEnumerable<int> BreakPoints(string word) => word == "kirjasto" ? [3, 5] : [];
}
```

```csharp
var text = new TextBlock("kirjasto", body, HAlign.Justify) { Hyphenator = new MyHyphenator() };
```

- Italic needs its own font file:
  `new FontFamily(regular, bold, Italic: italic, BoldItalic: boldItalic)`.

## Lists

Bulleted, numbered and lettered lists with hanging indents. An item that is
itself a list is a nested list:

```csharp
var steps = new ListBlock(body, ListMarker.Number)
    .Add("Check the parts")
    .Add("Book the time");

var nested = new ListBlock(body, ListMarker.Bullet)
    .Add("Tools")
    .Add(new ListBlock(body, ListMarker.LowerLetter).Add("Wrench").Add("Gauge"));
```

The markers are `Bullet`, `Dash`, `Number`, `LowerLetter` and `None`.

## Tables

A table's header repeats on every page the table reaches. Columns are a fixed
width or a share of what is left (`Len.Star()`):

```csharp
var bold = body with { Bold = true };
var table = new Table([
        new TableColumn(Len.Star()),
        new TableColumn(Len.Mm(20), HAlign.Right),
        new TableColumn(Len.Mm(30), HAlign.Right)])
    .SetHeader(bold, "Item", "Qty", "Amount");
table.HeaderRule = Color.Black;
table.RowRule = Color.Gray(0.85);

table.AddRow(body, "Machining", "2", "258,00");

// A cell can be any element: span columns, or give one cell its own fill.
table.AddRow(
    new TextBlock("Delivered to the Tampere site", body).SpanColumns(2),
    new TextBlock("0,00", body).CellFill(Color.Gray(0.95)));

// A row style: a background, rules, padding, and rows kept on the same page.
table.AddRow(new RowStyle(Background: Color.Gray(0.9), RuleAbove: Color.Black),
    new TextBlock("Total", bold), new TextBlock("", body), new TextBlock("258,00", bold));
```

## Rows, columns and boxes

`Column` stacks elements, `Row` puts them side by side, and the extension
methods wrap an element in padding, a background or a border:

```csharp
var address = new Column(
    new TextBlock("Konepaja Oy", body with { Bold = true }),
    new TextBlock("Teollisuustie 1", body)) { Spacing = 2 };

var top = new Row { Spacing = 12 }
    .Add(Len.Star(), address)
    .Add(Len.Mm(60), new TextBlock("Invoice 1187", body, HAlign.Right));

var note = new TextBlock("Payment within 14 days.", body)
    .Padding(6)
    .Background(Color.Gray(0.95));

var content = new Column(top, new Spacer(12), new Rule(Color.Black), note) { Spacing = 6 };
```

A row never splits across pages. A column splits between and inside its
children.

## Keeping things together, and breaking pages

```csharp
var heading = new TextBlock("Terms", body with { Bold = true }).KeepWithNext();   // not left alone at a page's end
var block = new Column(new TextBlock("Signed", body), new Spacer(30)).KeepTogether();   // moves whole to the next page
var content = new Column(heading, block, new PageBreak(), new TextBlock("Appendix", body));
```

## Images and barcodes in the flow

```csharp
PdfImage logo = PdfImage.Load(File.ReadAllBytes("logo.png"));
var content = new Column(
    new ImageBox(logo, maxWidth: Unit.Mm(40), maxHeight: Unit.Mm(20)),
    new BarcodeBox(new Code128(), "A-1187", width: 150, height: 36));
```

More in [[Fonts Images Barcodes]].

## When you need exact positions: the canvas

Every page has a canvas with its origin at the top-left corner, y down, in
points. Use it for forms where every mark has its place:

```csharp
var doc = new PdfDocument();
PdfFont font = doc.AddFont(File.ReadAllBytes("Inter-Regular.ttf"));
PdfPage page = doc.AddPage(PageSize.A4);

page.Canvas.Text(font, 12, Unit.Mm(20), Unit.Mm(30), "Exactly here");
page.Canvas.TextRight(font, 12, Unit.Mm(190), Unit.Mm(30), "258,00");
page.Canvas.SetStrokeColor(Color.Gray(0.5));
page.Canvas.Line(Unit.Mm(20), Unit.Mm(34), Unit.Mm(190), Unit.Mm(34));
page.Canvas.Barcode(new Code128().Encode("A-1187"), Unit.Mm(20), Unit.Mm(40), 150, 36);
page.AddLink(Unit.Mm(20), Unit.Mm(60), 100, 14, new Uri("https://example.com"));
```

Inside a flow, `Custom` reserves a fixed height and hands you the canvas and the
area's position and width:

```csharp
var signatureLine = new Custom(40, (canvas, x, y, width) => canvas.Line(x, y + 30, x + width / 2, y + 30));
```

## How fast is it

A 50 000-row table lays out to 1 021 pages in about 1.2 s and 1.5 MB on a
two-core cloud VM. The three-page sample invoice renders in about 5 ms at
roughly 100 kB including the logo and two subset fonts.
