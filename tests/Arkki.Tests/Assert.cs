namespace Arkki.Tests;

/// <summary>A dependency-free test runner: the library has no NuGet dependencies and neither do its tests.</summary>
internal static class Assert
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }

    public static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { Passed++; return; }
        Failed++;
        Console.WriteLine($"FAIL  {name}{(detail is null ? "" : "  -> " + detail)}");
    }

    public static void Equal<T>(string name, T expected, T actual) =>
        Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected '{expected}', got '{actual}'");
}
