namespace Arkki.Pdf.Layout;

/// <summary>A table column.</summary>
/// <param name="Width">Fixed (<see cref="Len.Pt"/>, <see cref="Len.Mm"/>) or a share of what is left (<see cref="Len.Star"/>).</param>
/// <param name="Align">How text added to the column as strings is aligned.</param>
public sealed record TableColumn(Len Width, HAlign Align = HAlign.Left);
