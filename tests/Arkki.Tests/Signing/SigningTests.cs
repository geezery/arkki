using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Arkki.Pdf;
using Arkki.Pdf.Reading;
using Arkki.Pdf.Signing;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>PAdES signing and verification against a PKI made in the test, checked by .NET, pdfsig and pyHanko.</summary>
internal static class SigningTests
{
    public static void Run()
    {
        var pki = new TestPki();
        var roots = new X509Certificate2Collection(pki.Root);
        byte[] original = SampleDocument();

        // ---- B-B ----
        var when = new DateTimeOffset(2026, 11, 24, 3, 0, 0, TimeSpan.FromHours(2));
        byte[] signed = PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions { Reason = "Hyväksytty", Location = "Helsinki", SigningTime = when });
        Check("signing appends: the original bytes are a prefix", signed.AsSpan(0, original.Length).SequenceEqual(original));
        var sigs = PdfSignatureVerifier.Verify(signed, roots);
        Equal("one signature", 1, sigs.Count);
        PdfSignatureInfo s = sigs[0];
        Check("B-B: intact", s.IsIntact, s.Problem);
        Check("B-B: covers the whole file", s.CoversWholeDocument);
        Check("B-B: signing-certificate-v2 binds the certificate", s.HasSigningCertificateV2);
        Check("B-B: no signing-time attribute", !s.HasSigningTimeAttribute);
        Equal("B-B: level", PadesLevel.BaselineB, s.Level);
        Equal("B-B: chain to the test root", true, s.ChainTrusted);
        Equal("B-B: field name", "Signature1", s.FieldName);
        Equal("B-B: sub-filter", "ETSI.CAdES.detached", s.SubFilter);
        Equal("B-B: reason survives Unicode", "Hyväksytty", s.Reason);
        Equal("B-B: signer name from the certificate", "Testi Allekirjoittaja", s.SignerName);
        Equal("B-B: claimed time", when, s.ClaimedSigningTime);
        Equal("B-B: no chain check without roots", null, PdfSignatureVerifier.Verify(signed)[0].ChainTrusted);
        Equal("B-B: an unrelated root does not trust it", false, PdfSignatureVerifier.Verify(signed, new X509Certificate2Collection(new TestPki().Root))[0].ChainTrusted);

        // The signed file is still a readable PDF with the field listed.
        var reader = PdfReader.Open(signed);
        Equal("signed file keeps its pages", 2, reader.PageCount);
        Check("signed file text is unchanged", reader.ExtractText(0).Contains("Sopimus"));
        Check("signature field is listed", reader.GetFormFields().Any(f => f.Name == "Signature1"));
        External.QpdfClean("signed B-B", signed);

        // Tampering with any signed byte breaks it.
        byte[] tampered = (byte[])signed.Clone();
        int at = Array.IndexOf(tampered, (byte)'S', 200);
        tampered[at] = (byte)'X';
        Check("tampered file: not intact", !PdfSignatureVerifier.Verify(tampered, roots)[0].IsIntact);
        byte[] swapped = (byte[])signed.Clone();
        int contents = IndexOf(swapped, "/Contents <"u8) + 11;
        swapped[contents + 200] = swapped[contents + 200] == (byte)'A' ? (byte)'B' : (byte)'A';
        Check("damaged signature value: not intact", !PdfSignatureVerifier.Verify(swapped, roots)[0].IsIntact);

        // Something appended after signing is visible.
        byte[] appended = [.. signed, .. "\n% appended\n"u8];
        var after = PdfSignatureVerifier.Verify(appended, roots)[0];
        Check("appended bytes: still intact but not whole", after.IsIntact && !after.CoversWholeDocument);

