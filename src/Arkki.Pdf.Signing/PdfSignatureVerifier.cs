using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;
using Arkki.Pdf.Signing.Internal;

namespace Arkki.Pdf.Signing;

/// <summary>The PAdES baseline level a signature meets, as far as this verifier checks it.</summary>
public enum PadesLevel
{
    /// <summary>Not a PAdES signature (for example adbe.pkcs7.detached) or its signature value is broken.</summary>
    None,
    /// <summary>ETSI.CAdES.detached with signing-certificate-v2 and no signing-time attribute.</summary>
    BaselineB,
    /// <summary>B plus a valid signature timestamp.</summary>
    BaselineT,
}

/// <summary>What <see cref="PdfSignatureVerifier"/> found about one signature.</summary>
public sealed record PdfSignatureInfo
{
    /// <summary>The signature field's full name.</summary>
    public required string FieldName { get; init; }
    /// <summary>The /SubFilter: ETSI.CAdES.detached for PAdES, adbe.pkcs7.detached for older CMS signatures.</summary>
    public string? SubFilter { get; init; }
    /// <summary>The signer's certificate as embedded in the signature.</summary>
    public X509Certificate2? Certificate { get; init; }
    /// <summary>The signer's name as the signature dictionary states it (/Name).</summary>
    public string? SignerName { get; init; }
    /// <summary>The reason the signer gave (/Reason).</summary>
    public string? Reason { get; init; }
    /// <summary>Where the signer says the document was signed (/Location).</summary>
    public string? Location { get; init; }
    /// <summary>The time the signer claims (/M); not proof of anything.</summary>
    public DateTimeOffset? ClaimedSigningTime { get; init; }
    /// <summary>The time a valid timestamp proves the signature existed at.</summary>
    public DateTimeOffset? TimestampTime { get; init; }
    /// <summary>The signed bytes are unchanged and the signature value matches the embedded certificate.</summary>
    public bool IsIntact { get; init; }
    /// <summary>The signature covers the whole file: nothing was appended after it (later signatures append, too).</summary>
    public bool CoversWholeDocument { get; init; }
    /// <summary>The signed attributes hold signing-certificate-v2 naming the signer's certificate, as PAdES requires.</summary>
    public bool HasSigningCertificateV2 { get; init; }
    /// <summary>The signed attributes hold a signing-time attribute, which PAdES baseline signatures must not have.</summary>
    public bool HasSigningTimeAttribute { get; init; }
    /// <summary>The signature carries an RFC 3161 signature timestamp.</summary>
    public bool HasTimestamp { get; init; }
    /// <summary>The timestamp's own signature is valid and it covers this signature's value.</summary>
    public bool TimestampValid { get; init; }
    /// <summary>The certificate chains to one of the trusted roots given; null when none were given.</summary>
    public bool? ChainTrusted { get; init; }
    /// <summary>The PAdES baseline level the signature meets.</summary>
    public PadesLevel Level { get; init; }
    /// <summary>Why the signature is not intact or not trusted, when it is not.</summary>
    public string? Problem { get; init; }
}

/// <summary>
/// Checks the signatures in a PDF: integrity of the signed bytes, the signature value, the PAdES attributes,
/// the timestamp and, given trusted roots, the certificate chain. Revocation (OCSP/CRL) is not checked.
/// </summary>
public static class PdfSignatureVerifier
{
    /// <param name="pdf">The signed file.</param>
    /// <param name="trustedRoots">Root certificates to build chains to; null skips the chain check.</param>
    public static IReadOnlyList<PdfSignatureInfo> Verify(byte[] pdf, X509Certificate2Collection? trustedRoots = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var reader = PdfReader.Open(pdf);
        var result = new List<PdfSignatureInfo>();
        if (reader.Resolve(reader.AcroForm?.Get("Fields")) is not PdfArray fields) return result;
        var seen = new HashSet<int>();
        foreach (var (name, sig) in SignatureFields(reader, fields, "", null, seen, 0))
            result.Add(Check(reader, pdf, name, sig, trustedRoots));
        return result;
    }

    private static IEnumerable<(string Name, PdfDict Sig)> SignatureFields(PdfReader reader, PdfArray kids, string prefix, string? inheritedType, HashSet<int> seen, int depth)
    {
        if (depth > 32) yield break;
        foreach (PdfValue item in kids.Items)
        {
            if (item is PdfRef r && !seen.Add(r.Number)) continue;
            if (reader.Resolve(item) is not PdfDict field) continue;
            string? partial = reader.Resolve(field.Get("T")) is PdfString t ? PdfSerializer.DecodeText(t.Bytes) : null;
            string name = partial is null ? prefix : prefix.Length == 0 ? partial : prefix + "." + partial;
            string? type = reader.Resolve(field.Get("FT")) is PdfName ft ? ft.Encoded : inheritedType;
            if (type == "Sig" && reader.Resolve(field.Get("V")) is PdfDict sig) yield return (name, sig);
            if (reader.Resolve(field.Get("Kids")) is PdfArray children)
                foreach (var found in SignatureFields(reader, children, name, type, seen, depth + 1)) yield return found;
        }
    }

