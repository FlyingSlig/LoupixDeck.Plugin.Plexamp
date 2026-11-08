namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>
/// Small in-memory cache for Plex thumbnails. Artwork is fetched at most once per thumb
/// path within the TTL, and the HTTP fetch gate keeps a page of 14 covers from opening
/// 14 connections at the same instant.
/// </summary>
internal sealed class ArtworkCache
{
    private const int MaxEntries = 400;
    private const int FetchConcurrency = 4;

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _fetchGate = new(FetchConcurrency);

    public async Task<byte[]?> GetAsync(
        PlexClient client,
        PlexConfig config,
        string? thumbPath,
        int size,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(thumbPath))
            return null;

        string key = $"{config.ServerUrl}|{size}|{thumbPath}";

        if (TryGet(key, out byte[]? cached))
            return cached;

        await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGet(key, out cached))
                return cached;

            // A broken or slow cover should not hold up the other 13 tiles.
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            byte[] bytes = await client
                .GetThumbnailPngAsync(config, thumbPath, size, timeout.Token)
                .ConfigureAwait(false);

            if (bytes.Length == 0)
                return null;

            lock (_gate)
            {
                _entries[key] = new Entry(bytes, DateTime.UtcNow);
                Prune();
            }

            return bytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A missing cover must never stop a folder from rendering.
            return null;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    public void Clear()
    {
        lock (_gate)
            _entries.Clear();
    }

    private bool TryGet(string key, out byte[]? bytes)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry) && entry is not null && DateTime.UtcNow - entry.At < Ttl)
            {
                bytes = entry.Bytes;
                return true;
            }
        }

        bytes = null;
        return false;
    }

    private void Prune()
    {
        if (_entries.Count <= MaxEntries)
            return;

        foreach (string key in _entries
                     .OrderBy(e => e.Value.At)
                     .Take(_entries.Count - MaxEntries)
                     .Select(e => e.Key)
                     .ToList())
            _entries.Remove(key);
    }

    private sealed record Entry(byte[] Bytes, DateTime At);
}
