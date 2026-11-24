using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Arkki.Pdf.Reading;
using Arkki.Pdf.Signing.Internal;

namespace Arkki.Pdf.Signing;

public static partial class PdfSigner
{
    /// <summary>
    /// Takes a signed file to PAdES B-LT: fetches the revocation information of every certificate its signatures and
    /// timestamps rely on and stores it, with the certificates, in the file's document security store. The signatures
    /// can then be validated without the authorities' servers. Sign with a timestamp first (B-T), then call this.
    /// </summary>
    /// <param name="pdf">The signed file.</param>
    /// <param name="revocation">Where OCSP responses and revocation lists come from.</param>
    /// <param name="certificates">
    /// Issuer certificates the signatures do not embed: intermediates and roots. Every certificate's issuer must be
    /// among the embedded ones or these.
    /// </param>
    /// <param name="cancellationToken">Cancels the fetching.</param>
    /// <exception cref="InvalidOperationException">
    /// The file has no signature, an issuer certificate is missing, or neither an OCSP response nor a revocation list
    /// could be had for a certificate.
    /// </exception>
    public static async Task<byte[]> AddValidationDataAsync(byte[] pdf, IRevocationClient revocation,
        X509Certificate2Collection? certificates = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(revocation);
        PdfReader reader = PdfReader.Open(pdf);
        var signatures = SignatureFields.Find(reader);
        if (signatures.Count == 0) throw new InvalidOperationException("The document has no signature to add validation data for.");

        // Everything that signed something: each signer, each timestamp authority.
        Dss existing = Dss.Read(reader);
        var pool = new List<X509Certificate2>(existing.Certificates);
        if (certificates is not null) pool.AddRange(certificates);
        var leaves = new List<X509Certificate2>();
        foreach (var (_, _, sig) in signatures)
        {
            if (reader.Resolve(sig.Get("Contents")) is not PdfString contents) continue;
            foreach (SignedCms cms in Containers(contents.Bytes))
            {
                pool.AddRange(cms.Certificates);
                foreach (SignerInfo signer in cms.SignerInfos)
                    if (signer.Certificate is { } c) leaves.Add(c);
            }
        }

        var needed = new Dictionary<string, X509Certificate2>();
        var newCrls = new Dictionary<string, byte[]>();
        var newOcsps = new Dictionary<string, byte[]>();
        var have = existing.ToRevocationData();
        var done = new HashSet<string>();
        foreach (X509Certificate2 leaf in leaves)
        {
            List<X509Certificate2> path = CertificatePath.Build(leaf, pool);
            if (!CertificatePath.IsComplete(path))
                throw new InvalidOperationException($"The issuer certificate of '{path[^1].Subject}' is not in the file: pass it in certificates.");
            foreach (X509Certificate2 c in path) needed[c.Thumbprint] = c;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                X509Certificate2 certificate = path[i], issuer = path[i + 1];
                if (!done.Add(certificate.Thumbprint)) continue;
                if (certificate.Extensions["1.3.6.1.5.5.7.48.1.5"] is not null) continue; // id-pkix-ocsp-nocheck: never revoked
                if (have.Check(certificate, issuer, DateTimeOffset.UtcNow, timeProven: false).Status != CertificateStatus.Unknown) continue;

                byte[]? ocsp = await revocation.GetOcspResponseAsync(certificate, issuer, cancellationToken).ConfigureAwait(false);
                if (ocsp is not null && OcspResponse.TryParse(ocsp) is { } parsed && parsed.Find(certificate, issuer) is not null && parsed.IsFrom(issuer))
                {
                    newOcsps[Convert.ToHexString(SHA256.HashData(ocsp))] = ocsp;
                    foreach (X509Certificate2 responder in parsed.Certificates) needed[responder.Thumbprint] = responder;
                    have.AddOcsp(ocsp);
                    continue;
                }
                byte[]? crl = await revocation.GetCrlAsync(certificate, cancellationToken).ConfigureAwait(false);
                if (crl is not null && Crl.TryParse(crl) is { } list && list.IsFrom(issuer))
                {
                    newCrls[Convert.ToHexString(SHA256.HashData(crl))] = crl;
                    have.AddCrl(crl);
                    continue;
                }
                throw new InvalidOperationException($"No revocation information could be had for '{certificate.Subject}': "
                    + "neither an OCSP response nor a revocation list signed by its issuer.");
            }
        }

