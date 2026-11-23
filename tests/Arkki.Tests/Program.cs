using Arkki.Tests;

// Library
Code128Tests.Run();
TrueTypeFontTests.Run();
LayoutTests.Run();
PdfDocumentTests.Run();

// The Finvoice sample
BankBarcodeTests.Run();
PaymentFormatTests.Run();
FinvoiceReaderTests.Run();
InvoiceTextsTests.Run();
InvoiceRenderTests.Run();

Console.WriteLine($"{Assert.Passed} passed, {Assert.Failed} failed");
return Assert.Failed == 0 ? 0 : 1;