        // ---- a second signature, ECDSA and SHA-384 ----
        byte[] twice = PdfSigner.Sign(signed, pki.EcSigner, new PdfSignatureOptions { FieldName = "Approver", HashAlgorithm = HashAlgorithmName.SHA384, PageIndex = 1 });
        var both = PdfSignatureVerifier.Verify(twice, roots);
        Equal("two signatures", 2, both.Count);
        Check("first signature stays intact under the second", both[0].IsIntact && !both[0].CoversWholeDocument);
        Check("second (ECDSA, SHA-384) is intact and whole", both[1].IsIntact && both[1].CoversWholeDocument && both[1].ChainTrusted == true, both[1].Problem);
        Throws<ArgumentException>("a taken field name is refused", () => PdfSigner.Sign(signed, pki.Signer));
        External.QpdfClean("signed twice", twice);

        // ---- B-T with the in-memory TSA ----
        byte[] stamped = PdfSigner.SignAsync(original, pki.Signer, new PdfSignatureOptions { Timestamp = pki.TimestampAuthority() }).GetAwaiter().GetResult();
        var t = PdfSignatureVerifier.Verify(stamped, roots)[0];
        Check("B-T: intact", t.IsIntact, t.Problem);
        Check("B-T: timestamp valid", t.HasTimestamp && t.TimestampValid);
        Equal("B-T: level", PadesLevel.BaselineT, t.Level);
        Check("B-T: timestamp time is now", t.TimestampTime is { } ts && Math.Abs((ts - DateTimeOffset.UtcNow).TotalMinutes) < 5, t.TimestampTime?.ToString());
        Throws<InvalidOperationException>("a timestamp for another hash is refused",
            () => PdfSigner.SignAsync(original, pki.Signer, new PdfSignatureOptions { Timestamp = pki.TimestampAuthority(corrupt: true) }).GetAwaiter().GetResult());
        Throws<InvalidOperationException>("Sign refuses a timestamp (it is async)", () => PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions { Timestamp = pki.TimestampAuthority() }));

        // An expired certificate is caught by the chain check at the claimed time.
        byte[] byExpired = PdfSigner.Sign(original, pki.Expired);
        var ex = PdfSignatureVerifier.Verify(byExpired, roots)[0];
        Check("expired certificate: intact but not trusted", ex.IsIntact && ex.ChainTrusted == false && ex.Problem is not null);

        // ---- input checks ----
        using (var noKey = X509CertificateLoader.LoadCertificate(pki.Signer.RawData))
            Throws<ArgumentException>("a certificate without a private key is refused", () => PdfSigner.Sign(original, noKey));
        Throws<ArgumentException>("a page that does not exist is refused", () => PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions { PageIndex = 5 }));
        Throws<ArgumentException>("MD5 is refused", () => PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions { HashAlgorithm = HashAlgorithmName.MD5 }));
        Throws<InvalidOperationException>("a reserved size too small is reported", () => PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions { ReservedSize = 100 }));
        var encrypted = new PdfDocument { Encryption = new PdfEncryption("pw") };
        encrypted.AddPage();
        Throws<NotSupportedException>("an encrypted PDF is refused", () => PdfSigner.Sign(encrypted.ToArray(), pki.Signer));
        Equal("an unsigned file has no signatures", 0, PdfSignatureVerifier.Verify(original).Count);

        // A file whose cross-reference is a stream gets a stream update.
        var objstm = External.Run("qpdf", original, "--object-streams=generate", "{file}", "-");
        if (objstm is { Exit: 0 })
        {
            string path = Path.Combine(Path.GetTempPath(), $"arkki-{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(path, original);
            var psi = new System.Diagnostics.ProcessStartInfo("qpdf") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (string a in new[] { "--object-streams=generate", path, path + ".out" }) psi.ArgumentList.Add(a);
            using (var p = System.Diagnostics.Process.Start(psi)!) p.WaitForExit();
            byte[] streamed = File.ReadAllBytes(path + ".out");
            File.Delete(path); File.Delete(path + ".out");
            byte[] signedStreamed = PdfSigner.Sign(streamed, pki.Signer);
            Check("xref-stream file: signature intact", PdfSignatureVerifier.Verify(signedStreamed, roots)[0].IsIntact);
            Check("xref-stream file: update uses an xref stream", IndexOf(signedStreamed.AsSpan(streamed.Length).ToArray(), "/Type /XRef"u8) >= 0);
            External.QpdfClean("signed xref-stream file", signedStreamed);
            PdfSigs(signedStreamed, "xref-stream file", 1);
        }

        // ---- external validators ----
        PdfSigs(signed, "B-B", 1);
        PdfSigs(twice, "two signatures", 2);
        PdfSigs(stamped, "B-T", 1);
        PyHanko(signed, pki, "B-B", expectTimestamp: false);
        PyHanko(stamped, pki, "B-T", expectTimestamp: true);
        PyHanko(twice, pki, "two signatures", expectTimestamp: false);
    }

    private static byte[] SampleDocument()
    {
        var doc = new PdfDocument();
        PdfFont font = doc.AddFont(TestData.Regular);
        doc.AddPage().Canvas.Text(font, 14, 50, 80, "Sopimus: allekirjoitettava asiakirja");
        doc.AddPage().Canvas.Text(font, 14, 50, 80, "Sivu 2");
        return doc.ToArray();
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> what) => data.AsSpan().IndexOf(what);

    /// <summary>poppler's pdfsig: every signature's value checks out (the test CA is not trusted there).</summary>
    private static void PdfSigs(byte[] pdf, string name, int expected)
    {
        var r = External.Run("pdfsig", pdf, "-nocert", "{file}");
        if (r is null) return;
        int valid = r.Value.Output.Split("Signature Validation: Signature is Valid.").Length - 1;
        Check($"{name}: pdfsig finds {expected} valid signature(s)", valid == expected, r.Value.Output);
    }

    /// <summary>pyHanko: intact, valid, trusted to the test root, and the timestamp valid and trusted.</summary>
    private static void PyHanko(byte[] pdf, TestPki pki, string name, bool expectTimestamp)
    {
        var probe = External.Run("python3", pdf, "-c", "import pyhanko.sign.validation", "{file}");
        if (probe is null || probe.Value.Exit != 0) { Console.WriteLine("skip  pyHanko is not installed: its checks are skipped"); return; }
        string rootPath = Path.Combine(Path.GetTempPath(), $"arkki-root-{Guid.NewGuid():N}.pem");
        File.WriteAllText(rootPath, pki.RootPem);
        try
        {
            const string script = """
import sys
from asn1crypto import pem, x509
from pyhanko.pdf_utils.reader import PdfFileReader
from pyhanko.sign.validation import validate_pdf_signature
from pyhanko_certvalidator import ValidationContext
root = x509.Certificate.load(pem.unarmor(open(sys.argv[2], 'rb').read())[2])
vc = ValidationContext(trust_roots=[root], allow_fetching=False, revocation_mode='soft-fail')
with open(sys.argv[1], 'rb') as f:
    for s in PdfFileReader(f).embedded_signatures:
        st = validate_pdf_signature(s, vc, ts_validation_context=vc)
        ts = st.timestamp_validity
        print(s.field_name, st.intact, st.valid, st.trusted, ts is not None and ts.valid and ts.trusted)
""";
            var r = External.Run("python3", pdf, "-c", script, "{file}", rootPath);
            if (r is null) return;
            string[] lines = r.Value.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.Contains("Warning", StringComparison.OrdinalIgnoreCase)).ToArray();
            Check($"{name}: pyHanko validates every signature as intact, valid and trusted",
                lines.Length > 0 && lines.All(l => l.Split(' ') is [_, "True", "True", "True", _]), r.Value.Output);
            if (expectTimestamp) Check($"{name}: pyHanko validates the timestamp", lines.All(l => l.EndsWith("True")), r.Value.Output);
        }
        finally { File.Delete(rootPath); }
    }
}
