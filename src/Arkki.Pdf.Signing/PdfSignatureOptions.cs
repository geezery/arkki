using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Arkki.Pdf.Signing;

/// <summary>What <see cref="PdfSigner"/> writes into a signature. Every property has a working default.</summary>
public sealed class PdfSignatureOptions
{
    /// <summary>
    /// The signature field's name. When the document has an empty signature field of this name (one made with
    /// <c>PdfPage.AddSignatureField</c> or by another program), that field is signed; otherwise a new field is added,
    /// and the name must be free.
    /// </summary>
    public string FieldName { get; init; } = "Signature1";

    /// <summary>
    /// Draws the signature on the page. Null leaves a new field invisible; an existing field that has a box gets the
    /// default appearance.
    /// </summary>
    public PdfSignatureAppearance? Appearance { get; init; }

    /// <summary>
    /// Makes this a certification signature: the author's signature that also says which later changes keep the
    /// document valid. It must be the document's first signature.
    /// </summary>
    public PdfCertificationLevel Certification { get; init; }

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

    /// <summary>The page a new signature field is placed on, 0-based. An existing field stays on its own page.</summary>
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
