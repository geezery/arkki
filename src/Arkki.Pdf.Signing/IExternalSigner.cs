using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Arkki.Pdf.Signing;

/// <summary>
/// A signing key that lives somewhere else: a smart card or hardware security module, a cloud key vault, a remote
/// signing service. Arkki builds the signature and asks only for the one operation that needs the key.
/// </summary>
public interface IExternalSigner
{
    /// <summary>The signer's certificate. Only its public part is used; it needs no private key.</summary>
    X509Certificate2 Certificate { get; }

    /// <summary>
    /// Signs a hash with the certificate's private key and returns the signature value.
    /// <para>For an RSA key: a PKCS #1 v1.5 signature (RSASSA-PKCS1-v1_5), as <c>RSA.SignHash</c> gives with
    /// <c>RSASignaturePadding.Pkcs1</c>. For an elliptic-curve key: an ECDSA signature either as the two numbers
    /// r and s side by side (IEEE P1363, what <c>ECDsa.SignHash</c> and most key vaults return) or as a DER
    /// sequence.</para>
    /// </summary>
    /// <param name="hash">The hash to sign, already computed with <paramref name="hashAlgorithm"/>.</param>
    /// <param name="hashAlgorithm">SHA-256, SHA-384 or SHA-512, as <see cref="PdfSignatureOptions.HashAlgorithm"/> says.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<byte[]> SignHashAsync(byte[] hash, HashAlgorithmName hashAlgorithm, CancellationToken cancellationToken = default);
}
