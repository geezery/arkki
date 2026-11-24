using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf;

/// <summary>The kind of a form field.</summary>
public enum PdfFieldKind
{
    /// <summary>A text field.</summary>
    Text,
    /// <summary>A check box.</summary>
    CheckBox,
    /// <summary>A group of radio buttons; the value is the chosen option.</summary>
    RadioButton,
    /// <summary>A list or combo box.</summary>
    Choice,
    /// <summary>A button with no value.</summary>
    PushButton,
    /// <summary>A signature field.</summary>
    Signature,
    /// <summary>A field type Arkki does not know.</summary>
    Unknown,
}

/// <summary>A form field (AcroForm) by its fully qualified name, such as "customer.name".</summary>
public sealed class PdfFormField
{
    internal PdfFormField(string name, PdfFieldKind kind, string? value, bool readOnly, IReadOnlyList<string> options)
    {
        Name = name; Kind = kind; Value = value; ReadOnly = readOnly; Options = options;
    }

    /// <summary>The field's full name, with parent names joined by dots ("customer.email").</summary>
    public string Name { get; }
    /// <summary>What kind of field it is.</summary>
    public PdfFieldKind Kind { get; }

    /// <summary>
    /// The value: the text of a text or choice field, the export value of the chosen button ("Yes", or "Off" when a
    /// check box is not checked), null when the field has none.
    /// </summary>
    public string? Value { get; internal set; }

    /// <summary>True for a check box or radio button that is on.</summary>
    public bool IsChecked => Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && Value is not null && Value != "Off";

    /// <summary>
    /// The chosen values: every chosen option of a list box that allows several, otherwise <see cref="Value"/> alone,
    /// and nothing when the field has no value.
    /// </summary>
    public IReadOnlyList<string> Values => Selected ?? (Value is null ? [] : [Value]);

    /// <summary>True when the form does not let a user change the field.</summary>
    public bool ReadOnly { get; internal set; }

    /// <summary>The choices of a choice field as the reader sees them, or the export values of a radio group's buttons.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>True for a choice field that is a combo box (a drop-down list); false for a list box and other kinds.</summary>
    public bool IsComboBox { get; internal init; }

    /// <summary>True for a combo box that also takes text that is not among its options.</summary>
    public bool IsEditable { get; internal init; }

    /// <summary>True for a list box in which several options can be chosen at once.</summary>
    public bool IsMultiSelect { get; internal init; }

    // A choice field's values as the form stores them, one per option; null when they are the options themselves.
    internal IReadOnlyList<string>? Exports { get; init; }
    internal IReadOnlyList<string>? Selected { get; set; }
    internal RadioGroup? Group { get; init; }

    // Where an imported field lives; null for a field created in this document.
    internal PdfReader? Reader { get; init; }
    internal int ObjectNumber { get; init; }
    internal IReadOnlyList<int> Widgets { get; init; } = [];
    internal double FontSize { get; init; }
    internal bool Multiline { get; init; }
    internal bool Edited { get; set; }
    internal PageAnnotation? Created { get; init; }

    /// <summary>The field's name, kind and value, for debugging.</summary>
    public override string ToString() => $"{Name} ({Kind}) = {Value}";
}

/// <summary>
/// The form of a document: the fields of the pages it imported (see <see cref="PdfDocument.Open(byte[], string?)"/>) and
/// the ones added with <see cref="PdfPage.AddTextField"/>, <see cref="PdfPage.AddCheckBox"/>,
/// <see cref="PdfPage.AddRadioButton"/>, <see cref="PdfPage.AddComboBox"/> and <see cref="PdfPage.AddListBox"/>. Filling a
/// field gives it a new appearance, so it shows the value in every viewer.
/// </summary>
public sealed class PdfForm
{
    private readonly List<PdfFormField> _fields = [];

    /// <summary>Every field: those of imported pages and those created in this document.</summary>
    public IReadOnlyList<PdfFormField> Fields => _fields;

    /// <summary>The field with this full name, or null.</summary>
    public PdfFormField? Find(string name) => _fields.FirstOrDefault(f => f.Name == name);

