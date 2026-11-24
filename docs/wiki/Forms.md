# Forms

Arkki reads, fills and creates PDF form fields (AcroForm): text fields, check
boxes, radio buttons, combo boxes and list boxes. A filled field gets a new
appearance, so its value shows in every viewer, not only in the ones that redraw
fields themselves.

## Read the fields of any file

```csharp
foreach (PdfFormField f in PdfReader.Open(pdf).GetFormFields())
    Console.WriteLine($"{f.Name} ({f.Kind}) = {f.Value}");
```

A field is known by its full name, with parent names joined by dots:
`customer.email`.

| Property | What it tells you |
|---|---|
| `Name` | The full name |
| `Kind` | `Text`, `CheckBox`, `RadioButton`, `Choice`, `PushButton`, `Signature` or `Unknown` |
| `Value` | The text of a text field, the chosen option of a combo or list box, the chosen button of a radio group, `"Off"` for a check box that is not checked, `null` when there is none |
| `Values` | Every chosen option of a list box that allows several; otherwise `Value` alone |
| `IsChecked` | True for a check box or radio group that is on |
| `Options` | The choices of a combo or list box, or the values of a radio group's buttons |
| `IsComboBox` | True for a drop-down list, false for a list box |
| `IsEditable` | True for a combo box that also takes text that is not among its options |
| `IsMultiSelect` | True for a list box in which several options can be chosen |
| `ReadOnly` | True when the form does not let a user change the field |

## Fill a form

Open the file as a document, set values by field name, and save:

```csharp
var doc = PdfDocument.Open(File.ReadAllBytes("application.pdf"));

doc.Form.SetValue("customer.name", "Maija Meikäläinen");   // a text field
doc.Form.SetChecked("agree", true);                         // a check box
doc.Form.SetValue("delivery", "courier");                   // a radio group, by the button's value
doc.Form.SetValue("country", "Norge");                      // a combo or list box, by the option
doc.Form.SetValues("extras", ["Gift wrap", "Insurance"]);   // a list box that takes several

File.WriteAllBytes("filled.pdf", doc.ToArray());
```

- `SetValue` on a radio group takes one of the group's `Options`; `"Off"`
  chooses none.
- `SetValue` on a combo or list box takes one of its `Options`. An editable
  combo box (`IsEditable`) takes any text.
- `SetValues` chooses several options of a list box with `IsMultiSelect`, one
  option of any other combo or list box, and clears the choice when given
  none.
- Some forms store another value than the text the reader sees (the option
  shows "Suomi", the form stores "fi"). `Options` holds the visible texts.
  You can set either; the stored one is what ends up in `Value`.

To look before you fill, `doc.Form.Fields` lists the fields and
`doc.Form.Find(name)` returns one or `null`:

```csharp
var doc = PdfDocument.Open(File.ReadAllBytes("application.pdf"));
PdfFormField? country = doc.Form.Find("country");
if (country is { Kind: PdfFieldKind.Choice })
    Console.WriteLine(string.Join(", ", country.Options));
```

### When a value is refused

| You did | You get |
|---|---|
| Named a field the form does not have | `KeyNotFoundException` |
| Gave a radio group a value none of its buttons has | `ArgumentException`, naming the buttons |
| Gave a combo or list box a value that is not an option | `ArgumentException`, naming the options |
| Gave several values to a field that takes one | `InvalidOperationException` |
| Called `SetChecked` on something that is not a check box, or `SetValues` on something that is not a combo or list box | `InvalidOperationException` |

## Create fields

Fields are added to a page at a position in points from the page's top-left
corner. Each method returns the `PdfFormField`.

### Text fields and check boxes

```csharp
var doc = new PdfDocument();
PdfPage page = doc.AddPage();

page.AddTextField("customer.name", x: 50, y: 100, width: 200, height: 18);
page.AddTextField("notes", x: 50, y: 130, width: 300, height: 60, value: "Line 1\nLine 2", fontSize: 9, multiline: true);
page.AddTextField("code", x: 50, y: 200, width: 60, height: 18, maxLength: 6);
page.AddCheckBox("newsletter", x: 50, y: 230, size: 12, isChecked: true);
```

A font size of 0 sizes a text field's text to its box. A check box's value is
`"Yes"` when checked and `"Off"` when not.

### Radio buttons

Buttons added with the same group name form one field. The first button of a
name creates the group, and the buttons can lie on any pages of the document:

```csharp
var doc = new PdfDocument();
PdfPage page = doc.AddPage();

page.AddRadioButton("plan", "basic", x: 50, y: 100, size: 12, selected: true);
page.AddRadioButton("plan", "pro", x: 50, y: 120, size: 12);
PdfFormField plan = page.AddRadioButton("plan", "enterprise", x: 50, y: 140, size: 12);

Console.WriteLine(plan.Value);                       // basic
Console.WriteLine(string.Join(", ", plan.Options));  // basic, pro, enterprise
```

- A button's value is a plain name: ASCII letters, digits, `.`, `_` and `-`,
  and not `Off`. It is the value the form holds, not a label; draw the label
  next to the button yourself.
- A value appears once in a group.
- `readOnly: true` on any button locks the whole group.

### Combo boxes

A combo box is a drop-down list showing the chosen option:

```csharp
var doc = new PdfDocument();
PdfPage page = doc.AddPage();

page.AddComboBox("language", x: 50, y: 100, width: 120, height: 18, ["Suomi", "Svenska", "English"], value: "Suomi");
page.AddComboBox("title", x: 50, y: 130, width: 120, height: 18, ["Mr", "Ms"], value: "Dr", editable: true);
```

With `editable: true` the reader can also type text that is not an option, and
`value` can be such text.

### List boxes

A list box shows its options in a box. Make the box tall enough for them: a row
is 1.25 times the font size.

```csharp
var doc = new PdfDocument();
PdfPage page = doc.AddPage();

page.AddListBox("size", x: 50, y: 100, width: 120, height: 40, ["S", "M", "L"], value: "M");

page.AddListBox("days", x: 50, y: 160, width: 120, height: 40, ["Mon", "Tue", "Wed"], multiSelect: true);
doc.Form.SetValues("days", ["Mon", "Wed"]);
```

Options are distinct, and a `value` given at creation is one of them.

### Labels and borders

A created field draws its own thin grey border. It has no label: draw one with
the canvas or put the field beside text you laid out.

```csharp
var doc = new PdfDocument();
PdfFont font = doc.AddFont(File.ReadAllBytes("Inter-Regular.ttf"));
PdfPage page = doc.AddPage();

page.Canvas.Text(font, 10, 50, 96, "Email");
page.AddTextField("email", x: 50, y: 100, width: 200, height: 18);
```

## Limits

- **Field text is Western European.** The appearance of a field is drawn in
  Helvetica with the WinAnsi character set: Latin letters including å, ä, ö,
  é, ü and the euro sign. Other characters are stored in the field's value but
  show as `?` in the appearance Arkki draws. See [[Troubleshooting]].
- **Radio button values are plain ASCII names**, as above.
- **Not included:** push buttons, JavaScript actions and calculated fields.
  Push buttons and signature fields are read (their `Kind` says so) but have
  no value to set.
