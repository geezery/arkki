using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Arkki.Pdf.Signing.Internal;

/// <summary>The ESS signing-certificate-v2 attribute (RFC 5035), which binds the signer's certificate into the signature.</summary>
internal static class Ess
{
    public const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    public const string SigningTimeOid = "1.2.840.113549.1.9.5";
    public const string TimestampTokenOid = "1.2.840.113549.1.9.16.2.14";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    /// <summary>SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 } with SHA-256 and the issuer and serial.</summary>
    public static AsnEncodedData SigningCertificateV2(X509Certificate2 certificate)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        using (w.PushSequence())
        using (w.PushSequence()) // ESSCertIDv2; hashAlgorithm left out: SHA-256 is its default
        {
            w.WriteOctetString(SHA256.HashData(certificate.RawData));
            using (w.PushSequence()) // IssuerSerial
            {
                using (w.PushSequence()) // GeneralNames
                using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true))) // directoryName
                    w.WriteEncodedValue(certificate.IssuerName.RawData);
                w.WriteIntegerUnsigned(certificate.SerialNumberBytes.Span);
            }
        }
        return new AsnEncodedData(SigningCertificateV2Oid, w.Encode());
    }

    /// <summary>True when the attribute value names <paramref name="certificate"/> by its hash (SHA-256 or the algorithm given).</summary>
    public static bool Matches(byte[] attributeValue, X509Certificate2 certificate)
    {
        try
        {
            var outer = new AsnReader(attributeValue, AsnEncodingRules.DER).ReadSequence();
            var certs = outer.ReadSequence();
            var first = certs.ReadSequence();
            string algorithm = Sha256Oid;
            if (first.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
                algorithm = first.ReadSequence().ReadObjectIdentifier();
            byte[] hash = first.ReadOctetString();
            byte[] expected = algorithm switch
            {
                Sha256Oid => SHA256.HashData(certificate.RawData),
                "2.16.840.1.101.3.4.2.2" => SHA384.HashData(certificate.RawData),
                "2.16.840.1.101.3.4.2.3" => SHA512.HashData(certificate.RawData),
                _ => [],
            };
            return expected.Length > 0 && CryptographicOperations.FixedTimeEquals(hash, expected);
        }
        catch (AsnContentException) { return false; }
    }

    /// <summary>The length of the DER value at the start of <paramref name="data"/>, so zero padding after it can be cut.</summary>
    public static int EncodedLength(ReadOnlySpan<byte> data)
    {
        AsnDecoder.ReadEncodedValue(data, AsnEncodingRules.BER, out _, out _, out int consumed);
        return consumed;
    }
}
