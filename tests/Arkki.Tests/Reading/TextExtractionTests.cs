using Arkki.Pdf;
using Arkki.Pdf.Layout;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Text comes back out of Arkki's own files and other producers' files.</summary>
internal static class TextExtractionTests
{
    public static void Run()
    {
        Equal("text of an Arkki page", "Page 1 (2)\nFirst page", PdfReader.Open(TestData.Pdf("arkki-2pages")).ExtractText(0));
        Equal("text of a Ghostscript page", "Page 2 (2)\nSecond page", PdfReader.Open(TestData.Pdf("ghostscript")).ExtractText(1));
        Equal("text of a LibreOffice page", "Written by LibreOffice.\nÄäkköset: äöå ÄÖÅ.", PdfReader.Open(TestData.Pdf("libreoffice")).ExtractText(0));
        Equal("text of all pages", "Page 1 (2)\nFirst page\n\nPage 2 (2)\nSecond page", PdfReader.Open(TestData.Pdf("objstm")).ExtractText());

        // Cells on one line are separated by a space; mixed styles join without one.
        var doc = new PdfDocument();
        var style = new TextStyle(new FontFamily(doc.AddFont(TestData.Regular), doc.AddFont(TestData.Bold)), 10);
        var table = new Table([new TableColumn(Len.Star()), new TableColumn(Len.Mm(30), HAlign.Right)]);
        table.AddRow(style, "Machining", "258,00");
        new FlowDocument
        {
            Content = new Column(
                new TextBlock([new TextSpan("Due ", style), new TextSpan("today", style with { Bold = true }), new TextSpan(".", style)]),
                table),
        }.RenderTo(doc);
        Equal("table row and rich text read back", "Due today.\nMachining 258,00", PdfReader.Open(doc.ToArray()).ExtractText(0));
    }
}
