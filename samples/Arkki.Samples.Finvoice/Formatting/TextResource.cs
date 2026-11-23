using System.Text.Json;

namespace Arkki.Samples.Finvoice;

/// <summary>
/// Reads a flat JSON object of texts: every value is a string or an array of strings. Unknown and
/// missing keys are errors, so a typo in a translation fails when the file is loaded rather than
/// printing an empty label on an invoice.
/// </summary>
internal sealed class TextResource : IDisposable
{
    private readonly JsonDocument _json;
    private readonly HashSet<string> _unused;
    private readonly string _name;

    private TextResource(JsonDocument json, string name)
    {
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{name}: expected a JSON object.");
        _json = json;
        _name = name;
        _unused = [.. json.RootElement.EnumerateObject().Select(p => p.Name)];
    }

    public static TextResource Parse(Stream json, string name) =>
        new(JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }), name);

    public static Stream OpenEmbedded(string fileName) =>
        typeof(TextResource).Assembly.GetManifestResourceStream("Arkki.Samples.Finvoice." + fileName)
        ?? throw new InvalidOperationException($"Embedded text resource '{fileName}' is missing.");

    public string String(string key) =>
        Get(key) is { ValueKind: JsonValueKind.String } v ? v.GetString()! : throw Invalid(key, "a string");

    public string[] Lines(string key, int count)
    {
        JsonElement v = Get(key);
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != count || v.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
            throw Invalid(key, $"an array of {count} strings");
        return [.. v.EnumerateArray().Select(e => e.GetString()!)];
    }

    /// <summary>Throws if the file has keys that were never read.</summary>
    public void EnsureAllUsed()
    {
        if (_unused.Count > 0)
            throw new InvalidDataException($"{_name}: unknown key(s) {string.Join(", ", _unused.Order())}.");
    }

    public void Dispose() => _json.Dispose();

    private JsonElement Get(string key)
    {
        if (!_json.RootElement.TryGetProperty(key, out JsonElement v))
            throw new InvalidDataException($"{_name}: missing key '{key}'.");
        _unused.Remove(key);
        return v;
    }

    private InvalidDataException Invalid(string key, string expected) => new($"{_name}: '{key}' must be {expected}.");
}
