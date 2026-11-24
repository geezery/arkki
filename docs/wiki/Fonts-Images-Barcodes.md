# Fonts, images and barcodes

## Fonts

Fonts are loaded from bytes you pass in; Arkki never looks for system fonts.

- **TrueType** fonts (.ttf) are embedded as subsets of the glyphs used.
- **OpenType fonts with CFF outlines** (.otf) are embedded whole.
- A `TrueTypeFont` is parsed once and can be shared by any number of documents
  and threads.

```csharp
TrueTypeFont face = TrueTypeFont.Load(File.ReadAllBytes("Inter-Regular.ttf"));
Console.WriteLine($"{face.PostScriptName}: {face.GlyphCount} glyphs, has ä {face.HasGlyph('ä')}");

var doc = new PdfDocument();
PdfFont font = doc.AddFont(face);
double width = font.Width("Order 1187", 12);   // in points at 12 pt
```

### Families: bold and italic

Each weight and slant is its own font file: a family needs a regular and a
bold font, and italic needs its own font file too.

```csharp
var doc = new PdfDocument();
PdfFont regular = doc.AddFont(File.ReadAllBytes("Inter-Regular.ttf"));
PdfFont bold = doc.AddFont(File.ReadAllBytes("Inter-Bold.ttf"));
PdfFont italic = doc.AddFont(File.ReadAllBytes("Inter-Italic.ttf"));
PdfFont boldItalic = doc.AddFont(File.ReadAllBytes("Inter-BoldItalic.ttf"));

var fonts = new FontFamily(regular, bold, Italic: italic, BoldItalic: boldItalic);
var body = new TextStyle(fonts, 10);
var emphasis = body with { Italic = true };
```

### Scripts

Right-to-left and complex-script shaping (Arabic, Indic) are not included.
A font draws only the characters it has a glyph for; `TrueTypeFont.HasGlyph`
tells you before you print.

## Images

PNG and JPEG. A PNG's transparency becomes a soft mask, and PNGs are stored in
the cheapest lossless form. An image used many times is embedded once.

```csharp
PdfImage logo = PdfImage.Load(File.ReadAllBytes("logo.png"));
Console.WriteLine($"{logo.Width} × {logo.Height} pixels");

// On the canvas, at an exact place and size:
var doc = new PdfDocument();
PdfPage page = doc.AddPage();
page.Canvas.Image(logo, Unit.Mm(20), Unit.Mm(20), Unit.Mm(40), Unit.Mm(15));

// In the flow, scaled to fit a box:
var inFlow = new ImageBox(logo, maxWidth: Unit.Mm(40), maxHeight: Unit.Mm(20), HAlign.Right);
```

A PNG that is larger than it prints can be shrunk as it is loaded. Pick the
limits from the printed size: a 60 mm wide logo needs about 710 pixels at
300 dpi.

```csharp
PdfImage small = PdfImage.Load(File.ReadAllBytes("photo.png"), maxPixelWidth: 710, maxPixelHeight: 710);
```

JPEG data is never re-encoded, so a JPEG keeps its size.

## Barcodes

Code 128, EAN-13 and QR code are drawn as vector rectangles, so they stay sharp
at any size.

```csharp
var doc = new PdfDocument();
PdfPage page = doc.AddPage();

page.Canvas.Barcode(new Code128().Encode("A-1187"), x: 50, y: 50, width: 150, height: 36);
page.Canvas.Barcode(new Ean13().Encode(Ean13.WithCheckDigit("640123456789")), x: 50, y: 110, width: 110, height: 60);
page.Canvas.Barcode(new QrCode(QrErrorCorrection.M).Encode("https://example.com"), x: 50, y: 190, width: 80, height: 80);

// In the flow:
var inFlow = new BarcodeBox(new QrCode(), "https://example.com", width: 80, height: 80, HAlign.Center);
```

### Another symbology

Implement `IBarcodeSymbology` to return a `BarcodeMatrix`, and it works with
`Canvas.Barcode` and `BarcodeBox` like the built-in ones:

```csharp
sealed class Stripes : IBarcodeSymbology
{
    public BarcodeMatrix Encode(string text)
    {
        var matrix = new BarcodeMatrix(columns: text.Length * 2, rows: 1, quietZone: 10);
        for (int i = 0; i < text.Length; i++) matrix[i * 2, 0] = true;   // a dark module
        return matrix;
    }
}
```

## Size

Fonts are subset, images are embedded once, and merging stores identical
objects once. The three-page sample invoice is roughly 100 kB including the
logo and two subset fonts.
