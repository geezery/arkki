using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Arkki.Pdf.Signing.Internal;

namespace Arkki.Pdf.Signing;

/// <summary>
/// Fetches what a certificate's issuer says about it: an OCSP response or a certificate revocation list. Implement it
/// to reach them your own way (a cache, a proxy, files), or use <see cref="HttpRevocationClient"/>.
/// </summary>
public interface IRevocationClient
{
    /// <summary>
    /// The DER-encoded OCSP response about <paramref name="certificate"/> from its issuer's responder, or null when
    /// there is no responder or it cannot be reached.
    /// </summary>
    Task<byte[]?> GetOcspResponseAsync(X509Certificate2 certificate, X509Certificate2 issuer, CancellationToken cancellationToken = default);

    /// <summary>
    /// The DER-encoded revocation list that covers <paramref name="certificate"/>, or null when the certificate names
    /// none or it cannot be fetched.
    /// </summary>
    Task<byte[]?> GetCrlAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches OCSP responses and revocation lists over HTTP from the addresses a certificate carries (its authority
/// information access and CRL distribution point extensions). An address that fails gives null, so the caller can try
/// the other kind.
/// </summary>
public sealed class HttpRevocationClient : IRevocationClient
{
    private readonly HttpClient _http;

    /// <param name="http">The client to send with; its lifetime, timeout and proxy stay the caller's.</param>
    public HttpRevocationClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetOcspResponseAsync(X509Certificate2 certificate, X509Certificate2 issuer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(issuer);
        byte[] request = OcspResponse.BuildRequest(certificate, issuer);
        foreach (Uri url in OcspUrls(certificate))
        {
            try
            {
                using var content = new ByteArrayContent(request);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/ocsp-request");
                using HttpResponseMessage response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetCrlAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        foreach (Uri url in CrlUrls(certificate))
        {
            try
            {
                using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return FromPem(body);
            }
            catch (HttpRequestException) { }
        }
        return null;
    }

    /// <summary>The HTTP addresses of the OCSP responders a certificate names.</summary>
    public static IReadOnlyList<Uri> OcspUrls(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var urls = new List<Uri>();
        foreach (X509AuthorityInformationAccessExtension aia in certificate.Extensions.OfType<X509AuthorityInformationAccessExtension>())
            foreach (string text in aia.EnumerateOcspUris()) AddHttp(urls, text);
        return urls;
    }

    /// <summary>The HTTP addresses of the revocation lists a certificate names.</summary>
    public static IReadOnlyList<Uri> CrlUrls(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var urls = new List<Uri>();
        X509Extension? extension = certificate.Extensions["2.5.29.31"]; // cRLDistributionPoints
        if (extension is null) return urls;
        try
        {
            // SEQUENCE OF DistributionPoint { [0] { [0] fullName: GeneralNames { [6] URI … } } … }
            var uri = new Asn1Tag(TagClass.ContextSpecific, 6);
            var first = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            AsnReader points = new AsnReader(extension.RawData, AsnEncodingRules.BER).ReadSequence();
            while (points.HasData)
            {
                AsnReader point = points.ReadSequence();
                if (!point.HasData || !point.PeekTag().HasSameClassAndValue(first)) continue;
                AsnReader name = point.ReadSequence(first);
                if (!name.HasData || !name.PeekTag().HasSameClassAndValue(first)) continue;
                AsnReader names = name.ReadSequence(first);
                while (names.HasData)
                {
                    if (names.PeekTag().HasSameClassAndValue(uri)) AddHttp(urls, names.ReadCharacterString(UniversalTagNumber.IA5String, uri));
                    else names.ReadEncodedValue();
                }
            }
        }
        catch (AsnContentException) { }
        return urls;
    }

    private static void AddHttp(List<Uri> urls, string text)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? url) && url.Scheme is "http" or "https" && !urls.Contains(url)) urls.Add(url);
    }

    /// <summary>Some authorities serve the list as PEM text; the DER inside is what a PDF stores.</summary>
    private static byte[] FromPem(byte[] body)
    {
        if (body.Length == 0 || body[0] == 0x30) return body;
        string text = System.Text.Encoding.ASCII.GetString(body);
        int begin = text.IndexOf("-----BEGIN X509 CRL-----", StringComparison.Ordinal), end = text.IndexOf("-----END X509 CRL-----", StringComparison.Ordinal);
        if (begin < 0 || end < begin) return body;
        try { return Convert.FromBase64String(text[(begin + 24)..end]); }
        catch (FormatException) { return body; }
    }
}