    /// <summary>The field with this full name; throws <see cref="KeyNotFoundException"/> when there is none.</summary>
    public PdfFormField this[string name] => Find(name) ?? throw new KeyNotFoundException($"The form has no field named '{name}'.");

    /// <summary>
    /// Sets a text field's text, chooses an option of a combo or list box, or chooses a radio button by its export
    /// value ("Off" chooses none). A choice must be one of <see cref="PdfFormField.Options"/>, unless the field is an
    /// editable combo box.
    /// </summary>
    public void SetValue(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        PdfFormField f = this[name];
        switch (f.Kind)
        {
            case PdfFieldKind.Text:
                break;
            case PdfFieldKind.Choice:
                SetChoice(f, [value]);
                return;
            case PdfFieldKind.RadioButton:
                if (value != "Off" && !f.Options.Contains(value))
                    throw new ArgumentException($"Radio group '{name}' has no button '{value}'. Its buttons: {string.Join(", ", f.Options)}.", nameof(value));
                break;
            case PdfFieldKind.CheckBox:
                SetChecked(name, value is not ("" or "Off"));
                return;
            default:
                throw new InvalidOperationException($"Field '{name}' is a {f.Kind} and has no value to set.");
        }
        Set(f, value);
    }

    /// <summary>
    /// Chooses the options of a combo or list box: several for a list box that allows it
    /// (<see cref="PdfFormField.IsMultiSelect"/>), one otherwise, none to clear the choice.
    /// </summary>
    public void SetValues(string name, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        PdfFormField f = this[name];
        if (f.Kind != PdfFieldKind.Choice) throw new InvalidOperationException($"Field '{name}' is a {f.Kind}, not a combo or list box.");
        string[] chosen = [.. values];
        if (chosen.Any(v => v is null)) throw new ArgumentException("A value is null.", nameof(values));
        if (chosen.Length > 1 && !f.IsMultiSelect)
            throw new InvalidOperationException($"Field '{name}' takes one value, not {chosen.Length}.");
        SetChoice(f, chosen);
    }

    /// <summary>Checks or unchecks a check box.</summary>
    public void SetChecked(string name, bool on)
    {
        PdfFormField f = this[name];
        if (f.Kind != PdfFieldKind.CheckBox) throw new InvalidOperationException($"Field '{name}' is a {f.Kind}, not a check box.");
        Set(f, on ? (f.Options.FirstOrDefault() ?? "Yes") : "Off");
    }

    private static void SetChoice(PdfFormField f, string[] values)
    {
        // A value is given as the option the reader sees or as the value the form stores for it; the stored one is kept.
        var stored = new List<string>(values.Length);
        foreach (string v in values)
        {
            int i = OptionIndex(f, v);
            if (i < 0 && !f.IsEditable)
                throw new ArgumentException($"Field '{f.Name}' has no option '{v}'. Its options: {string.Join(", ", f.Options)}.", nameof(values));
            string s = i >= 0 && f.Exports is not null ? f.Exports[i] : v;
            if (!stored.Contains(s)) stored.Add(s);
        }
        f.Selected = stored;
        f.Value = stored.Count > 0 ? stored[0] : null;
        f.Edited = true;
        if (f.Created is ChoiceFieldAnnotation c) c.Selected = stored;
    }

    /// <summary>The index of the option with this stored value or, failing that, this visible text; -1 when there is none.</summary>
    internal static int OptionIndex(PdfFormField f, string value)
    {
        if (f.Exports is { } exports)
            for (int i = 0; i < exports.Count; i++) if (exports[i] == value) return i;
        for (int i = 0; i < f.Options.Count; i++) if (f.Options[i] == value) return i;
        return -1;
    }

    private static void Set(PdfFormField f, string value)
    {
        f.Value = value;
        f.Edited = true;
        if (f.Group is { } group) group.Value = value;
        switch (f.Created)
        {
            case TextFieldAnnotation t: t.Value = value; break;
            case CheckBoxAnnotation c: c.Checked = value != "Off"; break;
        }
    }

