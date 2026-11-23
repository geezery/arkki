using Arkki.Tests;

BankBarcodeTests.Run();
PaymentFormatTests.Run();
Code128Tests.Run();
FinvoiceReaderTests.Run();
TrueTypeFontTests.Run();
InvoiceRenderTests.Run();
LayoutTests.Run();

Console.WriteLine($"{Assert.Passed} passed, {Assert.Failed} failed");
return Assert.Failed == 0 ? 0 : 1;
