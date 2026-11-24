using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Arkki.Pdf.Signing.Internal;

/// <summary>
/// Builds a CMS SignedData container (RFC 5652) by hand for a signature whose value comes from outside the process.
/// .NET's SignedCms wants the key itself; here the signed attributes are encoded first, their hash is handed to the
/// external signer, and the container is assembled around the value that comes back.
/// </summary>
internal static class CmsBuilder
{
    private const string Data = "1.2.840.113549.1.7.1";
    private const string SignedData = "1.2.840.113549.1.7.2";
    private const string ContentType = "1.2.840.113549.1.9.3";
    private const string MessageDigest = "1.2.840.113549.1.9.4";
    private const string RsaEncryption = "1.2.840.113549.1.1.1";

    /// <summary>
    /// The signed attributes as a DER SET: the content type, the hash of the signed bytes and signing-certificate-v2.
    /// Its hash is what the key signs.
    /// </summary>
    public static byte[] SignedAttributes(byte[] contentHash, X509Certificate2 certificate)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSetOf())
        {
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(ContentType);
                using (w.PushSetOf()) w.WriteObjectIdentifier(Data);
            }
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(MessageDigest);
                using (w.PushSetOf()) w.WriteOctetString(contentHash);
            }
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(Ess.SigningCertificateV2Oid);
                using (w.PushSetOf()) w.WriteEncodedValue(Ess.SigningCertificateV2(certificate).RawData);
            }
        }
        return w.Encode();
    }

    /// <summary>The container: one signer, its certificate and the extra ones, and the timestamp token when there is one.</summary>
    public static byte[] Build(X509Certificate2 certificate, X509Certificate2Collection? extraCertificates, string digestOid,
        byte[] signedAttributes, string signatureAlgorithmOid, byte[] signature, byte[]? timestampToken)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence()) // ContentInfo
        {
            w.WriteObjectIdentifier(SignedData);
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            using (w.PushSequence()) // SignedData
            {
                w.WriteInteger(1);
                using (w.PushSetOf()) Algorithm(w, digestOid, withNull: false);
                using (w.PushSequence()) w.WriteObjectIdentifier(Data); // detached: no content
                using (w.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    w.WriteEncodedValue(certificate.RawData);
                    if (extraCertificates is not null)
                        foreach (X509Certificate2 extra in extraCertificates)
                            if (!extra.RawData.AsSpan().SequenceEqual(certificate.RawData)) w.WriteEncodedValue(extra.RawData);
                }
                using (w.PushSetOf())
                using (w.PushSequence()) // SignerInfo
                {
                    w.WriteInteger(1);
                    using (w.PushSequence()) // IssuerAndSerialNumber
                    {
                        w.WriteEncodedValue(certificate.IssuerName.RawData);
                        w.WriteInteger(certificate.SerialNumberBytes.Span);
                    }
                    Algorithm(w, digestOid, withNull: false);
                    // The attributes were hashed as a SET; in the container the same bytes carry the tag [0].
                    byte[] tagged = (byte[])signedAttributes.Clone();
                    tagged[0] = 0xA0;
                    w.WriteEncodedValue(tagged);
                    Algorithm(w, signatureAlgorithmOid, withNull: signatureAlgorithmOid == RsaEncryption);
                    w.WriteOctetString(signature);
                    if (timestampToken is not null)
                    {
                        using (w.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
                        using (w.PushSequence())
                        {
                            w.WriteObjectIdentifier(Ess.TimestampTokenOid);
                            using (w.PushSetOf()) w.WriteEncodedValue(timestampToken);
                        }
                    }
                }
            }
        }
        return w.Encode();
    }

    /// <summary>
    /// The signature algorithm a certificate's key signs with, and the signature value in the form a CMS container
    /// holds: an ECDSA value given as r and s side by side becomes a DER sequence.
    /// </summary>
    public static (string AlgorithmOid, byte[] Signature) Normalize(X509Certificate2 certificate, HashAlgorithmName hash, byte[] signature)
    {
        if (certificate.GetRSAPublicKey() is not null) return (RsaEncryption, signature);
        using ECDsa? ec = certificate.GetECDsaPublicKey();
        if (ec is null) throw new NotSupportedException("The external signer's certificate must hold an RSA or an elliptic-curve (ECDSA) key.");
        string oid = hash.Name switch
        {
            "SHA256" => "1.2.840.10045.4.3.2",
            "SHA384" => "1.2.840.10045.4.3.3",
            _ => "1.2.840.10045.4.3.4",
        };
        int field = (ec.KeySize + 7) / 8;
        if (signature.Length != 2 * field) return (oid, signature); // already a DER sequence
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteIntegerUnsigned(Minimal(signature.AsSpan(0, field)));
            w.WriteIntegerUnsigned(Minimal(signature.AsSpan(field)));
        }
        return (oid, w.Encode());
    }

    /// <summary>True when the value is the certificate's signature of the hash.</summary>
    public static bool Verify(X509Certificate2 certificate, HashAlgorithmName hash, byte[] hashValue, string algorithmOid, byte[] signature)
    {
        try
        {
            if (algorithmOid == RsaEncryption)
            {
                using RSA rsa = certificate.GetRSAPublicKey()!;
                return rsa.VerifyHash(hashValue, signature, hash, RSASignaturePadding.Pkcs1);
            }
            using ECDsa ec = certificate.GetECDsaPublicKey()!;
            return ec.VerifyHash(hashValue, signature, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException) { return false; }
    }

    /// <summary>A big-endian number without leading zero bytes, as a DER integer wants it.</summary>
    private static ReadOnlySpan<byte> Minimal(ReadOnlySpan<byte> number)
    {
        ReadOnlySpan<byte> trimmed = number.TrimStart((byte)0);
        return trimmed.IsEmpty ? number[^1..] : trimmed;
    }

    private static void Algorithm(AsnWriter w, string oid, bool withNull)
    {
        using (w.PushSequence())
        {
            w.WriteObjectIdentifier(oid);
            if (withNull) w.WriteNull();
        }
    }
}
