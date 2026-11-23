namespace Arkki.Pdf.Layout;

/// <summary>
/// Finds where a word may be hyphenated. Arkki ships no language dictionaries: plug in your own rules for the
/// language you set. A soft hyphen (U+00AD) in the text is always a break point and needs no hyphenator; a word
/// that holds soft hyphens is broken only at them.
/// </summary>
public interface IHyphenator
{
    /// <summary>
    /// The positions inside <paramref name="word"/> (1 to length-1) before which a hyphen may break it,
    /// e.g. "kirjasto" → [3, 5] for "kir-jas-to". Positions out of range are ignored.
    /// </summary>
    IEnumerable<int> BreakPoints(string word);
}
