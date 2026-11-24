using System.Diagnostics;
using System.Text;
using Arkki.Pdf;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>
/// The reader against broken and hostile input: mutated real files and hand-made traps. Every case must end, in
/// time, with a result or one of the documented exceptions; never a crash, a hang or another exception type.
/// </summary>
internal static class FuzzTests
{
    private static readonly Type[] Allowed = [typeof(InvalidDataException), typeof(PdfPasswordException), typeof(NotSupportedException)];

    public static void Run()
    {
        if (Environment.GetEnvironmentVariable("ARKKI_FUZZ_REPRO") is { } repro)
        {
            byte[] data = File.ReadAllBytes(repro);
            var sw = Stopwatch.StartNew();
            var r = PdfReader.Open(data); Console.WriteLine($"open {sw.ElapsedMilliseconds} ms, {r.PageCount} pages");
            for (int p = 0; p < Math.Min(r.PageCount, 5); p++) { r.ExtractText(p); Console.WriteLine($"text {p} {sw.ElapsedMilliseconds} ms"); }
            r.GetFormFields(); Console.WriteLine($"fields {sw.ElapsedMilliseconds} ms");
            PdfDocument.Open(data).ToArray(); Console.WriteLine($"import {sw.ElapsedMilliseconds} ms");
            return;
        }
        int iterations = int.TryParse(Environment.GetEnvironmentVariable("ARKKI_FUZZ_ITERATIONS"), out int n) ? n : 150;
        var seeds = Directory.GetFiles(Path.Combine(TestData.Root, "tests", "Arkki.Tests", "Data", "Pdf"), "*.pdf")
            .Where(p => !Path.GetFileName(p).StartsWith("encrypted-rc4", StringComparison.Ordinal))
            .Select(File.ReadAllBytes).ToList();
        var failures = new List<string>();
        int fuzzSeed = int.TryParse(Environment.GetEnvironmentVariable("ARKKI_FUZZ_SEED"), out int sd) ? sd : 20261124;
        var rng = new Random(fuzzSeed);
        var watch = Stopwatch.StartNew();
        int cases = 0;
        foreach (byte[] seed in seeds)
        {
            for (int i = 0; i < iterations; i++)
            {
                byte[] mutated = Mutate(seed, rng);
                cases++;
                string? problem = Exercise(mutated);
                if (problem is not null && failures.Count < 10) failures.Add(problem);
            }
        }
        Check($"fuzz: {cases} mutated files end in a result or a documented exception", failures.Count == 0, string.Join("\n", failures));
        Console.WriteLine($"      fuzz: {cases} cases in {watch.Elapsed.TotalSeconds:0.0} s");

        foreach (var (name, data) in Traps())
        {
            string? problem = Exercise(data);
            Check($"trap: {name}", problem is null, problem);
        }
    }

    /// <summary>Runs every reading path on the file; null when it ended acceptably.</summary>
    private static string? Exercise(byte[] data)
    {
        var task = Task.Run(() =>
        {
            var reader = PdfReader.Open(data);
            for (int p = 0; p < Math.Min(reader.PageCount, 5); p++)
            {
                reader.GetPageSize(p);
                reader.GetPageRotation(p);
                reader.ExtractText(p);
            }
            _ = reader.Info.Title;
            _ = reader.Language;
            reader.GetFormFields();
            if (reader.PageCount > 0)
            {
                var doc = PdfDocument.Open(data);
                doc.ToArray();
            }
        });
        try
        {
            if (!task.Wait(TimeSpan.FromSeconds(10))) return $"hang: no result in 10 s ({Describe(data)})";
            return null;
        }
        catch (AggregateException e)
        {
            Exception inner = e.InnerException!;
            if (Allowed.Any(t => t.IsInstanceOfType(inner))) return null;
            return $"{inner.GetType().Name}: {inner.Message} ({Describe(data)})\n{inner.StackTrace?.Split('\n').Where(l => l.Contains("Arkki.Pdf")).Take(3).Aggregate("", (a, l) => a + l.Trim() + "\n")}";
        }
    }

