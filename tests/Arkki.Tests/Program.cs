using Arkki.Tests;

// Library
Code128Tests.Run();
BarcodeTests.Run();
TrueTypeFontTests.Run();
LayoutTests.Run();
SpanAndBreakTests.Run();
RowStyleTests.Run();
RichTextTests.Run();
TextFlowTests.Run();
SigningTests.Run();
FuzzTests.Run();
ProductionTests.Run();
ValidationTests.Run();
ReadmeTests.Run();
PdfDocumentTests.Run();
PdfReaderTests.Run();
MergeTests.Run();
TextExtractionTests.Run();
FileSizeTests.Run();
EditingTests.Run();
FormTests.Run();
FormChoiceTests.Run();
EncryptionTests.Run();

// The Finvoice sample
BankBarcodeTests.Run();
PaymentFormatTests.Run();
FinvoiceReaderTests.Run();
InvoiceTextsTests.Run();
InvoiceRenderTests.Run();

Console.WriteLine($"{Assert.Passed} passed, {Assert.Failed} failed, {External.Skipped} external checks skipped");
return Assert.Failed == 0 ? 0 : 1;
