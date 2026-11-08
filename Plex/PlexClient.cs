using System.Globalization;
using System.Xml.Linq;

namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>Read-only Plex Media Server calls used by the music browser.</summary>
internal sealed class PlexClient : IDisposable
{
    private const int AlbumHydrationConcurrency = 6;

    private readonly PlexHttp _http = new();
    private readonly string _clientIdentifier;

    public PlexClient(string clientIdentifier) => _clientIdentifier = clientIdentifier;

    public Task<XElement> GetServerRootAsync(PlexConfig config, CancellationToken cancellationToken) =>
        _http.GetAsync(config.ServerUrl, "/", config.Token, _clientIdentifier, cancellationToken);

    /// <summary>Machine identifier plus the address/port/protocol Plexamp must use to fetch media.</summary>
    public async Task<PlexServerIdentity> GetIdentityAsync(PlexConfig config, CancellationToken cancellationToken)
    {
        XElement root = await GetServerRootAsync(config, cancellationToken).ConfigureAwait(false);

        string machineIdentifier = Attr(root, "machineIdentifier") ?? string.Empty;
        if (machineIdentifier.Length == 0)
            throw new PlexApiException("The Plex server did not return a machine identifier.");

        Uri uri = new(config.ServerUrl, UriKind.Absolute);
        string protocol = uri.Scheme;
        string port = uri.IsDefaultPort
            ? (protocol == "https" ? "443" : "80")
            : uri.Port.ToString(CultureInfo.InvariantCulture);

        return new PlexServerIdentity(machineIdentifier, uri.DnsSafeHost, port, protocol);
    }

