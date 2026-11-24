using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Arkki.Pdf.Signing;

namespace Arkki.Tests;

/// <summary>
/// A throwaway PKI made in the test: a root CA, a signer, an ECDSA signer and a time-stamping authority that
/// answers RFC 3161 requests in memory. Nothing leaves the process.
/// </summary>
internal sealed class TestPki
{
    public X509Certificate2 Root { get; }
    public X509Certificate2 Signer { get; }
    public X509Certificate2 EcSigner { get; }
    public X509Certificate2 Tsa { get; }
    public X509Certificate2 Expired { get; }

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
            X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.8");
        Expired = Issue("CN=Expired Signer, O=Arkki Tests", RSA.Create(2048), now.AddDays(-2).AddHours(1), now.AddDays(-1), 5,
            X509KeyUsageFlags.DigitalSignature, null);
    }

    private X509Certificate2 Issue(string subject, AsymmetricAlgorithm key, DateTimeOffset from, DateTimeOffset to, byte serial, X509KeyUsageFlags usage, string? eku)
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
        X509Certificate2 cert = req.Create(Root.SubjectName, X509SignatureGenerator.CreateForRSA(_rootKey, RSASignaturePadding.Pkcs1), from, to, [serial, 1]);
        X509Certificate2 withKey = key switch { RSA rsa => cert.CopyWithPrivateKey(rsa), ECDsa ec => cert.CopyWithPrivateKey(ec), _ => cert };
        key.Dispose();
        return withKey;
    }

    public string RootPem => Root.ExportCertificatePem();

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
