using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;
using Arkki.Pdf.Signing.Internal;

namespace Arkki.Pdf.Signing;

/// <summary>
/// Checks the signatures and document timestamps in a PDF: integrity of the signed bytes, the signature value, the
/// PAdES attributes and level, the timestamp, revocation and, given trusted roots, the certificate chain.
/// <para>Revocation is checked with the lists and OCSP responses the file carries (its document security store);
/// <see cref="VerifyAsync"/> can fetch what is missing. Certificate policies and the freshness of revocation
/// information are not judged.</para>
/// </summary>
public static class PdfSignatureVerifier
{
    /// <summary>Verifies with the file's own data only; nothing is fetched.</summary>
    /// <param name="pdf">The signed file.</param>
    /// <param name="trustedRoots">Root certificates to build chains to; null skips the chain check.</param>
    public static IReadOnlyList<PdfSignatureInfo> Verify(byte[] pdf, X509Certificate2Collection? trustedRoots = null) =>
        // Without a revocation client nothing is awaited: the task is already complete.
        VerifyAsync(pdf, new PdfVerificationOptions { TrustedRoots = trustedRoots }).GetAwaiter().GetResult();

    /// <summary>Verifies, fetching revocation information the file lacks when <see cref="PdfVerificationOptions.Revocation"/> is set.</summary>
    public static async Task<IReadOnlyList<PdfSignatureInfo>> VerifyAsync(byte[] pdf, PdfVerificationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);
        var reader = PdfReader.Open(pdf);
        Dss dss = Dss.Read(reader);
        var checkedSignatures = new List<(PdfSignatureInfo Info, long End)>();
        foreach (var (name, field, sig) in SignatureFields.Find(reader))
            checkedSignatures.Add(await CheckAsync(reader, pdf, name, field, sig, dss, options, cancellationToken).ConfigureAwait(false));