    public async Task<IReadOnlyList<PlexLibrarySection>> GetMusicSectionsAsync(
        PlexConfig config,
        CancellationToken cancellationToken)
    {
        XElement root = await _http
            .GetAsync(config.ServerUrl, "/library/sections", config.Token, _clientIdentifier, cancellationToken)
            .ConfigureAwait(false);

        return Children(root, "Directory")
            .Where(e => Attr(e, "type") == "artist")
            .Select(e => new PlexLibrarySection(Attr(e, "key") ?? string.Empty, Attr(e, "title") ?? "Music"))
            .Where(s => s.Key.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<PlexArtist>> GetArtistsAsync(
        PlexConfig config,
        PlexLibrarySection section,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;

        List<PlexArtist> artists = [];
        HashSet<string> seen = [];

        // Plex usually returns the whole section, but a server may apply its own page
        // size. Read until a page adds nothing new, so large libraries stay complete.
        for (int start = 0; start < 100_000;)
        {
            XElement root = await _http.GetAsync(
                    config.ServerUrl,
                    $"/library/sections/{Uri.EscapeDataString(section.Key)}/all",
                    config.Token,
                    _clientIdentifier,
                    cancellationToken,
                    [
                        new("type", "8"),
                        new("sort", "titleSort:asc"),
                        new("X-Plex-Container-Start", start.ToString(CultureInfo.InvariantCulture)),
                        new("X-Plex-Container-Size", pageSize.ToString(CultureInfo.InvariantCulture))
                    ])
                .ConfigureAwait(false);

            List<PlexArtist> page = Children(root, "Directory")
                .Where(e => Attr(e, "type") == "artist")
                .Select(e => new PlexArtist(
                    Attr(e, "ratingKey") ?? string.Empty,
                    Attr(e, "title") ?? "Unknown artist",
                    Attr(e, "thumb")))
                .Where(a => a.RatingKey.Length > 0)
                .ToList();

            List<PlexArtist> fresh = page.Where(a => seen.Add(a.RatingKey)).ToList();
            artists.AddRange(fresh);
            int added = fresh.Count;

            int? total = ParseInt(Attr(root, "totalSize"));

            if (added == 0)
                break;

            if (total is int expected)
            {
                if (artists.Count >= expected)
                    break;
            }
            else if (page.Count < pageSize)
            {
                break;
            }

            start += page.Count;
        }

        return artists;
    }

    public async Task<IReadOnlyList<PlexAlbum>> GetAlbumsAsync(
        PlexConfig config,
        PlexLibrarySection section,
        PlexArtist artist,
        CancellationToken cancellationToken)
    {
        XElement root = await _http.GetAsync(
                config.ServerUrl,
                $"/library/sections/{Uri.EscapeDataString(section.Key)}/all",
                config.Token,
                _clientIdentifier,
                cancellationToken,
                [
                    new("type", "9"),
                    new("artist.id", artist.RatingKey),
                    new("X-Plex-Container-Size", "1000")
                ])
            .ConfigureAwait(false);

        List<PlexAlbum> albums = Children(root, "Directory")
            .Where(e => Attr(e, "type") == "album")
            .Select(ParseAlbum)
            .Where(a => a.RatingKey.Length > 0)
            .GroupBy(a => a.RatingKey)
            .Select(g => g.First())
            .ToList();

        if (albums.Count == 0)
            return albums;

        return await HydrateSubformatsAsync(config, albums, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The section "all" response does not carry album release types, so each album is
    /// read once to pick up its &lt;Subformat&gt; tags. Failures are non-fatal: an album
    /// whose type cannot be read simply lands in the Albums group.
    /// </summary>
    private async Task<IReadOnlyList<PlexAlbum>> HydrateSubformatsAsync(
        PlexConfig config,
        IReadOnlyList<PlexAlbum> albums,
        CancellationToken cancellationToken)
    {
        using SemaphoreSlim gate = new(AlbumHydrationConcurrency);

        Task<PlexAlbum>[] tasks = albums.Select(async album =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                XElement root = await _http.GetAsync(
                        config.ServerUrl,
                        $"/library/metadata/{Uri.EscapeDataString(album.RatingKey)}",
                        config.Token,
                        _clientIdentifier,
                        cancellationToken,
                        [new("includeSubformats", "1")])
                    .ConfigureAwait(false);

                XElement node = root.Descendants()
                                    .FirstOrDefault(e => e.Name.LocalName is "Directory" or "Metadata" or "Track"
                                                         && Attr(e, "type") == "album")
                                ?? root;

                string format = JoinTags(node, "Format");
                string subformat = JoinTags(node, "Subformat");

                return album with
                {
                    Format = format.Length == 0 ? null : format,
                    Subformat = subformat.Length == 0 ? null : subformat
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return album;
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a server-side audio play queue for the album and returns the key of the
    /// track the queue starts on. Playing through a queue makes Plexamp continue through
    /// the whole album instead of stopping after the first track.
    /// </summary>
    public async Task<PlexPlayQueue> CreatePlayQueueAsync(
        PlexConfig config,
        PlexServerIdentity identity,
        string albumRatingKey,
        CancellationToken cancellationToken)
    {
        string metadataKey = $"/library/metadata/{albumRatingKey}";
        string uri = $"server://{identity.MachineIdentifier}/com.plexapp.plugins.library{metadataKey}";

        XElement root = await _http.SendAsync(
                HttpMethod.Post,
                config.ServerUrl,
                "/playQueues",
                config.Token,
                _clientIdentifier,
                cancellationToken,
                [
                    new("type", "audio"),
                    new("continuous", "1"),
                    new("repeat", "0"),
                    new("own", "1"),
                    new("uri", uri),
                    new("key", metadataKey)
                ])
            .ConfigureAwait(false);

        string queueId = Attr(root, "playQueueID") ?? string.Empty;
        if (queueId.Length == 0)
            throw new PlexApiException("Plex did not return a play queue for this album.");

        List<XElement> tracks = Children(root, "Track").ToList();
        string? selectedItemId = Attr(root, "playQueueSelectedItemID");

        XElement? selected = tracks.FirstOrDefault(t => Attr(t, "playQueueItemID") == selectedItemId)
                             ?? tracks.FirstOrDefault();

        string selectedKey = selected is null ? string.Empty : Attr(selected, "key") ?? string.Empty;
        if (selectedKey.Length == 0)
            throw new PlexApiException("Plex did not return a playable track for this album.");

        return new PlexPlayQueue(queueId, selectedKey);
    }

    /// <summary>
    /// Asks Plex's photo transcoder for a square PNG thumbnail of an artwork path. Plex
    /// returns JPEG for the raw thumb path, so the explicit <c>format=png</c> matters.
    /// </summary>
    public Task<byte[]> GetThumbnailPngAsync(
        PlexConfig config,
        string thumbPath,
        int size,
        CancellationToken cancellationToken) =>
        _http.GetBytesAsync(
            config.ServerUrl,
            "/photo/:/transcode",
            config.Token,
            _clientIdentifier,
            cancellationToken,
            [
                new("width", size.ToString(CultureInfo.InvariantCulture)),
                new("height", size.ToString(CultureInfo.InvariantCulture)),
                new("minSize", "1"),
                new("upscale", "1"),
                new("format", "png"),
                new("url", thumbPath)
            ]);

    /// <summary>
    /// Asks the server which local players it can see and keeps the Plexamp ones.
    /// Used by the settings diagnostics only.
    /// </summary>
    public async Task<IReadOnlyList<PlexPlayerEndpoint>> GetPlexampClientsAsync(
        PlexConfig config,
        CancellationToken cancellationToken)
    {
        XElement root = await _http
            .GetAsync(config.ServerUrl, "/clients", config.Token, _clientIdentifier, cancellationToken)
            .ConfigureAwait(false);

        List<PlexPlayerEndpoint> players = [];

        foreach (XElement node in Children(root, "Server").Concat(Children(root, "Player")))
        {
            string name = Attr(node, "name") ?? Attr(node, "title") ?? "Plexamp";
            string product = Attr(node, "product") ?? string.Empty;

            if (!name.Contains("Plexamp", StringComparison.OrdinalIgnoreCase)
                && !product.Contains("Plexamp", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (XElement connection in Children(node, "Connection"))
            {
                string? url = ConnectionUrl(connection);
                if (url is not null)
                    players.Add(new PlexPlayerEndpoint(name, url));
            }

            string? direct = ConnectionUrl(node);
            if (direct is not null)
                players.Add(new PlexPlayerEndpoint(name, direct));
        }

        return players
            .GroupBy(p => p.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static string? ConnectionUrl(XElement node)
    {
        string? uri = Attr(node, "uri");
        if (!string.IsNullOrWhiteSpace(uri))
        {
            uri = uri.TrimEnd('/');
            return uri.StartsWith("plex://", StringComparison.OrdinalIgnoreCase)
                ? "http://" + uri["plex://".Length..]
                : uri;
        }

        string? address = Attr(node, "address");
        if (string.IsNullOrWhiteSpace(address))
            return null;

        // Plexamp advertises protocol="plex" in /clients; its control API itself is HTTP.
        string protocol = Attr(node, "protocol") ?? "http";
        if (protocol is not ("http" or "https"))
            protocol = "http";

        string? port = Attr(node, "port");

        return string.IsNullOrWhiteSpace(port)
            ? $"{protocol}://{address}"
            : $"{protocol}://{address}:{port}";
    }

    private static PlexAlbum ParseAlbum(XElement element) => new(
        Attr(element, "ratingKey") ?? string.Empty,
        Attr(element, "title") ?? "Unknown album",
        Attr(element, "parentTitle"),
        ParseInt(Attr(element, "year")),
        ParseInt(Attr(element, "leafCount")),
        Attr(element, "thumb") ?? Attr(element, "parentThumb"),
        null,
        null);

    private static string JoinTags(XElement node, string tagName) =>
        string.Join(", ",
            node.Elements()
                .Where(e => e.Name.LocalName == tagName)
                .Select(e => Attr(e, "tag"))
                .Where(t => !string.IsNullOrWhiteSpace(t)));

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

    internal static string? Attr(XElement element, string name) => element.Attribute(name)?.Value;

    private static IEnumerable<XElement> Children(XElement root, string name) =>
        root.Elements().Where(e => e.Name.LocalName == name);

    public void Dispose() => _http.Dispose();
}
