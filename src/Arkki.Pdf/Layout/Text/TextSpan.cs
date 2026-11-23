namespace Arkki.Pdf.Layout;

/// <summary>A run of text in one style, for paragraphs that mix styles: <c>new TextBlock([new("Due ", body), new("today", body with { Bold = true })])</c>.</summary>
public sealed record TextSpan(string Text, TextStyle Style);
