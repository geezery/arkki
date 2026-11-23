using Arkki.Pdf.Barcodes;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>The Code 128 pattern table.</summary>
internal static class Code128Tests
{
    public static void Run()
    {
        for (int v = 0; v <= 106; v++)
            Equal($"code128 pattern {v} width", v == 106 ? 13 : 11, Code128.TotalModules(Code128.ToModules([v])));
    }
}