        // B-LTA: a valid document timestamp made after the signature protects it and its validation data.
        var result = new List<PdfSignatureInfo>(checkedSignatures.Count);
        foreach (var (info, end) in checkedSignatures)
        {
            bool archived = info.Level == PadesLevel.BaselineLT
                && checkedSignatures.Any(t => t.Info.IsDocumentTimestamp && t.Info.IsIntact && t.End > end);
            result.Add(archived ? info with { Level = PadesLevel.BaselineLTA } : info);
        }
        return result;
    }

    private static async Task<(PdfSignatureInfo Info, long End)> CheckAsync(PdfReader reader, byte[] pdf, string name, PdfDict field, PdfDict sig,
        Dss dss, PdfVerificationOptions options, CancellationToken ct)
    {
        string? Text(string key) => reader.Resolve(sig.Get(key)) is PdfString s ? PdfSerializer.DecodeText(s.Bytes) : null;
        bool docTimestamp = SignatureFields.IsDocumentTimestamp(reader, sig);
        var info = new PdfSignatureInfo
        {
            FieldName = name,
            SubFilter = (reader.Resolve(sig.Get("SubFilter")) as PdfName)?.Encoded,
            IsDocumentTimestamp = docTimestamp,
            SignerName = Text("Name"),
            Reason = Text("Reason"),
            Location = Text("Location"),
            ClaimedSigningTime = PdfReader.ParseDate(Text("M")),
            IsVisible = IsVisible(reader, field),
            Certification = Certification(reader, sig),
        };
        (PdfSignatureInfo, long) Fail(string problem, long end = 0) => (info with { Problem = problem }, end);

        if (reader.Resolve(sig.Get("ByteRange")) is not PdfArray { Items.Count: 4 } br || br.Items.Any(v => v is not PdfNumber))
            return Fail("The signature has no valid /ByteRange.");
        long[] range = br.Items.Select(v => ((PdfNumber)v).LongValue).ToArray();
        if (range[0] != 0 || range.Any(v => v < 0) || range[1] >= range[2] || range[2] > pdf.Length || range[3] > pdf.Length - range[2])
            return Fail("The /ByteRange lies outside the file.");
        if (pdf[range[1]] != '<' || pdf[range[2] - 1] != '>')
            return Fail("The /ByteRange gap is not exactly the /Contents string.");
        long end = range[2] + range[3];
        if (reader.Resolve(sig.Get("Contents")) is not PdfString contents) return Fail("The signature has no /Contents.", end);
        info = info with { CoversWholeDocument = end == pdf.Length };
        if (!docTimestamp && info.SubFilter is not ("ETSI.CAdES.detached" or "adbe.pkcs7.detached"))
            return Fail($"Sub-filter '{info.SubFilter}' is not supported.", end);

        var signed = new byte[range[1] + range[3]];
        pdf.AsSpan(0, (int)range[1]).CopyTo(signed);
        pdf.AsSpan((int)range[2], (int)range[3]).CopyTo(signed.AsSpan((int)range[1]));

        var pool = new List<X509Certificate2>(dss.Certificates);
        if (options.Certificates is not null) pool.AddRange(options.Certificates);
        if (options.TrustedRoots is not null) pool.AddRange(options.TrustedRoots);
        X509Certificate2? cert;
        X509Certificate2? authority = null; // the timestamp's signer
        try
        {
            byte[] der = contents.Bytes;
            der = der[..Ess.EncodedLength(der)];
            if (docTimestamp)
            {
                if (!Rfc3161TimestampToken.TryDecode(der, out var token, out _) || !token.VerifySignatureForData(signed, out cert))
                    return Fail("The document timestamp does not match the file.", end);
                pool.AddRange(token.AsSignedCms().Certificates);
                info = info with { Certificate = cert, IsIntact = true, HasTimestamp = true, TimestampValid = true, TimestampTime = token.TokenInfo.Timestamp };
            }
            else
            {
                var cms = new SignedCms(new ContentInfo(signed), detached: true);
                cms.Decode(der);
                SignerInfo signer = cms.SignerInfos[0];
                cert = signer.Certificate;
                info = info with { Certificate = cert };
                cms.CheckSignature(verifySignatureOnly: true);
                info = info with { IsIntact = true };
                pool.AddRange(cms.Certificates);

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
                        && token.VerifySignatureForSignerInfo(signer, out authority))
                    {
                        info = info with { TimestampValid = true, TimestampTime = token.TokenInfo.Timestamp };
                        pool.AddRange(token.AsSignedCms().Certificates);
                    }
                }
            }
        }
        catch (Exception e) when (e is CryptographicException or System.Formats.Asn1.AsnContentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Fail("The signature does not match the signed bytes: " + e.Message, end);
        }

        DateTimeOffset at = info.TimestampTime ?? info.ClaimedSigningTime ?? DateTimeOffset.UtcNow;
        bool proven = info.TimestampTime is not null;
        if (options.TrustedRoots is not null && cert is not null)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(options.TrustedRoots);
            foreach (X509Certificate2 extra in pool) chain.ChainPolicy.ExtraStore.Add(extra);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // revocation is checked below, with the file's own data
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.VerificationTime = at.UtcDateTime;
            bool trusted = chain.Build(cert);
            info = info with { ChainTrusted = trusted };
            if (!trusted) info = info with { Problem = "The certificate does not chain to a trusted root: " + string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim())) };
        }

        // Revocation: first with what the file carries, which is what makes a signature long-term; then with what can be fetched.
        PdfRevocationStatus embedded = PdfRevocationStatus.NotChecked;
        if (cert is not null)
        {
            RevocationData data = dss.ToRevocationData();
            var paths = new List<List<X509Certificate2>> { CertificatePath.Build(cert, pool) };
            // The timestamp is part of the proof: its authority's chain has to hold too.
            if (authority is not null) paths.Add(CertificatePath.Build(authority, pool));

            (PdfRevocationStatus status, string? problem) = Revocation(paths, data, at, proven);
            embedded = status;
            if (options.Revocation is not null && status is PdfRevocationStatus.NotChecked or PdfRevocationStatus.Unknown)
            {
                foreach (var path in paths) await FetchAsync(options.Revocation, path, data, ct).ConfigureAwait(false);
                (status, problem) = Revocation(paths, data, at, proven);
            }
            info = info with { Revocation = status };
            if (status == PdfRevocationStatus.Revoked) info = info with { Problem = problem };
        }

        if (docTimestamp) return (info, end);
        bool baseline = info.SubFilter == "ETSI.CAdES.detached" && info.HasSigningCertificateV2 && !info.HasSigningTimeAttribute;
        PadesLevel level = !baseline ? PadesLevel.None
            : !info.TimestampValid ? PadesLevel.BaselineB
            : embedded != PdfRevocationStatus.Good ? PadesLevel.BaselineT
            : PadesLevel.BaselineLT;
        return (info with { Level = level }, end);
    }

    /// <summary>The status of every certificate on the paths below their roots, as one answer.</summary>
    private static (PdfRevocationStatus Status, string? Problem) Revocation(List<List<X509Certificate2>> paths, RevocationData data, DateTimeOffset at, bool proven)
    {
        if (data.IsEmpty) return (PdfRevocationStatus.NotChecked, null);
        bool unknown = false;
        foreach (List<X509Certificate2> path in paths)
        {
            unknown |= !CertificatePath.IsComplete(path);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                if (path[i].Extensions["1.3.6.1.5.5.7.48.1.5"] is not null) continue; // id-pkix-ocsp-nocheck
                var (status, revokedAt) = data.Check(path[i], path[i + 1], at, proven);
                if (status == CertificateStatus.Revoked)
                    return (PdfRevocationStatus.Revoked, string.Create(CultureInfo.InvariantCulture,
                        $"The certificate '{path[i].Subject}' was revoked on {revokedAt:yyyy-MM-dd HH:mm:ss} UTC."));
                unknown |= status == CertificateStatus.Unknown;
            }
        }
        return (unknown ? PdfRevocationStatus.Unknown : PdfRevocationStatus.Good, null);
    }

    private static async Task FetchAsync(IRevocationClient client, List<X509Certificate2> path, RevocationData data, CancellationToken ct)
    {
        for (int i = 0; i + 1 < path.Count; i++)
        {
            if (data.Check(path[i], path[i + 1], DateTimeOffset.UtcNow, timeProven: false).Status != CertificateStatus.Unknown) continue;
            if (await client.GetOcspResponseAsync(path[i], path[i + 1], ct).ConfigureAwait(false) is { } ocsp) data.AddOcsp(ocsp);
            if (data.Check(path[i], path[i + 1], DateTimeOffset.UtcNow, timeProven: false).Status != CertificateStatus.Unknown) continue;
            if (await client.GetCrlAsync(path[i], ct).ConfigureAwait(false) is { } crl) data.AddCrl(crl);
        }
    }

    /// <summary>True when the field's widget has a box with an area.</summary>
    private static bool IsVisible(PdfReader reader, PdfDict field)
    {
        PdfDict widget = field;
        if (field.Get("Rect") is null && reader.Resolve(field.Get("Kids")) is PdfArray { Items.Count: > 0 } kids && reader.Resolve(kids.Items[0]) is PdfDict kid)
            widget = kid;
        return reader.ReadRect(widget.Get("Rect")) is { } r && Math.Abs(r[2] - r[0]) > 0 && Math.Abs(r[3] - r[1]) > 0;
    }

    /// <summary>The DocMDP permission of a certification signature (/Reference … /TransformParams /P).</summary>
    private static PdfCertificationLevel Certification(PdfReader reader, PdfDict sig)
    {
        if (reader.Resolve(sig.Get("Reference")) is not PdfArray references) return PdfCertificationLevel.None;
        foreach (PdfValue item in references.Items.Take(8))
        {
            if (reader.Resolve(item) is not PdfDict reference || reader.Resolve(reference.Get("TransformMethod")) is not PdfName { Encoded: "DocMDP" }) continue;
            int p = reader.Resolve(reference.Get("TransformParams")) is PdfDict parameters && reader.Resolve(parameters.Get("P")) is PdfNumber n ? n.IntValue : 2;
            return p is >= 1 and <= 3 ? (PdfCertificationLevel)p : PdfCertificationLevel.FormFilling;
        }
        return PdfCertificationLevel.None;
    }
}
