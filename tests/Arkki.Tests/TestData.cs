using Arkki.Samples.Finvoice;
using Arkki.Pdf.Fonts;

namespace Arkki.Tests;

/// <summary>The sample files under <c>samples/</c>, loaded once and shared by the tests.</summary>
internal static class TestData
{
    public static readonly string Root = FindRoot();
    public static readonly string FinvoicePath = Path.Combine(Root, "samples", "data", "finvoice-sample.xml");
    public static readonly string LogoPath = Path.Combine(Root, "samples", "data", "logo.png");
    public static readonly TrueTypeFont Regular = LoadFont("LiberationSans-Regular.ttf");
    public static readonly TrueTypeFont Bold = LoadFont("LiberationSans-Bold.ttf");

    public static FinvoiceInvoice LoadInvoice() => FinvoiceReader.Load(FinvoicePath);

    private static TrueTypeFont LoadFont(string file) =>
        TrueTypeFont.Load(File.ReadAllBytes(Path.Combine(Root, "samples", "fonts", file)));

    private static string FindRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Arkki.slnx"))) dir = Path.GetDirectoryName(dir)!;
        return dir;
    }
}
