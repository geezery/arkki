using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Arkki.Pdf.Signing.Internal;

/// <summary>The signature every X.509 structure ends with: SEQUENCE { toBeSigned, algorithm, BIT STRING }.</summary>
internal static class X509Signature
{
    /// <summary>Splits a certificate, a CRL or a basic OCSP response into the signed part, the algorithm and the value.</summary>
    public static bool TrySplit(ReadOnlyMemory<byte> der, out ReadOnlyMemory<byte> toBeSigned, out string algorithmOid, out byte[] signature, out AsnReader rest)
    {
        toBeSigned = default; algorithmOid = ""; signature = []; rest = new AsnReader(ReadOnlyMemory<byte>.Empty, AsnEncodingRules.DER);
        try
        {
            AsnReader outer = new AsnReader(der, AsnEncodingRules.BER).ReadSequence();
            toBeSigned = outer.ReadEncodedValue();
            algorithmOid = outer.ReadSequence().ReadObjectIdentifier();
            signature = outer.ReadBitString(out _);
            rest = outer;
            return true;
        }
        catch (AsnContentException) { return false; }
    }

    /// <summary>True when <paramref name="signer"/>'s key made this signature over the bytes.</summary>
    public static bool Verify(ReadOnlySpan<byte> toBeSigned, string algorithmOid, byte[] signature, X509Certificate2 signer)
    {
        try
        {
            switch (algorithmOid)
            {
                case "1.2.840.113549.1.1.5": return Rsa(toBeSigned, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
                case "1.2.840.113549.1.1.11": return Rsa(toBeSigned, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                case "1.2.840.113549.1.1.12": return Rsa(toBeSigned, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1);
                case "1.2.840.113549.1.1.13": return Rsa(toBeSigned, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
                case "1.2.840.113549.1.1.10": // RSASSA-PSS: the hash is in the parameters; the usual three are tried
                    return Rsa(toBeSigned, HashAlgorithmName.SHA256, RSASignaturePadding.Pss) || Rsa(toBeSigned, HashAlgorithmName.SHA384, RSASignaturePadding.Pss)
                        || Rsa(toBeSigned, HashAlgorithmName.SHA512, RSASignaturePadding.Pss);
                case "1.2.840.10045.4.1": return Ec(toBeSigned, HashAlgorithmName.SHA1);
                case "1.2.840.10045.4.3.2": return Ec(toBeSigned, HashAlgorithmName.SHA256);
                case "1.2.840.10045.4.3.3": return Ec(toBeSigned, HashAlgorithmName.SHA384);
                case "1.2.840.10045.4.3.4": return Ec(toBeSigned, HashAlgorithmName.SHA512);
                default: return false;
            }
        }
        catch (CryptographicException) { return false; }

        bool Rsa(ReadOnlySpan<byte> data, HashAlgorithmName hash, RSASignaturePadding padding)
        {
            using RSA? rsa = signer.GetRSAPublicKey();
            return rsa is not null && rsa.VerifyData(data, signature, hash, padding);
        }

        bool Ec(ReadOnlySpan<byte> data, HashAlgorithmName hash)
        {
            using ECDsa? ec = signer.GetECDsaPublicKey();
            return ec is not null && ec.VerifyData(data, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
        }
    }

    /// <summary>True when <paramref name="issuer"/> issued and signed <paramref name="certificate"/>.</summary>
    public static bool Issued(X509Certificate2 issuer, X509Certificate2 certificate) =>
        certificate.IssuerName.RawData.AsSpan().SequenceEqual(issuer.SubjectName.RawData)
        && TrySplit(certificate.RawData, out var tbs, out string algorithm, out byte[] signature, out _)
        && Verify(tbs.Span, algorithm, signature, issuer);

    public static BigInteger Serial(X509Certificate2 certificate) => new(certificate.SerialNumberBytes.Span, isUnsigned: false, isBigEndian: true);

    public static DateTimeOffset ReadTime(AsnReader reader) =>
        reader.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) ? reader.ReadUtcTime() : reader.ReadGeneralizedTime();
}

/// <summary>The chain from a certificate up to a self-signed root, built from the certificates at hand and nothing else.</summary>
internal static class CertificatePath
{
    /// <summary>
    /// The certificate, its issuer, that one's issuer and so on, as far as <paramref name="pool"/> reaches. The path is
    /// complete when its last certificate is self-signed.
    /// </summary>
    public static List<X509Certificate2> Build(X509Certificate2 leaf, IReadOnlyCollection<X509Certificate2> pool)
    {
        var path = new List<X509Certificate2> { leaf };
        while (path.Count < 16 && !IsSelfSigned(path[^1]))
        {
            X509Certificate2 current = path[^1];
            X509Certificate2? issuer = pool.FirstOrDefault(c => !path.Contains(c) && X509Signature.Issued(c, current));
            if (issuer is null) break;
            path.Add(issuer);
        }
        return path;
    }

    public static bool IsSelfSigned(X509Certificate2 c) => c.SubjectName.RawData.AsSpan().SequenceEqual(c.IssuerName.RawData);

    public static bool IsComplete(List<X509Certificate2> path) => IsSelfSigned(path[^1]);
}

/// <summary>A certificate revocation list (RFC 5280), read just far enough to look a serial number up.</summary>
internal sealed class Crl
{
    private ReadOnlyMemory<byte> _toBeSigned;
    private string _algorithm = "";
    private byte[] _signature = [];

    public byte[] Issuer { get; private set; } = [];
    public DateTimeOffset ThisUpdate { get; private set; }
    public Dictionary<BigInteger, DateTimeOffset> Revoked { get; } = [];

    public static Crl? TryParse(byte[] der)
    {
        var crl = new Crl();
        if (!X509Signature.TrySplit(der, out crl._toBeSigned, out crl._algorithm, out crl._signature, out _)) return null;
        try
        {
            AsnReader tbs = new AsnReader(crl._toBeSigned, AsnEncodingRules.BER).ReadSequence();
            if (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Integer)) tbs.ReadInteger(); // version
            tbs.ReadSequence(); // signature algorithm
            crl.Issuer = tbs.ReadEncodedValue().ToArray();
            crl.ThisUpdate = X509Signature.ReadTime(tbs);
            if (tbs.HasData && (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) || tbs.PeekTag().HasSameClassAndValue(Asn1Tag.GeneralizedTime)))
                X509Signature.ReadTime(tbs); // nextUpdate
            if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
            {
                AsnReader revoked = tbs.ReadSequence();
                while (revoked.HasData)
                {
                    AsnReader entry = revoked.ReadSequence();
                    BigInteger serial = entry.ReadInteger();
                    crl.Revoked[serial] = X509Signature.ReadTime(entry);
                }
            }
            return crl;
        }
        catch (AsnContentException) { return null; }
    }

    public bool IsFrom(X509Certificate2 issuer) =>
        Issuer.AsSpan().SequenceEqual(issuer.SubjectName.RawData) && X509Signature.Verify(_toBeSigned.Span, _algorithm, _signature, issuer);
}

/// <summary>An OCSP request and response (RFC 6960), read just far enough to find a certificate's status.</summary>
internal sealed class OcspResponse
{
    private const string Sha1 = "1.3.14.3.2.26";
    private const string OcspSigning = "1.3.6.1.5.5.7.3.9";

