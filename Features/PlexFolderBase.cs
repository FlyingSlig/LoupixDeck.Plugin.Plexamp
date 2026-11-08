using LoupixDeck.Plugin.Plexamp.Plex;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>
/// Base for folders that load data from Plex. Loading starts in OnEnter and is cancelled
/// in OnExit, so leaving a folder does not leave a request running against a dead view.
/// </summary>
internal abstract class PlexFolderBase(
    IPluginHost host,
    MusicLibrary library,
    AlbumPlaybackService playback,
    PlexConfig config)
    : PagedFolderBase(host)
{
    private readonly Dictionary<int, byte[]> _artwork = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _artCts;
    private int _artGeneration;
    private bool _active;

    protected MusicLibrary Library { get; } = library;
    protected AlbumPlaybackService Playback { get; } = playback;
    protected PlexConfig Config { get; } = config;

    protected virtual bool LoadsOnEnter => true;

    protected bool IsActive => _active;

    protected bool IsLoading { get; private set; }

    protected string? LoadError { get; private set; }

    public sealed override void OnEnter()
    {
        _active = true;
        OnFolderEntered();

        if (LoadsOnEnter)
            StartLoad();
        else
            QueueArtworkForPage();
    }

    public sealed override void OnExit()
    {
        _active = false;
        OnFolderExited();

        CancellationTokenSource? cts = _loadCts;
        _loadCts = null;

        if (cts is not null)
        {
            try { cts.Cancel(); }
            catch { /* shutting down */ }
            cts.Dispose();
        }

        CancellationTokenSource? artCts = _artCts;
        _artCts = null;

        if (artCts is not null)
        {
            try { artCts.Cancel(); }
            catch { /* shutting down */ }
            artCts.Dispose();
        }
    }

    /// <summary>Loads one folder's data. Runs on the host's synchronization context.</summary>
    protected virtual Task LoadCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Called once when the folder becomes visible again; clear transient tile state here.</summary>
    protected virtual void OnFolderEntered()
    {
    }

    /// <summary>Called once when the folder is left; cancel transient tile state here.</summary>
    protected virtual void OnFolderExited()
    {
    }

    /// <summary>
    /// The artwork paths for the visible page, as (item index, Plex thumb path). Folders that
    /// can show artwork implement this; the default keeps text-only tiles.
    /// </summary>
    protected virtual IEnumerable<(int Index, string? Thumb)> PageArtwork(int start, int count) => [];

    /// <summary>The cached artwork for one item, or null while it is still loading.</summary>
    protected byte[]? Artwork(int index)
    {
        lock (_artwork)
            return _artwork.TryGetValue(index, out byte[]? bytes) ? bytes : null;
    }

    protected override void OnPageChanged() => QueueArtworkForPage();

    protected override FolderEntry? StatusEntry()
    {
        if (IsLoading)
        {
            return new FolderEntry
            {
                SlotIndex = 0,
                Text = "Loading…",
                TextSize = 16,
                BackColor = FolderTheme.Tile,
                TextColor = FolderTheme.Muted
            };
        }

        if (LoadError is not null)
        {
            return new FolderEntry
            {
                SlotIndex = 0,
                Text = $"{LoadError}\nTap to retry",
                TextSize = 12,
                BackColor = FolderTheme.Error,
                OnPress = () =>
                {
                    StartLoad();
                    return Task.CompletedTask;
                }
            };
        }

        return null;
    }

    private void StartLoad()
    {
        if (!_active) return;

        ClearArtwork();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = LoadAsync(_loadCts.Token);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        LoadError = null;
        RaiseEntriesChanged();

        try
        {
            await LoadCoreAsync(cancellationToken);
            QueueArtworkForPage();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LoadError = Short(ex.Message);
            Host.Logger.Error($"{GetType().Name} ({Title}) failed to load", ex);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
                if (_active)
                    RaiseEntriesChanged();
            }
        }
    }

    private void QueueArtworkForPage()
    {
        if (!_active)
            return;

        (int start, int count) = CurrentPageRange();
        List<(int Index, string Thumb)> items = PageArtwork(start, count)
            .Where(a => !string.IsNullOrWhiteSpace(a.Thumb))
            .Select(a => (a.Index, a.Thumb!))
            .ToList();

        if (items.Count == 0)
            return;

        _artCts?.Cancel();
        _artCts?.Dispose();
        _artCts = new CancellationTokenSource();
        _ = LoadArtworkAsync(items, _artGeneration, _artCts.Token);
    }

    private async Task LoadArtworkAsync(
        List<(int Index, string Thumb)> items,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(items.Select(async item =>
            {
                byte[]? bytes = await Library
                    .GetArtworkAsync(Config, item.Thumb, cancellationToken)
                    .ConfigureAwait(false);

                if (bytes is null || generation != Volatile.Read(ref _artGeneration))
                    return;

                lock (_artwork)
                    _artwork[item.Index] = bytes;
            }));
        }
        catch (OperationCanceledException)
        {
            // Page turns and folder exits cancel artwork; optional art must never fail a folder.
            return;
        }
        catch (Exception ex)
        {
            Host.Logger.Warn($"{GetType().Name} ({Title}) could not load artwork: {ex.Message}");
            return;
        }

        if (_active && !cancellationToken.IsCancellationRequested)
            RaiseEntriesChanged();
    }

    private void ClearArtwork()
    {
        Interlocked.Increment(ref _artGeneration);
        _artCts?.Cancel();
        _artCts?.Dispose();
        _artCts = null;

        lock (_artwork)
            _artwork.Clear();
    }

    private static string Short(string text)
    {
        string single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= 140 ? single : single[..140] + "…";
    }
}
