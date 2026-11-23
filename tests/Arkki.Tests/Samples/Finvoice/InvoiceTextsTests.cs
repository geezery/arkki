using System.Text;
using Arkki.Samples.Finvoice;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Invoice and form texts load from JSON, and bad files fail loudly.</summary>
internal static class InvoiceTextsTests
{
    public static void Run()
    {
        Equal("built-in invoice texts language", "fi-FI", InvoiceTexts.Finnish.Language);
        Equal("built-in giro title", "TILISIIRTO. GIRERING", GiroTexts.Standard.Title);
        Equal("single label can be overridden", "Maksupäivä", (InvoiceTexts.Finnish with { DueDate = "Maksupäivä" }).DueDate);

        Throws<InvalidDataException>("unknown key is rejected", () => InvoiceTexts.Load(Json(AddKey(InvoiceJson(), "\"dueDat\": \"x\""))));
        Throws<InvalidDataException>("missing key is rejected", () => InvoiceTexts.Load(Json("{ \"language\": \"en\" }")));
        Throws<InvalidDataException>("giro line count is enforced",
            () => GiroTexts.Load(Json("{ \"payeeAccount\": [\"one line\"] }")));

        // A full copy of the built-in file loads back to equal texts.
        InvoiceTexts copy = InvoiceTexts.Load(Json(InvoiceJson()));
        Equal("invoice texts round-trip", InvoiceTexts.Finnish, copy);
    }

    private static string InvoiceJson()
    {
        using Stream s = typeof(InvoiceTexts).Assembly.GetManifestResourceStream("Arkki.Samples.Finvoice.invoice-texts-fi.json")!;
        return new StreamReader(s).ReadToEnd();
    }

    private static string AddKey(string json, string member) => json.Insert(json.IndexOf('{') + 1, member + ",");

    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    private static void Throws<T>(string name, Action action) where T : Exception
    {
        try { action(); Check(name, false, "no exception"); }
        catch (T) { Check(name, true); }
        catch (Exception e) { Check(name, false, e.GetType().Name + ": " + e.Message); }
    }
}
