namespace Arkki.Pdf.Layout;

/// <summary>Chainable modifiers: wrap an element instead of nesting builder lambdas.</summary>
public static class ElementExtensions
{
    /// <summary>Adds the same space, in points, on all four sides.</summary>
    public static Box Padding(this Element e, double all) => new(e) { Left = all, Top = all, Right = all, Bottom = all };

    /// <summary>Adds space, in points, on each side.</summary>
    public static Box Padding(this Element e, double left, double top, double right, double bottom) =>
        new(e) { Left = left, Top = top, Right = right, Bottom = bottom };

    /// <summary>Puts a fill behind the element.</summary>
    public static Box Background(this Element e, Color color) => new(e) { Background = color };

    /// <summary>Draws a border around the element.</summary>
    public static Box Border(this Element e, Color color, double width = 0.5) => new(e) { BorderColor = color, BorderWidth = width };

    /// <summary>Keeps the element on one page: moved to the next page whole rather than split.</summary>
    public static KeepTogether KeepTogether(this Element e) => new(e);

    /// <summary>Keeps the element on the same page as the start of the next element in a column.</summary>
    public static KeepWithNext KeepWithNext(this Element e) => new(e);

    /// <summary>Makes a table cell cover <paramref name="columns"/> adjacent columns.</summary>
    public static ColumnSpan SpanColumns(this Element e, int columns) => new(e, columns);

    /// <summary>Gives a table cell its own background and border over the whole cell, the row's full height.</summary>
    public static CellFill CellFill(this Element e, Color? background, Color? border = null, double borderWidth = 0.5) =>
        new(e, background, border, borderWidth);
}
