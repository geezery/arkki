using Arkki.Pdf.Reading;

namespace Arkki.Pdf;

public sealed class PdfPage
{
    internal PdfPage(PdfDocument document, double width, double height)
    {
        Width = width; Height = height;
        Canvas = new PdfCanvas(document, height);
    }

    internal PdfPage(PdfDocument document, PdfReader source, int index) : this(document, 0, 0)
    {
        (Width, Height) = source.GetPageSize(index);
        Source = source;
        SourcePage = source.Pages[index];
    }

    public double Width { get; }
    public double Height { get; }

    /// <summary>Drawing surface of a page created with <see cref="PdfDocument.AddPage(double, double)"/>. Imported pages cannot be drawn on.</summary>
    public PdfCanvas Canvas { get; }

    /// <summary>True for a page copied from another PDF with <see cref="PdfDocument.ImportPages(PdfReader)"/>.</summary>
    public bool IsImported => Source is not null;

    internal PdfReader? Source { get; }
    internal ReaderPage? SourcePage { get; }
}
