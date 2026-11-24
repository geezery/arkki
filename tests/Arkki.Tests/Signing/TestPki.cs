using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Arkki.Pdf.Signing;

namespace Arkki.Tests;

/// <summary>
/// A throwaway PKI made in the test: a root CA, a signer, an ECDSA signer, a time-stamping authority that answers
/// RFC 3161 requests in memory, and the root's revocation lists and OCSP responder. Nothing leaves the process.
/// </summary>
internal sealed class TestPki
{
    public X509Certificate2 Root { get; }
    public X509Certificate2 Signer { get; }
    public X509Certificate2 EcSigner { get; }
    public X509Certificate2 Tsa { get; }
    public X509Certificate2 Expired { get; }
    /// <summary>A signer whose certificate names where its revocation list and OCSP responder are.</summary>
    public X509Certificate2 Addressed { get; }

    public const string CrlUrl = "http://crl.arkki.test/root.crl";
    public const string OcspUrl = "http://ocsp.arkki.test/";

    private readonly RSA _rootKey = RSA.Create(2048);

    public TestPki()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var rootReq = new CertificateRequest("CN=Arkki Test Root CA, O=Arkki Tests", _rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootReq.PublicKey, false));
        Root = rootReq.CreateSelfSigned(now.AddDays(-2), now.AddYears(5));

        Signer = Issue("CN=Testi Allekirjoittaja, O=Arkki Tests, C=FI", RSA.Create(2048), now.AddDays(-1), now.AddYears(1), 2,
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, null);
        EcSigner = Issue("CN=EC Signer, O=Arkki Tests", ECDsa.Create(ECCurve.NamedCurves.nistP256), now.AddDays(-1), now.AddYears(1), 3,
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, null);
        Tsa = Issue("CN=Arkki Test TSA, O=Arkki Tests", RSA.Create(2048), now.AddDays(-1), now.AddYears(1), 4,
            X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.8",
            CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([CrlUrl]),
            new X509AuthorityInformationAccessExtension([OcspUrl], null));
        Expired = Issue("CN=Expired Signer, O=Arkki Tests", RSA.Create(2048), now.AddDays(-2).AddHours(1), now.AddDays(-1), 5,
            X509KeyUsageFlags.DigitalSignature, null);
        Addressed = Issue("CN=Addressed Signer, O=Arkki Tests", RSA.Create(2048), now.AddDays(-1), now.AddYears(1), 6,
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, null,
            CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([CrlUrl]),
            new X509AuthorityInformationAccessExtension([OcspUrl], null));
    }

    private X509Certificate2 Issue(string subject, AsymmetricAlgorithm key, DateTimeOffset from, DateTimeOffset to, byte serial, X509KeyUsageFlags usage, string? eku,
        params X509Extension[] more)
    {
        CertificateRequest req = key switch
        {
            RSA rsa => new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            ECDsa ec => new CertificateRequest(subject, ec, HashAlgorithmName.SHA256),
            _ => throw new ArgumentException(null, nameof(key)),
        };
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Root, true, false));
        if (eku is not null) req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(eku)], true));
        foreach (X509Extension e in more) req.CertificateExtensions.Add(e);
        X509Certificate2 cert = req.Create(Root.SubjectName, X509SignatureGenerator.CreateForRSA(_rootKey, RSASignaturePadding.Pkcs1), from, to, [serial, 1]);
        X509Certificate2 withKey = key switch { RSA rsa => cert.CopyWithPrivateKey(rsa), ECDsa ec => cert.CopyWithPrivateKey(ec), _ => cert };
        key.Dispose();
        return withKey;
    }

    public string RootPem => Root.ExportCertificatePem();

    // ---- revocation ----

    /// <summary>The root's revocation list; <paramref name="revoked"/> are on it, revoked at <paramref name="revokedAt"/>.</summary>
    public byte[] Crl(DateTimeOffset? revokedAt = null, params X509Certificate2[] revoked)
    {
        var builder = new CertificateRevocationListBuilder();
        foreach (X509Certificate2 c in revoked) builder.AddEntry(c, revokedAt ?? DateTimeOffset.UtcNow.AddHours(-1));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return builder.Build(Root, 1, now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, now.AddMinutes(-1));
    }

    /// <summary>The root's OCSP responder answering one request: good, or revoked when the certificate is in <paramref name="revoked"/>.</summary>
    public byte[] Ocsp(byte[] request, DateTimeOffset? revokedAt = null, params X509Certificate2[] revoked)
    {
        // The request names the certificate by hashes of its issuer and its serial number.
        AsnReader id = new AsnReader(request, AsnEncodingRules.DER).ReadSequence().ReadSequence().ReadSequence().ReadSequence().ReadSequence();
        id.ReadSequence();
        byte[] nameHash = id.ReadOctetString(), keyHash = id.ReadOctetString();
        System.Numerics.BigInteger serial = id.ReadInteger();
        if (!nameHash.AsSpan().SequenceEqual(SHA1.HashData(Root.SubjectName.RawData)) || !keyHash.AsSpan().SequenceEqual(SHA1.HashData(Root.PublicKey.EncodedKeyValue.RawData)))
            throw new InvalidDataException("The OCSP request is not about a certificate of the test root.");
        bool isRevoked = revoked.Any(c => new System.Numerics.BigInteger(c.SerialNumberBytes.Span, isUnsigned: false, isBigEndian: true) == serial);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var tbs = new AsnWriter(AsnEncodingRules.DER);
        using (tbs.PushSequence())
        {
            using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true))) tbs.WriteOctetString(keyHash); // responder by key
            tbs.WriteGeneralizedTime(now, omitFractionalSeconds: true);
            using (tbs.PushSequence())
            using (tbs.PushSequence())
            {
                using (tbs.PushSequence())
                {
                    using (tbs.PushSequence()) { tbs.WriteObjectIdentifier("1.3.14.3.2.26"); tbs.WriteNull(); }
                    tbs.WriteOctetString(nameHash);
                    tbs.WriteOctetString(keyHash);
                    tbs.WriteInteger(serial);
                }
                if (isRevoked)
                    using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
                        tbs.WriteGeneralizedTime(revokedAt ?? now.AddHours(-1), omitFractionalSeconds: true);
                else tbs.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 0));
                tbs.WriteGeneralizedTime(now.AddMinutes(-1), omitFractionalSeconds: true);
                using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                    tbs.WriteGeneralizedTime(now.AddDays(7), omitFractionalSeconds: true);
            }
        }
        byte[] data = tbs.Encode();
        var basic = new AsnWriter(AsnEncodingRules.DER);
        using (basic.PushSequence())
        {
            basic.WriteEncodedValue(data);
            using (basic.PushSequence()) { basic.WriteObjectIdentifier("1.2.840.113549.1.1.11"); basic.WriteNull(); }
            basic.WriteBitString(_rootKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        var response = new AsnWriter(AsnEncodingRules.DER);
        using (response.PushSequence())
        {
            response.WriteEnumeratedValue(OcspStatus.Successful);
            using (response.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            using (response.PushSequence())
            {
                response.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1.1");
                response.WriteOctetString(basic.Encode());
            }
        }
        return response.Encode();
    }

    private enum OcspStatus { Successful = 0 }

    /// <summary>
    /// The root's revocation services in memory: OCSP, lists, both or neither, with <paramref name="revoked"/> revoked
    /// at <paramref name="revokedAt"/>.
    /// </summary>
    public MemoryRevocation Revocation(bool ocsp = false, bool crl = true, DateTimeOffset? revokedAt = null, params X509Certificate2[] revoked) =>
        new(this, ocsp, crl, revokedAt, revoked);

    internal sealed class MemoryRevocation(TestPki pki, bool ocsp, bool crl, DateTimeOffset? revokedAt, X509Certificate2[] revoked) : IRevocationClient
    {
        public int OcspCalls { get; private set; }
        public int CrlCalls { get; private set; }

        public Task<byte[]?> GetOcspResponseAsync(X509Certificate2 certificate, X509Certificate2 issuer, CancellationToken cancellationToken = default)
        {
            OcspCalls++;
            if (!ocsp) return Task.FromResult<byte[]?>(null);
            // Built the way HttpRevocationClient builds it, so the request encoding is exercised too.
            byte[] request = Arkki.Pdf.Signing.Internal.OcspResponse.BuildRequest(certificate, issuer);
            return Task.FromResult<byte[]?>(pki.Ocsp(request, revokedAt, revoked));
        }

        public Task<byte[]?> GetCrlAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
        {
            CrlCalls++;
            return Task.FromResult(crl ? pki.Crl(revokedAt, revoked) : null);
        }
    }

    /// <summary>A key held "elsewhere": it signs hashes and gives out only the certificate's public part.</summary>
    public IExternalSigner External(X509Certificate2 certificate, bool derEcdsa = false, X509Certificate2? keyFrom = null) =>
        new MemorySigner(certificate, keyFrom ?? certificate, derEcdsa);

    private sealed class MemorySigner(X509Certificate2 certificate, X509Certificate2 key, bool derEcdsa) : IExternalSigner
    {
        public X509Certificate2 Certificate { get; } = X509CertificateLoader.LoadCertificate(certificate.RawData);

        public Task<byte[]> SignHashAsync(byte[] hash, HashAlgorithmName hashAlgorithm, CancellationToken cancellationToken = default)
        {
            using RSA? rsa = key.GetRSAPrivateKey();
            if (rsa is not null) return Task.FromResult(rsa.SignHash(hash, hashAlgorithm, RSASignaturePadding.Pkcs1));
            using ECDsa ec = key.GetECDsaPrivateKey()!;
            return Task.FromResult(ec.SignHash(hash, derEcdsa ? DSASignatureFormat.Rfc3279DerSequence : DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }
    }

    /// <summary>An in-memory time-stamping authority. <paramref name="corrupt"/> makes it answer for another hash.</summary>
    public ITimestampClient TimestampAuthority(bool corrupt = false) => new MemoryTsa(Tsa, corrupt);

    private sealed class MemoryTsa(X509Certificate2 tsa, bool corrupt) : ITimestampClient
    {
        private long _serial = 1000;

        private static byte[] SerialBytes(long n) => [0x10, (byte)(n >> 16), (byte)(n >> 8), (byte)n];

        public Task<byte[]> RequestAsync(byte[] timestampRequest, CancellationToken cancellationToken = default)
        {
            if (!Rfc3161TimestampRequest.TryDecode(timestampRequest, out var request, out _)) throw new InvalidDataException("Not a timestamp request.");
            byte[] hash = request.GetMessageHash().ToArray();
            if (corrupt) hash[0] ^= 0xFF;
            var tstInfo = new Rfc3161TimestampTokenInfo(new Oid("1.2.3.4.5"), request.HashAlgorithmId, hash,
                SerialBytes(Interlocked.Increment(ref _serial)), DateTimeOffset.UtcNow,
                accuracyInMicroseconds: 1_000_000, isOrdering: false, nonce: request.GetNonce());
            var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), tstInfo.Encode()));
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, tsa) { IncludeOption = X509IncludeOption.EndCertOnly };
            var w = new AsnWriter(AsnEncodingRules.DER);
            using (w.PushSequence()) using (w.PushSequence()) using (w.PushSequence()) w.WriteOctetString(SHA256.HashData(tsa.RawData));
            signer.SignedAttributes.Add(new AsnEncodedData("1.2.840.113549.1.9.16.2.47", w.Encode()));
            cms.ComputeSignature(signer, silent: true);
            var resp = new AsnWriter(AsnEncodingRules.DER);
            using (resp.PushSequence())
            {
                using (resp.PushSequence()) resp.WriteInteger(0); // granted
                resp.WriteEncodedValue(cms.Encode());
            }
            return Task.FromResult(resp.Encode());
        }
    }
}