    private static string Describe(byte[] data)
    {
        string path = Path.Combine(Path.GetTempPath(), $"arkki-fuzz-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] Mutate(byte[] seed, Random rng)
    {
        var d = new List<byte>(seed);
        int edits = rng.Next(1, 6);
        for (int e = 0; e < edits && d.Count > 0; e++)
        {
            int at = rng.Next(d.Count);
            switch (rng.Next(7))
            {
                case 0: d[at] = (byte)rng.Next(256); break;                                   // flip a byte
                case 1: d.RemoveRange(at, Math.Min(rng.Next(1, 64), d.Count - at)); break;    // cut a run
                case 2: d.RemoveRange(at, d.Count - at); break;                               // truncate
                case 3: { int from = rng.Next(d.Count); d.InsertRange(at, d.GetRange(from, Math.Min(32, d.Count - from))); break; } // duplicate
                case 4: d[at] = (byte)"0123456789"[rng.Next(10)]; break;                      // a digit: offsets, lengths, numbers
                case 5: d.InsertRange(at, Encoding.ASCII.GetBytes(rng.Next(3) switch { 0 => "<<", 1 => "[", _ => " 1 0 R" })); break;
                case 6: d[at] = (byte)" /()<>[]{}%\n"[rng.Next(12)]; break;                  // a delimiter
            }
        }
        return [.. d];
    }

    private static IEnumerable<(string, byte[])> Traps()
    {
        static byte[] Pdf(string body) => Encoding.Latin1.GetBytes("%PDF-1.7\n" + body);
        static byte[] WithXref(params string[] objects)
        {
            var b = new StringBuilder("%PDF-1.7\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Length; i++) { offsets.Add(Encoding.Latin1.GetByteCount(b.ToString())); b.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
            int xref = Encoding.Latin1.GetByteCount(b.ToString());
            b.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (int o in offsets) b.Append($"{o:D10} 00000 n \n");
            b.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.Latin1.GetBytes(b.ToString());
        }
        const string catalog = "<< /Type /Catalog /Pages 2 0 R >>";

        yield return ("empty file", []);
        yield return ("only a header", Pdf(""));
        yield return ("not a PDF", Encoding.ASCII.GetBytes("hello world, this is not a PDF at all"));
        yield return ("deeply nested arrays", WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox " + new string('[', 100_000) + new string(']', 100_000) + " >>"));
        yield return ("deeply nested dictionaries", WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /X " + string.Concat(Enumerable.Repeat("<< /A ", 50_000)) + string.Concat(Enumerable.Repeat(">> ", 50_000)) + " >>"));
        yield return ("page tree that contains itself", WithXref(catalog, "<< /Type /Pages /Kids [2 0 R 3 0 R] /Count 2 >>", "<< /Type /Page /Parent 2 0 R >>"));
        yield return ("page tree cycle through two nodes", WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Pages /Kids [2 0 R] /Count 1 >>"));
        yield return ("reference to itself", WithXref("1 0 R"));
        yield return ("huge /Count", WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 2147483647 >>", "<< /Type /Page /Parent 2 0 R >>"));
        yield return ("huge /Size in the trailer", Pdf("1 0 obj\n" + catalog + "\nendobj\nxref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 2000000000 /Root 1 0 R >>\nstartxref\n77\n%%EOF\n"));
        yield return ("xref subsection claims a billion entries", Pdf("1 0 obj\n" + catalog + "\nendobj\nxref\n0 1000000000\n0000000000 65535 f \ntrailer\n<< /Size 2 /Root 1 0 R >>\nstartxref\n52\n%%EOF\n"));
        yield return ("startxref past the end", Pdf("1 0 obj\n" + catalog + "\nendobj\nstartxref\n999999999\n%%EOF\n"));
        yield return ("negative startxref", Pdf("startxref\n-5\n%%EOF\n"));
        yield return ("stream /Length larger than the file", WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>", "<< /Length 999999999 >>\nstream\nBT ET\nendstream"));
        yield return ("negative stream /Length", WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>", "<< /Length -20 >>\nstream\nBT ET\nendstream"));
        yield return ("decompression bomb", WithFlate(Bomb()));
        yield return ("garbage in a Flate stream", WithFlate(Enumerable.Range(0, 500).Select(i => (byte)(i * 37)).ToArray()));
        yield return ("unknown filter", WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>", "<< /Length 5 /Filter /NoSuchDecode >>\nstream\nBT ET\nendstream"));
        yield return ("object stream that contains itself", WithXref(catalog, "<< /Type /Pages /Kids [] /Count 0 >>",
            "<< /Type /ObjStm /N 1 /First 4 /Length 12 /Extends 3 0 R >>\nstream\n3 0 3 0 R\nendstream"));
        yield return ("text operators with missing operands", WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>", "<< /Length 40 >>\nstream\nBT Tf Tj TJ ' \" Td TD Tm T* ET ET ET Q Q\nendstream"));
        yield return ("unterminated string", WithXref(catalog + " (never closed"));
        yield return ("unterminated dictionary at the end", Pdf("1 0 obj\n<< /Type /Catalog /Pages << /Kids [ "));
        yield return ("binary noise after the header", Pdf(Encoding.Latin1.GetString(Enumerable.Range(0, 4000).Select(i => (byte)((i * 7919) % 251)).ToArray())));

        static byte[] Bomb()
        {
            // 1 GB of zeros compresses to about a megabyte.
            using var ms = new MemoryStream();
            using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            {
                var zeros = new byte[1 << 20];
                for (int i = 0; i < 1024; i++) z.Write(zeros);
            }
            return ms.ToArray();
        }
        static byte[] WithFlate(byte[] data)
        {
            string streamObj = $"<< /Length {data.Length} /Filter /FlateDecode >>\nstream\n" + Encoding.Latin1.GetString(data) + "\nendstream";
            return WithXref(catalog, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>", streamObj);
        }
    }
}
