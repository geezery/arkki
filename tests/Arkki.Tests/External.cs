using System.Diagnostics;

namespace Arkki.Tests;

/// <summary>
/// Other programs' opinion of a file Arkki wrote: qpdf (structure), pdfinfo and pdftotext (poppler), and Python's pypdf.
/// Each check runs when its tool is installed and is reported as skipped when it is not, so the suite runs anywhere and
/// checks more where it can.
/// </summary>
internal static class External
{
    public static int Skipped { get; private set; }
    private static readonly HashSet<string> Missing = [];

    /// <summary>Runs a tool on the file; null when the tool is not installed.</summary>
    public static (int Exit, string Output)? Run(string tool, byte[] pdf, params string[] args)
    {
        if (Missing.Contains(tool)) { Skipped++; return null; }
        if (tool.StartsWith("pdf", StringComparison.Ordinal) && Probed.Add(tool) && !IsPoppler(tool))
        {
            Missing.Add(tool);
            Skipped++;
            Console.WriteLine($"skip  {tool} is not poppler's: its checks are skipped");
            return null;
        }
        string path = Path.Combine(Path.GetTempPath(), $"arkki-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var psi = new ProcessStartInfo(tool)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            // Python on Windows writes in the console's code page unless told otherwise.
            psi.Environment["PYTHONUTF8"] = "1";
            foreach (string a in args) psi.ArgumentList.Add(a == "{file}" ? path : a);
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);
            return (p.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Missing.Add(tool);
            Skipped++;
            Console.WriteLine($"skip  {tool} is not installed: its checks are skipped");
            return null;
        }
        finally { File.Delete(path); }
    }

    private static readonly HashSet<string> Probed = [];

    /// <summary>
    /// True when the tool is poppler's or is not installed at all (then running it reports it missing). Git for Windows
    /// ships Xpdf's pdftotext under the same name, with other options and output, so its opinion is not asked.
    /// </summary>
    private static bool IsPoppler(string tool)
    {
        try
        {
            var psi = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-v");
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);
            return output.Contains("poppler", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    /// <summary>qpdf finds no syntax or stream errors (warnings fail too). True when qpdf is not installed.</summary>
    public static void QpdfClean(string name, byte[] pdf, string? password = null)
    {
        var r = password is null ? Run("qpdf", pdf, "--check", "{file}") : Run("qpdf", pdf, "--check", "--password=" + password, "{file}");
        if (r is null) return;
        Assert.Check($"{name}: qpdf --check", r.Value.Exit == 0 && r.Value.Output.Contains("No syntax or stream encoding errors"), r.Value.Output.Trim());
    }

    /// <summary>pdftotext's text of the file, or null when poppler is not installed.</summary>
    public static string? PopplerText(byte[] pdf, string? password = null) =>
        (password is null ? Run("pdftotext", pdf, "{file}", "-") : Run("pdftotext", pdf, "-upw", password, "{file}", "-"))?.Output;

    public static string? PdfInfo(byte[] pdf, params string[] extra) => Run("pdfinfo", pdf, [.. extra, "{file}"])?.Output;

    /// <summary>Runs a Python snippet with pypdf on the file (as sys.argv[1]); null when Python or pypdf is missing.</summary>
    public static string? PyPdf(byte[] pdf, string script)
    {
        var probe = Run("python3", pdf, "-c", "import pypdf", "{file}");
        if (probe is null || probe.Value.Exit != 0) { Skipped++; return null; }
        var r = Run("python3", pdf, "-c", "import sys, pypdf\n" + script, "{file}");
        return r?.Output;
    }

    /// <summary>Renders the first page with pdftoppm to RGB pixels; null when poppler is not installed.</summary>
    public static (int Width, int Height, byte[] Rgb)? Render(byte[] pdf, int dpi = 72)
    {
        string root = Path.Combine(Path.GetTempPath(), $"arkki-{Guid.NewGuid():N}");
        var r = Run("pdftoppm", pdf, "-r", dpi.ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "1", "-l", "1", "-singlefile", "{file}", root);
        if (r is null) return null;
        string ppm = root + ".ppm";
        if (!File.Exists(ppm)) { Assert.Check("pdftoppm renders the page", false, r.Value.Output); return null; }
        byte[] d = File.ReadAllBytes(ppm);
        File.Delete(ppm);
        // P6 header: magic, width, height, maxval, each followed by whitespace.
        int pos = 0;
        string Token()
        {
            while (char.IsWhiteSpace((char)d[pos])) pos++;
            int start = pos;
            while (!char.IsWhiteSpace((char)d[pos])) pos++;
            return System.Text.Encoding.ASCII.GetString(d, start, pos - start);
        }
        Token();
        int w = int.Parse(Token()), h = int.Parse(Token());
        Token();
        pos++;
        return (w, h, d[pos..]);
    }
}
