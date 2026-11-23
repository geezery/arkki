using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>TrueType parsing.</summary>
internal static class TrueTypeFontTests
{
    public static void Run()
    {
        var regular = Samples.Regular;
        Equal("font units per em", 2048, regular.UnitsPerEm);
        Check("font has Finnish, Estonian, Latvian and Lithuanian letters", "äöåÄÖÅõšžŠŽāēģķļņūąčęėįų€".All(c => regular.HasGlyph(c)));
        Equal("font postscript name", "LiberationSans", regular.PostScriptName);
    }
}