        // The store keeps what it had and gains what is new.
        var update = new IncrementalUpdate(reader, pdf);
        var known = new HashSet<string>(existing.Certificates.Select(c => c.Thumbprint));
        var certRefs = new List<string>(existing.CertificateRefs.Select(Ref));
        var crlRefs = new List<string>(existing.CrlRefs.Select(Ref));
        var ocspRefs = new List<string>(existing.OcspRefs.Select(Ref));
        foreach (X509Certificate2 c in needed.Values.OrderBy(c => c.Thumbprint, StringComparer.Ordinal))
            if (known.Add(c.Thumbprint)) certRefs.Add(Stream(update, c.RawData));
        foreach (var (_, crl) in newCrls.OrderBy(p => p.Key, StringComparer.Ordinal)) crlRefs.Add(Stream(update, crl));
        foreach (var (_, ocsp) in newOcsps.OrderBy(p => p.Key, StringComparer.Ordinal)) ocspRefs.Add(Stream(update, ocsp));

        var dss = new StringBuilder("<< /Type /DSS");
        if (certRefs.Count > 0) dss.Append(" /Certs [").AppendJoin(' ', certRefs).Append(']');
        if (ocspRefs.Count > 0) dss.Append(" /OCSPs [").AppendJoin(' ', ocspRefs).Append(']');
        if (crlRefs.Count > 0) dss.Append(" /CRLs [").AppendJoin(' ', crlRefs).Append(']');
        dss.Append(" >>");
        int dssId = update.Reserve();
        update.Put(dssId, Encoding.ASCII.GetBytes(dss.ToString()));
        update.SetCatalog(IncrementalUpdate.With(update.Catalog, "DSS", new PdfOutputRef(dssId)));
        return update.Write(out _);

        static string Ref(PdfValue v) => v is PdfRef r ? IncrementalUpdate.MapRef(r) : "null";

        static string Stream(IncrementalUpdate update, byte[] data)
        {
            int id = update.Reserve();
            update.PutStream(id, "", data);
            return string.Create(CultureInfo.InvariantCulture, $"{id} 0 R");
        }
    }

    /// <summary>
    /// Adds a document timestamp: an RFC 3161 timestamp over the whole file as it is now, signatures and validation
    /// data included. After <see cref="AddValidationDataAsync"/> this takes the file to PAdES B-LTA, and adding another
    /// before the timestamp authority's certificate expires keeps the file valid for as long as that is repeated.
    /// </summary>
    /// <param name="pdf">The file, usually signed and carrying its validation data.</param>
    /// <param name="timestamp">The time-stamping authority.</param>
    /// <param name="fieldName">The timestamp field's name; null picks the first free one of Timestamp1, Timestamp2, …</param>
    /// <param name="cancellationToken">Cancels the call to the authority.</param>
    public static Task<byte[]> AddDocumentTimestampAsync(byte[] pdf, ITimestampClient timestamp, string? fieldName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(timestamp);
        if (fieldName is null)
        {
            var taken = new HashSet<string>(PdfReader.Open(pdf).GetFormFields().Select(f => f.Name));
            for (int n = 1; fieldName is null; n++)
                if (!taken.Contains($"Timestamp{n}")) fieldName = $"Timestamp{n}";
        }
        return SignCoreAsync(pdf, new PdfSignatureOptions { FieldName = fieldName }, null, docTimestamp: true, 8192,
            signedBytes => RequestTimestampAsync(timestamp, SHA256.HashData(signedBytes), cancellationToken));
    }

    /// <summary>The CMS container in a signature's /Contents, and the timestamp tokens inside it.</summary>
    internal static List<SignedCms> Containers(byte[] contents)
    {
        var found = new List<SignedCms>();
        try
        {
            var cms = new SignedCms();
            cms.Decode(contents.AsSpan(0, Ess.EncodedLength(contents)));
            found.Add(cms);
            foreach (SignerInfo signer in cms.SignerInfos)
                foreach (CryptographicAttributeObject a in signer.UnsignedAttributes)
                {
                    if (a.Oid.Value != Ess.TimestampTokenOid || a.Values.Count == 0) continue;
                    var token = new SignedCms();
                    token.Decode(a.Values[0].RawData);
                    found.Add(token);
                }
        }
        catch (Exception e) when (e is CryptographicException or System.Formats.Asn1.AsnContentException or ArgumentOutOfRangeException) { }
        return found;
    }
}
