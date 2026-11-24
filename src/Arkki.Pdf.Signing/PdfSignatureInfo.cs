using System.Security.Cryptography.X509Certificates;

namespace Arkki.Pdf.Signing;

/// <summary>The PAdES baseline level a signature meets, as far as this verifier checks it.</summary>
public enum PadesLevel
{
    /// <summary>Not a PAdES signature (for example adbe.pkcs7.detached), a document timestamp, or its signature value is broken.</summary>
    None,
    /// <summary>ETSI.CAdES.detached with signing-certificate-v2 and no signing-time attribute.</summary>
    BaselineB,
    /// <summary>B plus a valid signature timestamp.</summary>
    BaselineT,
    /// <summary>
    /// T plus validation data in the file: the chains of the signer and of the timestamp authority reach a root, and
    /// the file's own revocation lists and OCSP responses say every certificate on them was good.
    /// </summary>
    BaselineLT,
    /// <summary>LT plus a valid document timestamp added after the signature, which protects the validation data.</summary>
    BaselineLTA,
}

/// <summary>What revocation information says about the certificates a signature relies on.</summary>
public enum PdfRevocationStatus
{
    /// <summary>There was nothing to check with: the file carries no revocation information and none was fetched.</summary>
    NotChecked,
    /// <summary>Every certificate below the root was good when the signature was made.</summary>
    Good,
    /// <summary>A certificate had been revoked when the signature was made.</summary>
    Revoked,
    /// <summary>For some certificate no answer was found, or the chain does not reach a root.</summary>
    Unknown,
}

/// <summary>What <see cref="PdfSignatureVerifier"/> found about one signature or document timestamp.</summary>
public sealed record PdfSignatureInfo
{
    /// <summary>The signature field's full name.</summary>
    public required string FieldName { get; init; }
    /// <summary>The /SubFilter: ETSI.CAdES.detached for PAdES, adbe.pkcs7.detached for older CMS signatures, ETSI.RFC3161 for a document timestamp.</summary>
    public string? SubFilter { get; init; }
    /// <summary>True for a document timestamp: not anyone's signature, but an authority's proof of when the file existed.</summary>
    public bool IsDocumentTimestamp { get; init; }
    /// <summary>The signer's certificate as embedded in the signature; for a document timestamp, the authority's.</summary>
    public X509Certificate2? Certificate { get; init; }
    /// <summary>The signer's name as the signature dictionary states it (/Name).</summary>
    public string? SignerName { get; init; }
    /// <summary>The reason the signer gave (/Reason).</summary>
    public string? Reason { get; init; }
    /// <summary>Where the signer says the document was signed (/Location).</summary>
    public string? Location { get; init; }
    /// <summary>The time the signer claims (/M); not proof of anything.</summary>
    public DateTimeOffset? ClaimedSigningTime { get; init; }
    /// <summary>The time a valid timestamp proves the signature (or, for a document timestamp, the file) existed at.</summary>
    public DateTimeOffset? TimestampTime { get; init; }
    /// <summary>The signed bytes are unchanged and the signature value matches the embedded certificate.</summary>
    public bool IsIntact { get; init; }
    /// <summary>The signature covers the whole file: nothing was appended after it (later signatures append, too).</summary>
    public bool CoversWholeDocument { get; init; }
    /// <summary>The signature has a box on a page; false for an invisible one.</summary>
    public bool IsVisible { get; init; }
    /// <summary>For a certification signature, the changes it allows afterwards; <see cref="PdfCertificationLevel.None"/> for an ordinary one.</summary>
    public PdfCertificationLevel Certification { get; init; }
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
    /// <summary>
    /// Whether the signer's certificate and the ones above it were revoked, by the revocation information in the file
    /// and, when a client was given, fetched from the issuers.
    /// </summary>
    public PdfRevocationStatus Revocation { get; init; }
    /// <summary>The PAdES baseline level the signature meets.</summary>
    public PadesLevel Level { get; init; }
    /// <summary>Why the signature is not intact, not trusted or revoked, when it is.</summary>
    public string? Problem { get; init; }
}

/// <summary>What <see cref="PdfSignatureVerifier.VerifyAsync"/> checks against.</summary>
public sealed class PdfVerificationOptions
{
    /// <summary>Root certificates to build chains to; null skips the chain check.</summary>
    public X509Certificate2Collection? TrustedRoots { get; init; }

    /// <summary>
    /// Fetches revocation information the file does not carry. Null checks with the file's own only, which is all a
    /// long-term (B-LT) signature needs.
    /// </summary>
    public IRevocationClient? Revocation { get; init; }

    /// <summary>Issuer certificates the file does not embed: intermediates, for building chains and checking revocation.</summary>
    public X509Certificate2Collection? Certificates { get; init; }
}
