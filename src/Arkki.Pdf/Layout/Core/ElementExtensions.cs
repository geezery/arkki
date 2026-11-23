namespace Arkki.Pdf.Layout;

/// <summary>Chainable modifiers: wrap an element instead of nesting builder lambdas.</summary>
public static class ElementExtensions
{
    public static Box Padding(this Element e, double all) => new(e) { Left = all, Top = all, Right = all, Bottom = all };

    public static Box Padding(this Element e, double left, double top, double right, double bottom) =>
        new(e) { Left = left, Top = top, Right = right, Bottom = bottom };

    public static Box Background(this Element e, Color color) => new(e) { Background = color };

    public static Box Border(this Element e, Color color, double width = 0.5) => new(e) { BorderColor = color, BorderWidth = width };

    public static KeepTogether KeepTogether(this Element e) => new(e);

    /// <summary>Makes a table cell cover <paramref name="columns"/> adjacent columns.</summary>
    public static ColumnSpan SpanColumns(this Element e, int columns) => new(e, columns);
}