    internal void Add(PdfFormField field)
    {
        if (Find(field.Name) is not null) throw new ArgumentException($"The form already has a field named '{field.Name}'.");
        _fields.Add(field);
    }

    internal void AddRange(IEnumerable<PdfFormField> fields)
    {
        foreach (var f in fields) if (Find(f.Name) is null) _fields.Add(f);
    }

    internal IEnumerable<PdfFormField> EditedImports => _fields.Where(f => f.Edited && f.Reader is not null);

    /// <summary>The overrides that give edited imported fields their new value and appearance.</summary>
    internal Dictionary<(PdfReader, int), PdfValue> BuildOverrides(SaveContext ctx)
    {
        var overrides = new Dictionary<(PdfReader, int), PdfValue>();
        foreach (PdfFormField f in EditedImports)
        {
            PdfReader r = f.Reader!;
            PdfDict Current(int number) =>
                overrides.TryGetValue((r, number), out PdfValue? o) ? (PdfDict)o : r.GetObject(number) as PdfDict ?? new PdfDict([]);

            bool button = f.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton;
            bool choice = f.Kind == PdfFieldKind.Choice;
            List<int> chosen = choice ? [.. f.Values.Select(s => OptionIndex(f, s)).Where(i => i >= 0).Order()] : [];
            if (choice)
            {
                // /V is one string or an array of them; /I, the chosen rows of a list box, must agree with it or go.
                PdfValue? v = f.Values.Count switch
                {
                    0 => null,
                    1 => Str(f.Values[0]),
                    _ => new PdfArray([.. f.Values.Select(Str)]),
                };
                PdfValue? rows = f.IsComboBox || chosen.Count == 0 ? null
                    : new PdfArray([.. chosen.Select(i => (PdfValue)new PdfNumber(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))]);
                overrides[(r, f.ObjectNumber)] = With(Current(f.ObjectNumber), ("V", v), ("I", rows));
            }
            else
            {
                PdfValue v = button ? new PdfName(f.Value!) : Str(f.Value ?? "");
                overrides[(r, f.ObjectNumber)] = With(Current(f.ObjectNumber), ("V", v));
            }

            foreach (int w in f.Widgets)
            {
                PdfDict widget = Current(w);
                if (button)
                {
                    bool has = r.Resolve(widget.Get("AP")) is PdfDict ap && r.Resolve(ap.Get("N")) is PdfDict n && n.Get(f.Value!) is not null;
                    overrides[(r, w)] = With(widget, ("AS", new PdfName(has ? f.Value! : "Off")));
                }
                else
                {
                    double[] rect = r.ReadRect(widget.Get("Rect")) ?? [0, 0, 0, 0];
                    double width = Math.Abs(rect[2] - rect[0]), height = Math.Abs(rect[3] - rect[1]);
                    int ap = choice && !f.IsComboBox
                        ? FieldAppearance.List(ctx, width, height, f.Options, chosen, f.FontSize)
                        : FieldAppearance.Text(ctx, width, height, Shown(f), f.FontSize, f.Multiline);
                    overrides[(r, w)] = With(widget, ("AP", new PdfDict([new("N", new PdfOutputRef(ap))])));
                }
            }
        }
        return overrides;
    }

    private static PdfValue Str(string s) => new PdfString(PdfSerializer.TextBytes(s), hex: true);

    /// <summary>What a field shows: its text, or for a combo box the visible text of the chosen option.</summary>
    private static string Shown(PdfFormField f)
    {
        if (f.Kind != PdfFieldKind.Choice || f.Value is null) return f.Value ?? "";
        int i = OptionIndex(f, f.Value);
        return i >= 0 ? f.Options[i] : f.Value;
    }

    /// <summary>A copy of the dictionary with these entries set; a null value takes the entry away.</summary>
    private static PdfDict With(PdfDict d, params (string Key, PdfValue? Value)[] set)
    {
        var entries = d.Entries.Where(e => !set.Any(s => s.Key == e.Key)).ToList();
        foreach (var (k, v) in set) if (v is not null) entries.Add(new(k, v));
        return d is PdfStream s ? new PdfStream(entries, s.Data) : new PdfDict(entries);
    }
}