    private static PdfSignatureInfo Check(PdfReader reader, byte[] pdf, string name, PdfDict sig, X509Certificate2Collection? roots)
    {
        string? Text(string key) => reader.Resolve(sig.Get(key)) is PdfString s ? PdfSerializer.DecodeText(s.Bytes) : null;
        var info = new PdfSignatureInfo
        {
            FieldName = name,
            SubFilter = (reader.Resolve(sig.Get("SubFilter")) as PdfName)?.Encoded,
            SignerName = Text("Name"),
            Reason = Text("Reason"),
            Location = Text("Location"),
            ClaimedSigningTime = PdfReader.ParseDate(Text("M")),
        };
        PdfSignatureInfo Fail(string problem) => info with { Problem = problem };

        if (reader.Resolve(sig.Get("ByteRange")) is not PdfArray { Items.Count: 4 } br || br.Items.Any(v => v is not PdfNumber))
            return Fail("The signature has no valid /ByteRange.");
        long[] range = br.Items.Select(v => ((PdfNumber)v).LongValue).ToArray();
        if (range[0] != 0 || range.Any(v => v < 0) || range[1] > range[2] || range[2] + range[3] > pdf.Length)
            return Fail("The /ByteRange lies outside the file.");
        if (pdf[range[1]] != '<' || pdf[range[2] - 1] != '>')
            return Fail("The /ByteRange gap is not exactly the /Contents string.");
        if (reader.Resolve(sig.Get("Contents")) is not PdfString contents) return Fail("The signature has no /Contents.");
        info = info with { CoversWholeDocument = range[2] + range[3] == pdf.Length };
        if (info.SubFilter is not ("ETSI.CAdES.detached" or "adbe.pkcs7.detached"))
            return Fail($"Sub-filter '{info.SubFilter}' is not supported.");

        var signed = new byte[range[1] + range[3]];
        pdf.AsSpan(0, (int)range[1]).CopyTo(signed);
        pdf.AsSpan((int)range[2], (int)range[3]).CopyTo(signed.AsSpan((int)range[1]));
        var cms = new SignedCms(new ContentInfo(signed), detached: true);
        SignerInfo signer;
        try
        {
            byte[] der = contents.Bytes;
            cms.Decode(der.AsSpan(0, Ess.EncodedLength(der)));
            signer = cms.SignerInfos[0];
            info = info with { Certificate = signer.Certificate };
            cms.CheckSignature(verifySignatureOnly: true);
        }
        catch (Exception e) when (e is CryptographicException or System.Formats.Asn1.AsnContentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Fail("The signature does not match the signed bytes: " + e.Message);
        }
        info = info with { IsIntact = true };

        X509Certificate2? cert = signer.Certificate;
        foreach (CryptographicAttributeObject a in signer.SignedAttributes)
        {
            if (a.Oid.Value == Ess.SigningTimeOid) info = info with { HasSigningTimeAttribute = true };
            if (a.Oid.Value == Ess.SigningCertificateV2Oid && cert is not null && a.Values.Count == 1 && Ess.Matches(a.Values[0].RawData, cert))
                info = info with { HasSigningCertificateV2 = true };
        }
        foreach (CryptographicAttributeObject a in signer.UnsignedAttributes)
        {
            if (a.Oid.Value != Ess.TimestampTokenOid || a.Values.Count == 0) continue;
            info = info with { HasTimestamp = true };
            if (Rfc3161TimestampToken.TryDecode(a.Values[0].RawData, out var token, out _)
                && token.VerifySignatureForSignerInfo(signer, out _))
                info = info with { TimestampValid = true, TimestampTime = token.TokenInfo.Timestamp };
        }

        if (roots is not null && cert is not null)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(roots);
            chain.ChainPolicy.ExtraStore.AddRange(cms.Certificates);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationTime = (info.TimestampTime ?? info.ClaimedSigningTime ?? DateTimeOffset.UtcNow).UtcDateTime;
            bool trusted = chain.Build(cert);
            info = info with { ChainTrusted = trusted };
            if (!trusted) info = info with { Problem = "The certificate does not chain to a trusted root: " + string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim())) };
        }

        bool baseline = info.SubFilter == "ETSI.CAdES.detached" && info.HasSigningCertificateV2 && !info.HasSigningTimeAttribute;
        return info with { Level = !baseline ? PadesLevel.None : info.TimestampValid ? PadesLevel.BaselineT : PadesLevel.BaselineB };
    }
}