    private ReadOnlyMemory<byte> _toBeSigned;
    private string _algorithm = "";
    private byte[] _signature = [];

    public sealed record Single(string HashOid, byte[] NameHash, byte[] KeyHash, BigInteger Serial, int Status, DateTimeOffset? RevokedAt);

    public List<Single> Responses { get; } = [];
    public List<X509Certificate2> Certificates { get; } = [];

    /// <summary>The request for one certificate's status, naming it by SHA-1 hashes of its issuer as every responder accepts.</summary>
    public static byte[] BuildRequest(X509Certificate2 certificate, X509Certificate2 issuer)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence()) // OCSPRequest
        using (w.PushSequence()) // TBSRequest
        using (w.PushSequence()) // requestList
        using (w.PushSequence()) // Request
        using (w.PushSequence()) // CertID
        {
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(Sha1);
                w.WriteNull();
            }
            w.WriteOctetString(SHA1.HashData(issuer.SubjectName.RawData));
            w.WriteOctetString(SHA1.HashData(issuer.PublicKey.EncodedKeyValue.RawData));
            w.WriteInteger(certificate.SerialNumberBytes.Span);
        }
        return w.Encode();
    }

    /// <summary>Reads a response; null when it is malformed or the responder did not answer "successful".</summary>
    public static OcspResponse? TryParse(byte[] der)
    {
        try
        {
            AsnReader outer = new AsnReader(der, AsnEncodingRules.BER).ReadSequence();
            if (outer.ReadEnumeratedBytes().Span is not [0] || !outer.HasData) return null;
            AsnReader bytes = outer.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence();
            if (bytes.ReadObjectIdentifier() != "1.3.6.1.5.5.7.48.1.1") return null; // id-pkix-ocsp-basic
            byte[] basic = bytes.ReadOctetString();

            var response = new OcspResponse();
            if (!X509Signature.TrySplit(basic, out response._toBeSigned, out response._algorithm, out response._signature, out AsnReader rest)) return null;
            if (rest.HasData)
            {
                AsnReader certs = rest.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence();
                while (certs.HasData && response.Certificates.Count < 16)
                    response.Certificates.Add(X509CertificateLoader.LoadCertificate(certs.ReadEncodedValue().Span));
            }

            AsnReader data = new AsnReader(response._toBeSigned, AsnEncodingRules.BER).ReadSequence();
            if (data.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true))) data.ReadEncodedValue(); // version
            data.ReadEncodedValue(); // responderID
            data.ReadGeneralizedTime(); // producedAt
            AsnReader responses = data.ReadSequence();
            while (responses.HasData)
            {
                AsnReader single = responses.ReadSequence();
                AsnReader id = single.ReadSequence();
                string hash = id.ReadSequence().ReadObjectIdentifier();
                byte[] nameHash = id.ReadOctetString(), keyHash = id.ReadOctetString();
                BigInteger serial = id.ReadInteger();
                Asn1Tag status = single.PeekTag();
                DateTimeOffset? revokedAt = null;
                if (status.TagValue == 1)
                    revokedAt = single.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)).ReadGeneralizedTime();
                response.Responses.Add(new Single(hash, nameHash, keyHash, serial, status.TagValue, revokedAt));
            }
            return response;
        }
        catch (Exception e) when (e is AsnContentException or CryptographicException) { return null; }
    }

    /// <summary>
    /// True when the response is signed by the issuer itself or by a responder the issuer delegated: a certificate the
    /// issuer signed that is marked for OCSP signing.
    /// </summary>
    public bool IsFrom(X509Certificate2 issuer)
    {
        if (X509Signature.Verify(_toBeSigned.Span, _algorithm, _signature, issuer)) return true;
        foreach (X509Certificate2 responder in Certificates)
        {
            bool delegated = responder.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == OcspSigning));
            if (delegated && X509Signature.Issued(issuer, responder) && X509Signature.Verify(_toBeSigned.Span, _algorithm, _signature, responder)) return true;
        }
        return false;
    }

    /// <summary>The answer about this certificate, or null when the response is about others.</summary>
    public Single? Find(X509Certificate2 certificate, X509Certificate2 issuer)
    {
        BigInteger serial = X509Signature.Serial(certificate);
        foreach (Single s in Responses)
        {
            if (s.Serial != serial) continue;
            byte[]? name = Hash(s.HashOid, issuer.SubjectName.RawData), key = Hash(s.HashOid, issuer.PublicKey.EncodedKeyValue.RawData);
            if (name is not null && s.NameHash.AsSpan().SequenceEqual(name) && s.KeyHash.AsSpan().SequenceEqual(key)) return s;
        }
        return null;
    }

    private static byte[]? Hash(string oid, byte[] data) => oid switch
    {
        Sha1 => SHA1.HashData(data),
        "2.16.840.1.101.3.4.2.1" => SHA256.HashData(data),
        "2.16.840.1.101.3.4.2.2" => SHA384.HashData(data),
        "2.16.840.1.101.3.4.2.3" => SHA512.HashData(data),
        _ => null,
    };
}

