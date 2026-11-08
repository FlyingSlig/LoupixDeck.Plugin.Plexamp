namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>
/// Caches the Plex library hierarchy so moving back and forth between folders does not
/// hammer the server. Lists are keyed by the settings signature, so editing the server
/// URL, token or selected library throws all of it away.
/// </summary>
internal sealed class MusicLibrary(PlexClient client)
{
    private static readonly TimeSpan SectionTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(5);

    private const int ArtworkSize = 256;

    private readonly PlexClient _client = client;
    private readonly ArtworkCache _artwork = new();
    private readonly object _gate = new();

    private string _signature = string.Empty;
    private List<PlexLibrarySection>? _sections;
    private DateTime _sectionsAt;

    private readonly Dictionary<string, Cache<PlexArtist>> _artists = new();
    private readonly Dictionary<string, Cache<PlexAlbum>> _albums = new();

    public async Task<IReadOnlyList<PlexLibrarySection>> GetSectionsAsync(
        PlexConfig config,
        CancellationToken cancellationToken,
        bool force = false)
    {
        string signature = Prepare(config);

        lock (_gate)
        {
            if (!force && _sections is not null && DateTime.UtcNow - _sectionsAt < SectionTtl)
                return _sections;
        }

        IReadOnlyList<PlexLibrarySection> loaded = await _client
            .GetMusicSectionsAsync(config, cancellationToken)
            .ConfigureAwait(false);

        lock (_gate)
        {
            if (_signature == signature)
            {
                _sections = loaded.ToList();
                _sectionsAt = DateTime.UtcNow;
            }
        }

        return loaded;
    }

    public async Task<IReadOnlyList<PlexArtist>> GetArtistsAsync(
        PlexConfig config,
        PlexLibrarySection section,
        CancellationToken cancellationToken,
        bool force = false)
    {
        string signature = Prepare(config);
        string key = section.Key;

        lock (_gate)
        {
            if (!force && _artists.TryGetValue(key, out Cache<PlexArtist>? cached)
                && DateTime.UtcNow - cached.At < ListTtl)
                return cached.Items;
        }

        IReadOnlyList<PlexArtist> loaded = await _client
            .GetArtistsAsync(config, section, cancellationToken)
            .ConfigureAwait(false);

        lock (_gate)
        {
            if (_signature == signature)
                _artists[key] = new Cache<PlexArtist>(loaded.ToList(), DateTime.UtcNow);
        }

        return loaded;
    }

    public async Task<IReadOnlyList<PlexAlbum>> GetAlbumsAsync(
        PlexConfig config,
        PlexLibrarySection section,
        PlexArtist artist,
        CancellationToken cancellationToken,
        bool force = false)
    {
        string signature = Prepare(config);
        string key = $"{section.Key}|{artist.RatingKey}";

        lock (_gate)
        {
            if (!force && _albums.TryGetValue(key, out Cache<PlexAlbum>? cached)
                && DateTime.UtcNow - cached.At < ListTtl)
                return cached.Items;
        }

        IReadOnlyList<PlexAlbum> loaded = await _client
            .GetAlbumsAsync(config, section, artist, cancellationToken)
            .ConfigureAwait(false);

        lock (_gate)
        {
            if (_signature == signature)
                _albums[key] = new Cache<PlexAlbum>(loaded.ToList(), DateTime.UtcNow);
        }

        return loaded;
    }

    /// <summary>Square PNG bytes for a Plex artwork path, or null when it cannot be read.</summary>
    public Task<byte[]?> GetArtworkAsync(
        PlexConfig config,
        string? thumbPath,
        CancellationToken cancellationToken) =>
        _artwork.GetAsync(_client, config, thumbPath, ArtworkSize, cancellationToken);

    public void Invalidate()
    {
        lock (_gate)
        {
            _signature = string.Empty;
            _sections = null;
            _artists.Clear();
            _albums.Clear();
        }

        _artwork.Clear();
    }

    /// <summary>The configured section, or null when the user has to pick one.</summary>
    public static PlexLibrarySection? ResolveSection(
        IReadOnlyList<PlexLibrarySection> sections,
        string configuredLibrary)
    {
        if (sections.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(configuredLibrary))
            return sections.Count == 1 ? sections[0] : null;

        string wanted = configuredLibrary.Trim();
        return sections.FirstOrDefault(s =>
                   string.Equals(s.Key, wanted, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(s.Title, wanted, StringComparison.OrdinalIgnoreCase))
               ?? (sections.Count == 1 ? sections[0] : null);
    }

    private string Prepare(PlexConfig config)
    {
        string signature = config.Signature;

        lock (_gate)
        {
            if (_signature == signature)
                return signature;

            _signature = signature;
            _sections = null;
            _artists.Clear();
            _albums.Clear();
        }

        return signature;
    }

    private sealed record Cache<T>(IReadOnlyList<T> Items, DateTime At);
}
