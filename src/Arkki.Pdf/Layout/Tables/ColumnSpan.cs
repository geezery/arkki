namespace Arkki.Pdf.Layout;

/// <summary>
/// A table cell that covers several adjacent columns. Create one with
/// <see cref="ElementExtensions.SpanColumns"/>; outside a table it lays out as its content.
/// </summary>
public sealed class ColumnSpan : Element
{
    public ColumnSpan(Element content, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        Content = content;
        Columns = columns;
    }

    public Element Content { get; }
    public int Columns { get; }

    public override LayoutResult Layout(double width, double height) => Content.Layout(width, height);
}
