# Troubleshooting

## Exceptions and what they mean

Arkki fails early and says why. Bad arguments fail at once, and a file that
cannot be read fails with one of three exceptions, never with another one or a
hang.

| Exception | Meaning | What to do |
|---|---|---|
| `ArgumentException` and its kin (`ArgumentNullException`, `ArgumentOutOfRangeException`) | You passed something the method cannot use. The message names it. | Fix the call. |
| `InvalidDataException` | The file is not a PDF, or it is damaged beyond repair. | Get the file again; it is the file, not your code. |
| `PdfPasswordException` | The file is encrypted and the password does not open it. | Ask for the password, then pass it to `Open`. |
| `NotSupportedException` | The file, font or image uses a feature Arkki does not read. The message names the feature. | See the cases below. |
| `KeyNotFoundException` | A form has no field with the name you gave. | List `doc.Form.Fields` to see the names. |
| `InvalidOperationException` | The field is not of the kind the call is for. | See [[Forms]]. |

Catching the three reading failures apart:

```csharp
try
{
    PdfReader reader = PdfReader.Open(pdf, password);
    Console.WriteLine(reader.PageCount);
}
catch (PdfPasswordException) { Console.WriteLine("Wrong or missing password."); }
catch (InvalidDataException e) { Console.WriteLine($"Damaged file: {e.Message}"); }
catch (NotSupportedException e) { Console.WriteLine($"Not supported: {e.Message}"); }
```

### NotSupportedException cases you may meet

- **A font collection (.ttc).** Extract one font from the collection and load
  that.
- **An interlaced PNG.** Save it without interlacing.
- **An image that is neither PNG nor JPEG.** Convert it first.
- **A PDF encrypted with something other than the standard security handler**,
  such as a certificate.

### Hostile files

The reader is built for files you do not trust. A stream that decodes past
256 MB is refused. The test suite mutates every sample file thousands of times
and tries hand-made traps: deep nesting, reference cycles, huge counts and
lengths, and a decompression bomb.

## Threads

- Fonts (`TrueTypeFont`) and images (`PdfImage`) are immutable and safe to
  share across threads.
- A `PdfDocument` or a `PdfReader` is used by one thread at a time.
- Building many documents in parallel is fine: give each thread its own
  document and share the fonts and images.

```csharp
TrueTypeFont face = TrueTypeFont.Load(File.ReadAllBytes("Inter-Regular.ttf"));
var results = new byte[100][];
Parallel.For(0, results.Length, i =>
{
    var doc = new PdfDocument();                 // one per thread
    PdfFont font = doc.AddFont(face);            // the parsed font is shared
    doc.AddPage().Canvas.Text(font, 12, 50, 50, $"Document {i}");
    results[i] = doc.ToArray();
});
```

## Text problems

**Italic text is not italic.**
Italic needs its own font file. Give the family one:
`new FontFamily(regular, bold, Italic: italic, BoldItalic: boldItalic)`.

**A letter is missing or shows as an empty box.**
The font has no glyph for it. Arkki uses only the font you gave and never falls
back to a system font. Check with `TrueTypeFont.HasGlyph`, and pick a font that
covers your text:

```csharp
TrueTypeFont face = TrueTypeFont.Load(File.ReadAllBytes("Inter-Regular.ttf"));
string text = "Łódź";
foreach (char c in text)
    if (!face.HasGlyph(c)) Console.WriteLine($"The font has no glyph for '{c}' (U+{(int)c:X4}).");
```

**Arabic, Hebrew or Indic text comes out wrong.**
Right-to-left and complex-script shaping are not included.

**Long words stick out or lines break badly.**
Put soft hyphens (U+00AD) in the text, or plug in your language's rules with
`IHyphenator`; see [[Layout]]. No hyphenation dictionaries ship with Arkki.

**An .otf font makes the file bigger than a .ttf.**
TrueType fonts are embedded as subsets of the glyphs used. OpenType fonts with
CFF outlines are embedded whole; CFF subsetting is not included.

## Form problems

**Non-Latin text in a form field shows as question marks.**
The appearance Arkki draws for a field uses Helvetica with the WinAnsi
character set, which covers Western European text. Other characters (Cyrillic,
Greek, Chinese and so on) are kept in the field's value, so software that reads
the form gets them right, but they show as `?` on the page. For such text, draw
it on the page with the canvas and your own font, and leave the field out.

**`SetValue` throws on a combo box.**
The value is not one of the field's `Options`, and the combo box is not
editable. Print `field.Options` to see what it takes.

**A radio button's value is refused when creating it.**
Values are plain names: ASCII letters, digits, `.`, `_` and `-`, and not `Off`.
Put the human-readable label next to the button as text.

**A created field has no label.**
Fields draw their box only. Draw the label with the canvas; see [[Forms]].

## Signature problems

**The viewer says the signature is valid but not trusted.**
The viewer trusts certificates from CAs on its own lists (for Adobe, the AATL
or EU trusted lists). A certificate you issued yourself gives a valid but
untrusted signature.

**Signing a second time throws.**
The field name `Signature1` is taken by the first signature. Set
`PdfSignatureOptions.FieldName` to another name.

## Still stuck

[Open an issue](https://github.com/geezery/arkki/issues) with the smallest code
that shows the problem, and the file if you can share it. Please do not attach
files that hold personal or confidential data.
