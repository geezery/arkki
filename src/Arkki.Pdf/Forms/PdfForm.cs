using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf;

public enum PdfFieldKind { Text, CheckBox, RadioButton, Choice, PushButton, Signature, Unknown }

/// <summary>A form field (AcroForm) by its fully qualified name, such as "customer.name".</summary>
public sealed class PdfFormField
{
    internal PdfFormField(string name, PdfFieldKind kind, string? value, bool readOnly, IReadOnlyList<string> options)
    {
        Name = name; Kind = kind; Value = value; ReadOnly = readOnly; Options = options;
    }

    public string Name { get; }
    public PdfFieldKind Kind { get; }

    /// <summary>
    /// The value: the text of a text or choice field, the export value of the chosen button ("Yes", or "Off" when a
    /// check box is not checked), null when the field has none.
    /// </summary>
    public string? Value { get; internal set; }

    /// <summary>True for a check box or radio button that is on.</summary>
    public bool IsChecked => Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && Value is not null && Value != "Off";

    public bool ReadOnly { get; }

    /// <summary>The choices of a choice field, or the export values of a radio group's buttons.</summary>
    public IReadOnlyList<string> Options { get; }

    // Where an imported field lives; null for a field created in this document.
    internal PdfReader? Reader { get; init; }
    internal int ObjectNumber { get; init; }
    internal IReadOnlyList<int> Widgets { get; init; } = [];
    internal double FontSize { get; init; }
    internal bool Multiline { get; init; }
    internal bool Edited { get; set; }
    internal PageAnnotation? Created { get; init; }

    public override string ToString() => $"{Name} ({Kind}) = {Value}";
}

/// <summary>
/// The form of a document: the fields of the pages it imported (see <see cref="PdfDocument.Open(byte[], string?)"/>) and
/// the ones added with <see cref="PdfPage.AddTextField"/> and <see cref="PdfPage.AddCheckBox"/>. Filling a field gives it
/// a new appearance, so it shows the value in every viewer.
/// </summary>
public sealed class PdfForm
{
    private readonly List<PdfFormField> _fields = [];

    public IReadOnlyList<PdfFormField> Fields => _fields;

    public PdfFormField? Find(string name) => _fields.FirstOrDefault(f => f.Name == name);

    public PdfFormField this[string name] => Find(name) ?? throw new KeyNotFoundException($"The form has no field named '{name}'.");

    /// <summary>Sets a text or choice field's text, or chooses a radio button by its export value.</summary>
    public void SetValue(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        PdfFormField f = this[name];
        switch (f.Kind)
        {
            case PdfFieldKind.Text or PdfFieldKind.Choice:
                break;
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

    /// <summary>Checks or unchecks a check box.</summary>
    public void SetChecked(string name, bool on)
    {
        PdfFormField f = this[name];
        if (f.Kind != PdfFieldKind.CheckBox) throw new InvalidOperationException($"Field '{name}' is a {f.Kind}, not a check box.");
        Set(f, on ? (f.Options.FirstOrDefault() ?? "Yes") : "Off");
    }

    private static void Set(PdfFormField f, string value)
    {
        f.Value = value;
        f.Edited = true;
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
            PdfValue v = button ? new PdfName(f.Value!) : new PdfString(PdfSerializer.TextBytes(f.Value ?? ""), hex: true);
            overrides[(r, f.ObjectNumber)] = With(Current(f.ObjectNumber), ("V", v));

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
                    int ap = FieldAppearance.Text(ctx, Math.Abs(rect[2] - rect[0]), Math.Abs(rect[3] - rect[1]), f.Value ?? "", f.FontSize, f.Multiline);
                    overrides[(r, w)] = With(widget, ("AP", new PdfDict([new("N", new PdfOutputRef(ap))])));
                }
            }
        }
        return overrides;
    }

    private static PdfDict With(PdfDict d, params (string Key, PdfValue Value)[] set)
    {
        var entries = d.Entries.Where(e => !set.Any(s => s.Key == e.Key)).ToList();
        foreach (var (k, v) in set) entries.Add(new(k, v));
        return d is PdfStream s ? new PdfStream(entries, s.Data) : new PdfDict(entries);
    }
}
