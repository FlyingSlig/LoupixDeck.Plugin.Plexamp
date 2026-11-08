using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>
/// The small HTTP/XML layer both the Plex server and the Plexamp player speak. It keeps
/// tokens in headers (except where the Companion protocol requires them as a query
/// parameter) and turns every non-success response into a <see cref="PlexApiException"/>.
/// </summary>
internal sealed class PlexHttp : IDisposable
{
    private readonly HttpClient _http;

    public PlexHttp()
    {
        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(3),
            AutomaticDecompression = DecompressionMethods.All
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public Task<XElement> GetAsync(
        string baseUrl,
        string path,
        string? token,
        string clientIdentifier,
        CancellationToken cancellationToken,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        string? targetClientIdentifier = null,
        bool tolerateNonXml = false) =>
        SendAsync(HttpMethod.Get, baseUrl, path, token, clientIdentifier, cancellationToken, query,
            targetClientIdentifier, tolerateNonXml);

    /// <summary>Downloads an image (artwork), returning the raw bytes.</summary>
    public async Task<byte[]> GetBytesAsync(
        string baseUrl,
        string path,
        string? token,
        string clientIdentifier,
        CancellationToken cancellationToken,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, baseUrl, path, token, clientIdentifier,
            query, null, "image/png");

        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string detail = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                .Trim()
                .ReplaceLineEndings(" ");

            if (detail.Length > 300) detail = detail[..300] + "…";
            throw new PlexApiException(
                $"{baseUrl} returned {(int)response.StatusCode} {response.ReasonPhrase}" +
                (detail.Length > 0 ? $": {detail}" : ""));
        }

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new PlexApiException($"Plex returned '{mediaType ?? "unknown"}' instead of an image.");

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<XElement> SendAsync(
        HttpMethod method,
        string baseUrl,
        string path,
        string? token,
        string clientIdentifier,
        CancellationToken cancellationToken,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        string? targetClientIdentifier = null,
        bool tolerateNonXml = false)
    {
        using HttpRequestMessage request = CreateRequest(method, baseUrl, path, token, clientIdentifier,
            query, targetClientIdentifier, "application/xml");

        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string detail = body.Trim().ReplaceLineEndings(" ");
            if (detail.Length > 300) detail = detail[..300] + "…";
            throw new PlexApiException(
                $"{baseUrl} returned {(int)response.StatusCode} {response.ReasonPhrase}" +
                (detail.Length > 0 ? $": {detail}" : ""));
        }

        if (string.IsNullOrWhiteSpace(body))
            return new XElement("MediaContainer");

        try
        {
            return XElement.Parse(body, LoadOptions.None);
        }
        catch (XmlException) when (tolerateNonXml && !body.TrimStart().StartsWith('<'))
        {
            // Plexamp answers successful commands with a plain "OK".
            return new XElement("MediaContainer");
        }
        catch (XmlException ex)
        {
            throw new PlexApiException($"Plex returned non-XML content from {path}.", ex);
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string baseUrl,
        string path,
        string? token,
        string clientIdentifier,
        IReadOnlyList<KeyValuePair<string, string?>>? query,
        string? targetClientIdentifier,
        string accept)
    {
        HttpRequestMessage request = new(method, BuildUri(baseUrl, path, query));
        request.Headers.TryAddWithoutValidation("Accept", accept);

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("X-Plex-Token", token);

        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", clientIdentifier);
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "LoupixDeck Plexamp");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", "1.0.0");
        request.Headers.TryAddWithoutValidation("X-Plex-Device-Name", "LoupixDeck");
        request.Headers.TryAddWithoutValidation("X-Plex-Platform", PlatformName());
        request.Headers.TryAddWithoutValidation("X-Plex-Provides", "controller");

        if (!string.IsNullOrWhiteSpace(targetClientIdentifier))
            request.Headers.TryAddWithoutValidation("X-Plex-Target-Client-Identifier", targetClientIdentifier);

        return request;
    }

    private static Uri BuildUri(string baseUrl, string path, IReadOnlyList<KeyValuePair<string, string?>>? query)
    {
        StringBuilder builder = new(path.TrimStart('/'));

        if (query is { Count: > 0 })
        {
            builder.Append(path.Contains('?') ? '&' : '?');
            bool first = true;

            foreach ((string key, string? value) in query)
            {
                if (value is null) continue;

                if (!first) builder.Append('&');
                builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
                first = false;
            }
        }

        Uri baseUri = new(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, builder.ToString());
    }

    private static string PlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
        return "Linux";
    }

    public void Dispose() => _http.Dispose();
}
