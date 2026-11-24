using System.Net.Http.Headers;

namespace Arkki.Pdf.Signing;

/// <summary>
/// Sends an RFC 3161 time-stamp request and returns the authority's response. Implement it to reach a
/// time-stamping authority your own way, or use <see cref="HttpTimestampClient"/>.
/// </summary>
public interface ITimestampClient
{
    /// <summary>Posts the DER-encoded TimeStampReq and returns the DER-encoded TimeStampResp.</summary>
    Task<byte[]> RequestAsync(byte[] timestampRequest, CancellationToken cancellationToken = default);
}

/// <summary>An RFC 3161 time-stamping authority over HTTP (<c>application/timestamp-query</c>).</summary>
public sealed class HttpTimestampClient : ITimestampClient
{
    private readonly HttpClient _http;
    private readonly Uri _url;

    /// <param name="http">The client to send with; its lifetime stays the caller's.</param>
    /// <param name="url">The authority's address.</param>
    public HttpTimestampClient(HttpClient http, Uri url)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(url);
        _http = http;
        _url = url;
    }

    /// <inheritdoc />
    public async Task<byte[]> RequestAsync(byte[] timestampRequest, CancellationToken cancellationToken = default)
    {
        using var content = new ByteArrayContent(timestampRequest);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        using HttpResponseMessage response = await _http.PostAsync(_url, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
