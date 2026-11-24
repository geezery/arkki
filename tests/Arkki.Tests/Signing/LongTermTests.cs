using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Arkki.Pdf.Reading;
using Arkki.Pdf.Signing;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>Long-term signatures: validation data (B-LT), document timestamps (B-LTA) and revocation checking.</summary>
internal static class LongTermTests
{
    public static void Run()
    {
        var pki = new TestPki();
        var roots = new X509Certificate2Collection(pki.Root);
        byte[] original = SigningTests.SampleDocument();
        byte[] stamped = Wait(PdfSigner.SignAsync(original, pki.Signer, new PdfSignatureOptions { Timestamp = pki.TimestampAuthority() }));

        byte[] archived = ValidationData(pki, roots, stamped);
        Hostile(roots, archived);
        Revoked(pki, roots, original, stamped);
        OverHttp(pki, roots, original);
    }

    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    /// <summary>A damaged signed file ends in an answer or in the reader's own exceptions, whatever byte is hit.</summary>
    private static void Hostile(X509Certificate2Collection roots, byte[] archived)
    {
        var random = new Random(20261124);
        int tail = archived.Length / 2; // the signatures, the validation data and the timestamp are in the later half
        string? broken = null;
        for (int i = 0; i < 400 && broken is null; i++)
        {
            byte[] file = (byte[])archived.Clone();
            for (int n = random.Next(1, 4); n > 0; n--) file[tail + random.Next(archived.Length - tail)] = (byte)random.Next(256);
            try { _ = PdfSignatureVerifier.Verify(file, roots); }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or Arkki.Pdf.PdfPasswordException) { }
            catch (Exception e) { broken = $"mutation {i}: {e.GetType().Name}: {e.Message}"; }
        }
        Check("verifying a damaged signed file never fails in an unexpected way", broken is null, broken);
    }

    private static byte[] ValidationData(TestPki pki, X509Certificate2Collection roots, byte[] stamped)
    {
        PdfSignatureInfo before = PdfSignatureVerifier.Verify(stamped, roots)[0];
        Equal("B-T: nothing to check revocation with", PdfRevocationStatus.NotChecked, before.Revocation);
        Equal("B-T: level", PadesLevel.BaselineT, before.Level);

        // B-LT with revocation lists.
        var lists = pki.Revocation();
        byte[] lt = Wait(PdfSigner.AddValidationDataAsync(stamped, lists, roots));
        Check("validation data is appended", lt.AsSpan(0, stamped.Length).SequenceEqual(stamped));
        PdfSignatureInfo s = PdfSignatureVerifier.Verify(lt, roots).Single();
        Check("B-LT: the signature stays intact", s.IsIntact && !s.CoversWholeDocument, s.Problem);
        Equal("B-LT: revocation", PdfRevocationStatus.Good, s.Revocation);
        Equal("B-LT: level", PadesLevel.BaselineLT, s.Level);
        Equal("B-LT: chain", true, s.ChainTrusted);
        string raw = Encoding.Latin1.GetString(lt, stamped.Length, lt.Length - stamped.Length);
        Check("B-LT: the store holds certificates and lists", raw.Contains("/Type /DSS") && raw.Contains("/Certs [") && raw.Contains("/CRLs [") && !raw.Contains("/OCSPs"));
        Check("B-LT: the responder was asked first, then the list", lists.OcspCalls > 0 && lists.CrlCalls > 0);
        External.QpdfClean("B-LT with lists", lt);
        SigningTests.PdfSigs(lt, "B-LT with lists", 1);
        PyHankoLongTerm(lt, pki, "B-LT with lists", 0);

        // B-LT with OCSP responses. A validator asks a responder only about certificates that name one, so this
        // signer's certificate does.
        byte[] stampedAddressed = Wait(PdfSigner.SignAsync(SigningTests.SampleDocument(), pki.Addressed, new PdfSignatureOptions { Timestamp = pki.TimestampAuthority() }));
        var responder = pki.Revocation(ocsp: true, crl: false);
        byte[] ltOcsp = Wait(PdfSigner.AddValidationDataAsync(stampedAddressed, responder, roots));
        PdfSignatureInfo o = PdfSignatureVerifier.Verify(ltOcsp, roots).Single();
        Equal("B-LT with OCSP: revocation", PdfRevocationStatus.Good, o.Revocation);
        Equal("B-LT with OCSP: level", PadesLevel.BaselineLT, o.Level);
        string rawOcsp = Encoding.Latin1.GetString(ltOcsp, stampedAddressed.Length, ltOcsp.Length - stampedAddressed.Length);
        Check("B-LT with OCSP: the store holds responses", rawOcsp.Contains("/OCSPs [") && !rawOcsp.Contains("/CRLs"));
        Equal("B-LT with OCSP: no list was needed", 0, responder.CrlCalls);
        External.QpdfClean("B-LT with OCSP", ltOcsp);
        PyHankoLongTerm(ltOcsp, pki, "B-LT with OCSP", 0);

        // Adding again fetches nothing and keeps what is there.
        var again = pki.Revocation();
        byte[] twice = Wait(PdfSigner.AddValidationDataAsync(lt, again, roots));
        Equal("data already in the file is not fetched again", 0, again.OcspCalls + again.CrlCalls);
        Equal("B-LT twice: still long-term", PadesLevel.BaselineLT, PdfSignatureVerifier.Verify(twice, roots).Single().Level);
        External.QpdfClean("B-LT twice", twice);

        // B-LTA: a document timestamp over the signature and its validation data.
        byte[] lta = Wait(PdfSigner.AddDocumentTimestampAsync(lt, pki.TimestampAuthority()));
        var all = PdfSignatureVerifier.Verify(lta, roots);
        Equal("B-LTA: a signature and a timestamp", "Signature1,Timestamp1", string.Join(",", all.Select(a => a.FieldName)));
        Equal("B-LTA: the signature's level", PadesLevel.BaselineLTA, all[0].Level);
        PdfSignatureInfo ts = all[1];
        Check("document timestamp: intact and whole", ts.IsDocumentTimestamp && ts.IsIntact && ts.CoversWholeDocument, ts.Problem);
        Check("document timestamp: proves a time", ts.TimestampTime is { } t && Math.Abs((t - DateTimeOffset.UtcNow).TotalMinutes) < 5);
        Equal("document timestamp: sub-filter", "ETSI.RFC3161", ts.SubFilter);
        Equal("document timestamp: the authority's certificate", pki.Tsa.Thumbprint, ts.Certificate?.Thumbprint);
        Equal("document timestamp: chain", true, ts.ChainTrusted);
        Check("document timestamp: not a signature level", ts.Level == PadesLevel.None && !ts.IsVisible);
        Check("the signature under it stays intact", all[0].IsIntact && !all[0].CoversWholeDocument);
        External.QpdfClean("B-LTA", lta);
        PyHankoLongTerm(lta, pki, "B-LTA", 1);

        byte[] renewed = Wait(PdfSigner.AddDocumentTimestampAsync(lta, pki.TimestampAuthority()));
        Equal("a second document timestamp gets the next name", "Signature1,Timestamp1,Timestamp2",
            string.Join(",", PdfSignatureVerifier.Verify(renewed, roots).Select(a => a.FieldName)));
        byte[] tampered = (byte[])lta.Clone();
        tampered[stampedOffset(lta)] ^= 1;
        Check("a changed file breaks its document timestamp", !PdfSignatureVerifier.Verify(tampered, roots)[^1].IsIntact);
        Throws<InvalidOperationException>("a timestamp for another hash is refused",
            () => Wait(PdfSigner.AddDocumentTimestampAsync(lt, pki.TimestampAuthority(corrupt: true))));
        Throws<ArgumentException>("a taken timestamp field name is refused", () => Wait(PdfSigner.AddDocumentTimestampAsync(lta, pki.TimestampAuthority(), "Timestamp1")));

        // What cannot be done is said.
        Throws<InvalidOperationException>("validation data needs a signature", () => Wait(PdfSigner.AddValidationDataAsync(SigningTests.SampleDocument(), lists, roots)));
        Throws<InvalidOperationException>("validation data needs the issuer's certificate", () => Wait(PdfSigner.AddValidationDataAsync(stamped, lists)));
        Throws<InvalidOperationException>("validation data needs an answer", () => Wait(PdfSigner.AddValidationDataAsync(stamped, pki.Revocation(crl: false), roots)));
        Throws<InvalidOperationException>("a list from another authority is no answer",
            () => Wait(PdfSigner.AddValidationDataAsync(stamped, new TestPki().Revocation(), roots)));

        return lta;

        // A byte inside the DSS update, after the signed revision.
        static int stampedOffset(byte[] file) => file.AsSpan().LastIndexOf("/Type /DSS"u8) + 3;
    }

    private static void Revoked(TestPki pki, X509Certificate2Collection roots, byte[] original, byte[] stamped)
    {
        // Revoked before the signature was made: the file says so, and it is not long-term.
        foreach (bool ocsp in new[] { false, true })
        {
            string kind = ocsp ? "OCSP" : "list";
            var early = pki.Revocation(ocsp, !ocsp, DateTimeOffset.UtcNow.AddHours(-2), pki.Signer);
            byte[] bad = Wait(PdfSigner.AddValidationDataAsync(stamped, early, roots));
            PdfSignatureInfo s = PdfSignatureVerifier.Verify(bad, roots).Single();
            Equal($"revoked before signing ({kind}): status", PdfRevocationStatus.Revoked, s.Revocation);
            Check($"revoked before signing ({kind}): the problem names the certificate", s.Problem?.Contains("Testi Allekirjoittaja") == true && s.Problem.Contains("revoked"), s.Problem);
            Equal($"revoked before signing ({kind}): not long-term", PadesLevel.BaselineT, s.Level);
            Check($"revoked before signing ({kind}): the bytes are still intact", s.IsIntact);
        }

        // Revoked after a time the timestamp proves: the signature was made with a good certificate.
        var late = pki.Revocation(revokedAt: DateTimeOffset.UtcNow.AddHours(1), revoked: pki.Signer);
        PdfSignatureInfo after = PdfSignatureVerifier.Verify(Wait(PdfSigner.AddValidationDataAsync(stamped, late, roots)), roots).Single();
        Equal("revoked after the proven time: good", PdfRevocationStatus.Good, after.Revocation);
        Equal("revoked after the proven time: long-term", PadesLevel.BaselineLT, after.Level);

        // Without a timestamp the claimed time proves nothing, so a later revocation counts.
        byte[] plain = PdfSigner.Sign(original, pki.Signer);
        PdfSignatureInfo unproven = PdfSignatureVerifier.Verify(Wait(PdfSigner.AddValidationDataAsync(plain, late, roots)), roots).Single();
        Equal("revoked after a merely claimed time: revoked", PdfRevocationStatus.Revoked, unproven.Revocation);
        Equal("validation data without a timestamp is not B-LT", PadesLevel.BaselineB, unproven.Level);

        // A revoked timestamp authority spoils the proof.
        var tsaRevoked = pki.Revocation(revokedAt: DateTimeOffset.UtcNow.AddHours(-2), revoked: pki.Tsa);
        PdfSignatureInfo spoiled = PdfSignatureVerifier.Verify(Wait(PdfSigner.AddValidationDataAsync(stamped, tsaRevoked, roots)), roots).Single();
        Equal("a revoked timestamp authority: revoked", PdfRevocationStatus.Revoked, spoiled.Revocation);

        // Fetching while verifying: the file has no data, the client has.
        var online = pki.Revocation(ocsp: true);
        var options = new PdfVerificationOptions { TrustedRoots = roots, Revocation = online };
        PdfSignatureInfo fetched = Wait(PdfSignatureVerifier.VerifyAsync(stamped, options)).Single();
        Equal("fetched revocation: good", PdfRevocationStatus.Good, fetched.Revocation);
        Equal("fetched revocation does not make the file long-term", PadesLevel.BaselineT, fetched.Level);
        Check("fetched revocation: the responder was asked", online.OcspCalls > 0);
        var onlineRevoked = new PdfVerificationOptions { TrustedRoots = roots, Revocation = pki.Revocation(revokedAt: DateTimeOffset.UtcNow.AddHours(-2), revoked: pki.Signer) };
        Equal("fetched revocation: revoked", PdfRevocationStatus.Revoked, Wait(PdfSignatureVerifier.VerifyAsync(stamped, onlineRevoked)).Single().Revocation);
        var noRoots = new PdfVerificationOptions { Revocation = pki.Revocation() };
        Equal("without the issuer's certificate nothing can be asked", PdfRevocationStatus.NotChecked, Wait(PdfSignatureVerifier.VerifyAsync(stamped, noRoots)).Single().Revocation);
        var deaf = new PdfVerificationOptions { TrustedRoots = roots, Revocation = pki.Revocation(crl: false) };
        Equal("a client with no answer leaves it unchecked", PdfRevocationStatus.NotChecked, Wait(PdfSignatureVerifier.VerifyAsync(stamped, deaf)).Single().Revocation);
    }

    /// <summary>The HTTP client: addresses come from the certificate, requests go out as the protocols say.</summary>
    private static void OverHttp(TestPki pki, X509Certificate2Collection roots, byte[] original)
    {
        Equal("OCSP address from the certificate", TestPki.OcspUrl, string.Join(",", HttpRevocationClient.OcspUrls(pki.Addressed)));
        Equal("list address from the certificate", TestPki.CrlUrl, string.Join(",", HttpRevocationClient.CrlUrls(pki.Addressed)));
        Equal("a certificate without addresses has none", 0, HttpRevocationClient.OcspUrls(pki.Signer).Count + HttpRevocationClient.CrlUrls(pki.Signer).Count);

        byte[] signed = PdfSigner.Sign(original, pki.Addressed);
        var handler = new FakeAuthority(pki, ocspWorks: true);
        using (var http = new HttpClient(handler))
        {
            byte[] lt = Wait(PdfSigner.AddValidationDataAsync(signed, new HttpRevocationClient(http), roots));
            Equal("over HTTP: revocation", PdfRevocationStatus.Good, PdfSignatureVerifier.Verify(lt, roots).Single().Revocation);
            Equal("over HTTP: the responder got an OCSP request", "POST application/ocsp-request " + TestPki.OcspUrl, handler.Log.Single());
        }

        // A responder that is down: the list is fetched instead, here served as PEM text.
        var down = new FakeAuthority(pki, ocspWorks: false);
        using (var http = new HttpClient(down))
        {
            byte[] lt = Wait(PdfSigner.AddValidationDataAsync(signed, new HttpRevocationClient(http), roots));
            Equal("over HTTP with the responder down: revocation", PdfRevocationStatus.Good, PdfSignatureVerifier.Verify(lt, roots).Single().Revocation);
            Equal("over HTTP with the responder down: the list is fetched", "GET " + TestPki.CrlUrl, down.Log.Last());
            var client = new HttpRevocationClient(http);
            Check("a certificate without addresses gets no answer", Wait(client.GetCrlAsync(pki.Signer)) is null && Wait(client.GetOcspResponseAsync(pki.Signer, pki.Root)) is null);
        }
        Throws<ArgumentNullException>("the HTTP client needs a client", () => new HttpRevocationClient(null!));
    }

    private sealed class FakeAuthority(TestPki pki, bool ocspWorks) : HttpMessageHandler
    {
        public List<string> Log { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Log.Add($"POST {request.Content?.Headers.ContentType?.MediaType} {request.RequestUri}");
                if (!ocspWorks) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pki.Ocsp(body)) };
            }
            Log.Add($"GET {request.RequestUri}");
            string pem = "-----BEGIN X509 CRL-----\n" + Convert.ToBase64String(pki.Crl(), Base64FormattingOptions.InsertLineBreaks) + "\n-----END X509 CRL-----\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(pem) };
        }
    }

    /// <summary>
    /// pyHanko, offline and with revocation required: every signature and document timestamp validates with the
    /// certificates, lists and responses in the file's own store and nothing else.
    /// </summary>
    private static void PyHankoLongTerm(byte[] pdf, TestPki pki, string name, int timestamps)
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
from pyhanko.sign.validation import validate_pdf_signature, validate_pdf_timestamp
from pyhanko.sign.validation.dss import DocumentSecurityStore
root = x509.Certificate.load(pem.unarmor(open(sys.argv[2], 'rb').read())[2])
with open(sys.argv[1], 'rb') as f:
    r = PdfFileReader(f)
    dss = DocumentSecurityStore.read_dss(r)
    def vc():
        return dss.as_validation_context({'trust_roots': [root], 'allow_fetching': False, 'revocation_mode': 'require'})
    for s in r.embedded_regular_signatures:
        st = validate_pdf_signature(s, vc(), ts_validation_context=vc())
        ts = st.timestamp_validity
        print('SIG', s.field_name, st.intact, st.valid, st.trusted, ts is not None and ts.valid and ts.trusted)
    for s in r.embedded_timestamp_signatures:
        st = validate_pdf_timestamp(s, vc())
        print('TS', s.field_name, st.intact, st.valid, st.trusted)
""";
            var r = External.Run("python3", pdf, "-c", script, "{file}", rootPath);
            if (r is null) return;
            string[] lines = r.Value.Output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            string[] sigs = lines.Where(l => l.StartsWith("SIG ", StringComparison.Ordinal)).ToArray();
            string[] stamps = lines.Where(l => l.StartsWith("TS ", StringComparison.Ordinal)).ToArray();
            Check($"{name}: pyHanko validates the signature with the file's own revocation data, revocation required",
                sigs.Length == 1 && sigs[0].EndsWith("True True True True", StringComparison.Ordinal), r.Value.Output);
            Check($"{name}: pyHanko validates {timestamps} document timestamp(s) the same way",
                stamps.Length == timestamps && stamps.All(l => l.EndsWith("True True True", StringComparison.Ordinal)), r.Value.Output);
        }
        finally { File.Delete(rootPath); }
    }
}
