using System.Text;

namespace Arkki.Pdf.Reading;

// The object model of a parsed PDF file. Values keep their source spelling where it matters
// (numbers, names), so an imported object is written back byte-for-byte equivalent.

internal abstract class PdfValue;

internal sealed class PdfNull : PdfValue
{
    public static readonly PdfNull Instance = new();
}

internal sealed class PdfBool(bool value) : PdfValue
{
    public static readonly PdfBool True = new(true), False = new(false);
    public bool Value { get; } = value;
}

/// <summary>A number, kept as written in the file.</summary>
internal sealed class PdfNumber(string text) : PdfValue
{
    public string Text { get; } = text;
    public double Value => double.TryParse(Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
    public int IntValue => (int)Math.Round(Value);
    public long LongValue => (long)Math.Round(Value);
}

/// <summary>A name, kept in its encoded form without the leading slash (#xx escapes included).</summary>
internal sealed class PdfName(string encoded) : PdfValue
{
    public string Encoded { get; } = encoded;
}

internal sealed class PdfString(byte[] bytes, bool hex) : PdfValue
{
    public byte[] Bytes { get; } = bytes;
    public bool Hex { get; } = hex;
}

internal sealed class PdfRef(int number, int generation) : PdfValue
{
    public int Number { get; } = number;
    public int Generation { get; } = generation;
}

internal sealed class PdfArray(List<PdfValue> items) : PdfValue
{
    public List<PdfValue> Items { get; } = items;
}

internal class PdfDict(List<KeyValuePair<string, PdfValue>> entries) : PdfValue
{
    /// <summary>Entries in file order; keys are encoded names without the slash.</summary>
    public List<KeyValuePair<string, PdfValue>> Entries { get; } = entries;

    public PdfValue? Get(string key)
    {
        foreach (var e in Entries) if (e.Key == key) return e.Value;
        return null;
    }

    public bool IsType(string type) => Get("Type") is PdfName n && n.Encoded == type;
}

/// <summary>A stream: its dictionary and the data exactly as stored (still encoded).</summary>
internal sealed class PdfStream(List<KeyValuePair<string, PdfValue>> entries, ReadOnlyMemory<byte> data) : PdfDict(entries)
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}

internal static class PdfValueExtensions
{
    public static string Describe(this PdfValue v) => v switch
    {
        PdfName n => "/" + n.Encoded,
        PdfNumber n => n.Text,
        PdfRef r => $"{r.Number} {r.Generation} R",
        PdfString s => Encoding.Latin1.GetString(s.Bytes),
        _ => v.GetType().Name,
    };
}

/// <summary>A reference to an object of the document being written (not of a read file): written as "n 0 R" as is.</summary>
internal sealed class PdfOutputRef(int id) : PdfValue
{
    public int Id { get; } = id;
}
