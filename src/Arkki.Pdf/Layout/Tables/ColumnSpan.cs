namespace Arkki.Pdf.Layout;

/// <summary>
/// A table cell that covers several adjacent columns. Create one with
/// <see cref="ElementExtensions.SpanColumns"/>; outside a table it lays out as its content.
/// </summary>
public sealed class ColumnSpan : Element
{
    /// <summary>A cell covering <paramref name="columns"/> columns.</summary>
    public ColumnSpan(Element content, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        Content = content;
        Columns = columns;
    }

    /// <summary>The cell's content.</summary>
    public Element Content { get; }
    /// <summary>How many columns the cell covers.</summary>
    public int Columns { get; }

    /// <inheritdoc />
    public override LayoutResult Layout(double width, double height) => Content.Layout(width, height);
}
