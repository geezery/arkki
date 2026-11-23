using Arkki.Tests;

// Library
Code128Tests.Run();
BarcodeTests.Run();
TrueTypeFontTests.Run();
LayoutTests.Run();
SpanAndBreakTests.Run();
RichTextTests.Run();
PdfDocumentTests.Run();
PdfReaderTests.Run();
MergeTests.Run();
TextExtractionTests.Run();
FileSizeTests.Run();

// The Finvoice sample
BankBarcodeTests.Run();
PaymentFormatTests.Run();
FinvoiceReaderTests.Run();
InvoiceTextsTests.Run();
InvoiceRenderTests.Run();

Console.WriteLine($"{Assert.Passed} passed, {Assert.Failed} failed");
return Assert.Failed == 0 ? 0 : 1;