/// <summary>What revocation information says about a certificate.</summary>
internal enum CertificateStatus { Good, Revoked, Unknown }

/// <summary>CRLs and OCSP responses gathered for a check, parsed once.</summary>
internal sealed class RevocationData
{
    public List<Crl> Crls { get; } = [];
    public List<OcspResponse> Ocsps { get; } = [];

    public bool IsEmpty => Crls.Count == 0 && Ocsps.Count == 0;

    public void AddCrl(byte[] der) { if (Crl.TryParse(der) is { } crl) Crls.Add(crl); }
    public void AddOcsp(byte[] der) { if (OcspResponse.TryParse(der) is { } ocsp) Ocsps.Add(ocsp); }

    /// <summary>
    /// The status of a certificate at a moment. A certificate revoked after a moment that a timestamp proves was still
    /// good when it signed; without that proof a later revocation counts, because the claimed time could be a lie.
    /// </summary>
    public (CertificateStatus Status, DateTimeOffset? RevokedAt) Check(X509Certificate2 certificate, X509Certificate2 issuer, DateTimeOffset at, bool timeProven)
    {
        foreach (OcspResponse ocsp in Ocsps)
        {
            if (ocsp.Find(certificate, issuer) is not { } answer || !ocsp.IsFrom(issuer)) continue;
            if (answer.Status == 0) return (CertificateStatus.Good, null);
            if (answer.Status == 1) return Revoked(answer.RevokedAt ?? at);
        }
        BigInteger serial = X509Signature.Serial(certificate);
        foreach (Crl crl in Crls)
        {
            if (!crl.IsFrom(issuer)) continue;
            return crl.Revoked.TryGetValue(serial, out DateTimeOffset when) ? Revoked(when) : (CertificateStatus.Good, null);
        }
        return (CertificateStatus.Unknown, null);

        (CertificateStatus, DateTimeOffset?) Revoked(DateTimeOffset when) =>
            timeProven && when > at ? (CertificateStatus.Good, null) : (CertificateStatus.Revoked, when);
    }
}
