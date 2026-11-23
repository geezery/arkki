namespace Arkki.Pdf.Layout;

public sealed record TableColumn(Len Width, HAlign Align = HAlign.Left);
