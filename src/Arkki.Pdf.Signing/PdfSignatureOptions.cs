using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Arkki.Pdf.Signing;

/// <summary>What <see cref="PdfSigner"/> writes into a signature. Every property has a working default.</summary>
public sealed class PdfSignatureOptions
{
    /// <summary>The signature field's name; it must not already exist in the document.</summary>
    public string FieldName { get; init; } = "Signature1";

    /// <summary>Why the document is signed ("Approved"), shown by viewers.</summary>
    public string? Reason { get; init; }

    /// <summary>Where it was signed ("Helsinki").</summary>
    public string? Location { get; init; }

    /// <summary>How to reach the signer.</summary>
    public string? ContactInfo { get; init; }

    /// <summary>The signer's name as written in the signature dictionary; null takes the certificate's common name.</summary>
    public string? SignerName { get; init; }

    /// <summary>The claimed signing time (the /M entry); null is now. A timestamp, when used, is the time that can be proved.</summary>
    public DateTimeOffset? SigningTime { get; init; }

    /// <summary>The page the (invisible) signature field is placed on, 0-based.</summary>
    public int PageIndex { get; init; }

    /// <summary>The digest algorithm: SHA-256 (default), SHA-384 or SHA-512.</summary>
    public HashAlgorithmName HashAlgorithm { get; init; } = HashAlgorithmName.SHA256;

    /// <summary>Intermediate certificates to embed so a verifier can build the chain to the root.</summary>
    public X509Certificate2Collection? ExtraCertificates { get; init; }

    /// <summary>When set, the signature is timestamped (PAdES B-T) by this RFC 3161 time-stamping authority.</summary>
    public ITimestampClient? Timestamp { get; init; }

    /// <summary>Bytes reserved for the signature container; 0 picks a size from the certificates (and timestamp).</summary>
    public int ReservedSize { get; init; }
}
