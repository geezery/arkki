using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Arkki.Pdf;
using Arkki.Pdf.Reading;
using Arkki.Pdf.Signing;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Visible signatures, signing an existing field, certification, and keys held outside the process.</summary>
internal static class VisibleSignatureTests
{
    public static void Run()
    {
        var pki = new TestPki();
        var roots = new X509Certificate2Collection(pki.Root);
        byte[] original = SigningTests.SampleDocument();
        Appearances(pki, roots, original);
        ExistingFields(pki, roots);
        Certification(pki, roots, original);
        ExternalKeys(pki, roots, original);
    }

    private static void Appearances(TestPki pki, X509Certificate2Collection roots, byte[] original)
    {
        // The default look: who, when, why and where, in Helvetica.
        var when = new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.FromHours(2));
        byte[] visible = PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions
        {
            Reason = "Hyväksytty", Location = "Helsinki", SigningTime = when,
            Appearance = new PdfSignatureAppearance { X = 50, Y = 600, Width = 220, Height = 70, Border = true },
        });
        PdfSignatureInfo s = PdfSignatureVerifier.Verify(visible, roots)[0];
        Check("visible: intact and whole", s.IsIntact && s.CoversWholeDocument, s.Problem);
        Check("visible: reported as visible", s.IsVisible);
        Equal("visible: still a baseline signature", PadesLevel.BaselineB, s.Level);
        string raw = Encoding.Latin1.GetString(visible, original.Length, visible.Length - original.Length);
        Check("visible: the box is placed from the page's top-left corner", raw.Contains("/Rect [50 171.89 270 241.89]"), "no /Rect [50 171.89 270 241.89]");
        Check("visible: names the signer", raw.Contains("(Digitally signed by Testi Allekirjoittaja) Tj"));
        Check("visible: gives the time", raw.Contains("(Date: 2026-11-24 09:30:00 +02:00) Tj"));
        Check("visible: gives reason and location", raw.Contains("(Reason: Hyväksytty) Tj") && raw.Contains("(Location: Helsinki) Tj"));
        Check("visible: has its frame", raw.Contains("0.6 0.6 0.6 RG 1 w 0.5 0.5 219 69 re S"));
        Check("an invisible signature is reported as such", !PdfSignatureVerifier.Verify(PdfSigner.Sign(original, pki.Signer))[0].IsVisible);
        External.QpdfClean("visible signature", visible);
        SigningTests.PdfSigs(visible, "visible signature", 1);
        SigningTests.PyHanko(visible, pki, "visible signature", expectTimestamp: false);
        if (External.Render(visible) is var (w, _, rgb))
        {
            bool Dark(int x0, int y0, int x1, int y1)
            {
                for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (rgb[(y * w + x) * 3] < 128) return true;
                return false;
            }
            Check("visible: the text is drawn in the box", Dark(54, 604, 266, 666));
            Check("visible: nothing is drawn outside the box", !Dark(40, 580, 280, 598) && !Dark(40, 672, 280, 690));
        }

        // An image, an embedded font and text Helvetica could not write.
        byte[] logo = File.ReadAllBytes(TestData.LogoPath);
        byte[] font = File.ReadAllBytes(Path.Combine(TestData.Root, "samples", "fonts", "LiberationSans-Regular.ttf"));
        bool drawn = false;
        byte[] rich = PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions
        {
            PageIndex = 1,
            Appearance = new PdfSignatureAppearance
            {
                X = 300, Y = 100, Width = 240, Height = 80, Image = logo, Font = font,
                Text = "Allekirjoittanut Žofia Čapek\nPäiväys 24.11.2026",
                Draw = (doc, canvas) => { drawn = true; canvas.SetStrokeColor(Color.FromHex("#1f5eff")); canvas.Line(0, 79, 240, 79); },
            },
        });
        PdfSignatureInfo r = PdfSignatureVerifier.Verify(rich, roots)[0];
        Check("image and font: intact", r.IsIntact && r.IsVisible, r.Problem);
        Check("image and font: the drawing callback ran", drawn);
        string richRaw = Encoding.Latin1.GetString(rich, original.Length, rich.Length - original.Length);
        Check("image and font: the image is in the update", richRaw.Contains("/Subtype /Image"));
        Check("image and font: the font is embedded in the update", richRaw.Contains("/FontFile2"));
        Check("image and font: the page is painted as a form", richRaw.Contains("q /Bg Do Q"));
        Equal("image and font: the document's own text is untouched", "Sivu 2", PdfReader.Open(rich).ExtractText(1).Trim());
        External.QpdfClean("signature with image and font", rich);
        SigningTests.PdfSigs(rich, "signature with image and font", 1);
        SigningTests.PyHanko(rich, pki, "signature with image and font", expectTimestamp: false);
        string? fonts = External.Run("pdffonts", rich, "{file}")?.Output;
        if (fonts is not null) Check("image and font: pdffonts sees the embedded font", fonts.Contains("LiberationSans"), fonts);

        // An image alone fills the box; text that is too long shrinks and wraps.
        byte[] stamp = PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions
        {
            Appearance = new PdfSignatureAppearance { X = 20, Y = 20, Width = 120, Height = 60, Image = logo, Text = "" },
        });
        Check("image only: intact", PdfSignatureVerifier.Verify(stamp, roots)[0].IsIntact);
        string stampRaw = Encoding.Latin1.GetString(stamp, original.Length, stamp.Length - original.Length);
        Check("image only: no text, no standard font", !stampRaw.Contains("/Helv"));
        byte[] wordy = PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions
        {
            Appearance = new PdfSignatureAppearance { X = 20, Y = 20, Width = 100, Height = 40, Text = "A long sentence that has to wrap onto several lines to fit its small box" },
        });
        string wordyRaw = Encoding.Latin1.GetString(wordy, original.Length, wordy.Length - original.Length);
        Check("long text wraps", wordyRaw.Split(" Tj T*").Length > 3, wordyRaw.Length.ToString());

        Throws<ArgumentException>("a visible signature needs a box", () => PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions { Appearance = new PdfSignatureAppearance() }));
        Throws<NotSupportedException>("an image that is no image is refused", () => PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions
        {
            Appearance = new PdfSignatureAppearance { Width = 50, Height = 50, Image = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] },
        }));
    }

    private static void ExistingFields(TestPki pki, X509Certificate2Collection roots)
    {
        var doc = new PdfDocument();
        PdfPage page = doc.AddPage();
        page.AddTextField("name", 60, 60, 200, 18, "Aino");
        page.AddSignatureField("approval", 60, 700, 200, 60);
        page.AddSignatureField("sign.manager", 300, 700, 200, 60);
        Throws<ArgumentException>("a signature field's name is unique", () => page.AddSignatureField("name", 0, 0, 10, 10));
        Throws<InvalidOperationException>("a signature field has no value to set", () => doc.Form.SetValue("approval", "x"));
        byte[] form = doc.ToArray();
        External.QpdfClean("form with signature fields", form);
        var fields = PdfReader.Open(form).GetFormFields();
        Equal("signature fields read back", "name:Text,approval:Signature,sign.manager:Signature", string.Join(",", fields.Select(f => $"{f.Name}:{f.Kind}")));
        Check("the form says it has signature fields", Encoding.Latin1.GetString(form).Contains("/SigFlags 1"));
        Equal("an empty field is not a signature", 0, PdfSignatureVerifier.Verify(form).Count);

        // Signing a field with a box draws the default look in it; no field is added.
        byte[] signed = PdfSigner.Sign(form, pki.Signer, new PdfSignatureOptions { FieldName = "approval" });
        var sigs = PdfSignatureVerifier.Verify(signed, roots);
        Equal("the existing field is signed", "approval", sigs.Single().FieldName);
        Check("existing field: intact, whole and visible", sigs[0].IsIntact && sigs[0].CoversWholeDocument && sigs[0].IsVisible, sigs[0].Problem);
        Equal("existing field: no field is added", 3, PdfReader.Open(signed).GetFormFields().Count);
        string raw = Encoding.Latin1.GetString(signed, form.Length, signed.Length - form.Length);
        Check("existing field: the default look is drawn in its box", raw.Contains("(Digitally signed by") && raw.Contains("/BBox [0 0 200 60]"), raw.Length > 4000 ? "" : raw);
        Check("existing field: its frame is kept", raw.Contains("0.6 0.6 0.6 RG 1 w 0.5 0.5 199 59 re S"));
        Equal("the other fields keep their values", "Aino", PdfReader.Open(signed).GetFormFields()[0].Value);

        // The second field, under a parent, with its own look sized by the field.
        byte[] twice = PdfSigner.Sign(signed, pki.EcSigner, new PdfSignatureOptions
        {
            FieldName = "sign.manager", Appearance = new PdfSignatureAppearance { Text = "OK", Border = true },
        });
        var both = PdfSignatureVerifier.Verify(twice, roots);
        Equal("both fields are signed", "approval,sign.manager", string.Join(",", both.Select(b => b.FieldName)));
        Check("the first signature stays intact", both[0].IsIntact && !both[0].CoversWholeDocument);
        Check("the second is intact and whole", both[1].IsIntact && both[1].CoversWholeDocument && both[1].IsVisible, both[1].Problem);
        External.QpdfClean("two signed fields", twice);
        SigningTests.PdfSigs(twice, "two signed fields", 2);
        SigningTests.PyHanko(twice, pki, "two signed fields", expectTimestamp: false);

        // A new box moves the field.
        byte[] moved = PdfSigner.Sign(form, pki.Signer, new PdfSignatureOptions
        {
            FieldName = "approval", Appearance = new PdfSignatureAppearance { X = 10, Y = 10, Width = 100, Height = 40 },
        });
        string movedRaw = Encoding.Latin1.GetString(moved, form.Length, moved.Length - form.Length);
        Check("a given box replaces the field's own", movedRaw.Contains("/Rect [10 791.89 110 831.89]") && movedRaw.Contains("/BBox [0 0 100 40]"), "rect not replaced");

        Throws<ArgumentException>("a signed field is not signed again", () => PdfSigner.Sign(signed, pki.Signer, new PdfSignatureOptions { FieldName = "approval" }));
        Throws<ArgumentException>("a text field is not a signature field", () => PdfSigner.Sign(form, pki.Signer, new PdfSignatureOptions { FieldName = "name" }));
        Throws<ArgumentException>("a new field's name has no dot", () => PdfSigner.Sign(form, pki.Signer, new PdfSignatureOptions { FieldName = "new.field" }));
    }

    private static void Certification(TestPki pki, X509Certificate2Collection roots, byte[] original)
    {
        byte[] certified = PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions { Certification = PdfCertificationLevel.FormFilling });
        PdfSignatureInfo s = PdfSignatureVerifier.Verify(certified, roots)[0];
        Check("certification: intact", s.IsIntact, s.Problem);
        Equal("certification: the level reads back", PdfCertificationLevel.FormFilling, s.Certification);
        string raw = Encoding.Latin1.GetString(certified, original.Length, certified.Length - original.Length);
        Check("certification: the catalog points at it", raw.Contains("/Perms") && raw.Contains("/DocMDP"));
        Check("certification: the transform parameters are written", raw.Contains("/TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P 2 /V /1.2 >>"));
        Equal("an ordinary signature certifies nothing", PdfCertificationLevel.None, PdfSignatureVerifier.Verify(PdfSigner.Sign(original, pki.Signer))[0].Certification);
        External.QpdfClean("certified", certified);
        SigningTests.PdfSigs(certified, "certified", 1);
        SigningTests.PyHanko(certified, pki, "certified", expectTimestamp: false);

        // An approval signature may follow; a second certification may not.
        byte[] approved = PdfSigner.Sign(certified, pki.EcSigner, new PdfSignatureOptions { FieldName = "Approver" });
        var both = PdfSignatureVerifier.Verify(approved, roots);
        Check("an approval follows a certification", both.Count == 2 && both.All(b => b.IsIntact) && both[0].Certification == PdfCertificationLevel.FormFilling);
        Throws<InvalidOperationException>("a certification is the first signature",
            () => PdfSigner.Sign(certified, pki.EcSigner, new PdfSignatureOptions { FieldName = "Second", Certification = PdfCertificationLevel.NoChanges }));
        Throws<ArgumentException>("an unknown level is refused", () => PdfSigner.Sign(original, pki.Signer, new PdfSignatureOptions { Certification = (PdfCertificationLevel)7 }));
    }

    private static void ExternalKeys(TestPki pki, X509Certificate2Collection roots, byte[] original)
    {
        // RSA: the signer hands out a certificate without a key and signs only the hash.
        IExternalSigner rsa = pki.External(pki.Signer);
        Check("the external signer's certificate has no private key", !rsa.Certificate.HasPrivateKey);
        byte[] signed = PdfSigner.SignAsync(original, rsa, new PdfSignatureOptions { Reason = "HSM" }).GetAwaiter().GetResult();
        PdfSignatureInfo s = PdfSignatureVerifier.Verify(signed, roots)[0];
        Check("external RSA: intact and whole", s.IsIntact && s.CoversWholeDocument, s.Problem);
        Check("external RSA: PAdES attributes", s.HasSigningCertificateV2 && !s.HasSigningTimeAttribute);
        Equal("external RSA: level", PadesLevel.BaselineB, s.Level);
        Equal("external RSA: chain", true, s.ChainTrusted);
        Equal("external RSA: signer name from the certificate", "Testi Allekirjoittaja", s.SignerName);
        External.QpdfClean("external RSA", signed);
        SigningTests.PdfSigs(signed, "external RSA", 1);
        SigningTests.PyHanko(signed, pki, "external RSA", expectTimestamp: false);

        // ECDSA in both forms a device may return, with SHA-384, a timestamp and a visible box.
        foreach (bool der in new[] { false, true })
        {
            string name = der ? "external ECDSA (DER)" : "external ECDSA (r and s)";
            byte[] ec = PdfSigner.SignAsync(original, pki.External(pki.EcSigner, derEcdsa: der), new PdfSignatureOptions
            {
                HashAlgorithm = HashAlgorithmName.SHA384, Timestamp = pki.TimestampAuthority(),
                Appearance = new PdfSignatureAppearance { X = 50, Y = 50, Width = 200, Height = 50 },
            }).GetAwaiter().GetResult();
            PdfSignatureInfo e = PdfSignatureVerifier.Verify(ec, roots)[0];
            Check($"{name}: intact", e.IsIntact && e.CoversWholeDocument, e.Problem);
            Equal($"{name}: timestamped", PadesLevel.BaselineT, e.Level);
            Equal($"{name}: chain", true, e.ChainTrusted);
            SigningTests.PdfSigs(ec, name, 1);
            SigningTests.PyHanko(ec, pki, name, expectTimestamp: true);
        }

        // Extra certificates are embedded, each once.
        byte[] withChain = PdfSigner.SignAsync(original, rsa, new PdfSignatureOptions { ExtraCertificates = [pki.Root, pki.Signer] }).GetAwaiter().GetResult();
        Check("external: extra certificates are embedded", PdfSignatureVerifier.Verify(withChain, roots)[0].IsIntact
            && withChain.AsSpan().IndexOf(Convert.FromHexString(Convert.ToHexString(pki.Root.RawData.AsSpan(0, 32)))) < 0); // hex inside /Contents, not raw
        string hex = Encoding.ASCII.GetString(withChain);
        Check("external: the root is in the container", hex.Contains(Convert.ToHexString(pki.Root.RawData.AsSpan(0, 48))));

        // A key that does not belong to the certificate is caught before a broken file is written.
        Throws<InvalidOperationException>("external: a wrong key is refused",
            () => PdfSigner.SignAsync(original, pki.External(pki.Signer, keyFrom: pki.Tsa)).GetAwaiter().GetResult());
        Throws<ArgumentNullException>("external: a signer is needed", () => PdfSigner.SignAsync(original, (IExternalSigner)null!).GetAwaiter().GetResult());
    }
}
