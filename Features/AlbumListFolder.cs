using LoupixDeck.Plugin.Plexamp.Plex;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>One release group of an artist. Tapping an album starts it on Plexamp.</summary>
internal sealed class AlbumListFolder(
    IPluginHost host,
    MusicLibrary library,
    AlbumPlaybackService playback,
    PlexConfig config,
    PlexArtist artist,
    AlbumGroup group,
    IReadOnlyList<PlexAlbum> albums)
    : PlexFolderBase(host, library, playback, config)
{
    private int _statusIndex = -1;
    private string? _status;
    private int _statusGeneration;

    protected override bool LoadsOnEnter => false;

    public override string Title => $"{artist.Title} · {AlbumGrouping.Label(group)}{PageSuffix}";

    protected override int ItemCount => albums.Count;

    protected override string EmptyText => "No releases here";

    protected override IEnumerable<(int Index, string? Thumb)> PageArtwork(int start, int count)
    {
        for (int i = start; i < Math.Min(start + count, albums.Count); i++)
            yield return (i, albums[i].Thumb);
    }

    protected override FolderEntry BuildItem(int index, int slot)
    {
        PlexAlbum album = albums[index];
        string? year = album.Year?.ToString();
        string? status = index == _statusIndex ? _status : null;
        string secondLine = status ?? year ?? string.Empty;
        string text = secondLine.Length == 0 ? album.Title : $"{album.Title}\n{secondLine}";

        byte[]? artwork = Artwork(index);

        Action<IRenderCanvas>? render = null;
        if (artwork is not null)
        {
            byte[] image = artwork;
            render = canvas => TileRenderer.Render(canvas, image, album.Title, year, status);
        }

        return new FolderEntry
        {
            SlotIndex = slot,
            Text = artwork is null ? text : string.Empty,
            TextSize = 13,
            BackColor = FolderTheme.ForGroup(group),
            Render = render,
            OnPress = () => PlayAsync(album, index)
        };
    }

    protected override void OnFolderEntered() => ClearStatus();

    protected override void OnFolderExited() => ClearStatus();

    private async Task PlayAsync(PlexAlbum album, int index)
    {
        SetStatus(index, "Starting…", TimeSpan.FromSeconds(8));

        try
        {
            await Playback.PlayAsync(Config, album, CancellationToken.None);

            // Leave the browser only if this folder is still the one on screen. If the user
            // navigated elsewhere while playback was starting, closing every folder here
            // would yank them out of a folder this tap did not open.
            if (IsActive && PlexampSettings.ReturnToWorkspace(Host.Settings))
            {
                HostFolders.CloseAll(Host);
                return;
            }

            SetStatus(index, "Playing", TimeSpan.FromSeconds(2.5));
        }
        catch (Exception ex)
        {
            Host.Logger.Error($"Playing album \"{album.Title}\" on Plexamp failed", ex);
            SetStatus(index, "Play failed", TimeSpan.FromSeconds(4));
        }
    }

    private void SetStatus(int index, string text, TimeSpan duration)
    {
        if (!IsActive)
            return;

        _statusIndex = index;
        _status = text;
        int generation = Interlocked.Increment(ref _statusGeneration);
        RaiseEntriesChanged();
        _ = ClearStatusAfterAsync(duration, generation);
    }

    private async Task ClearStatusAfterAsync(TimeSpan delay, int generation)
    {
        await Task.Delay(delay);

        if (!IsActive || generation != Volatile.Read(ref _statusGeneration))
            return;

        ClearStatus();
        RaiseEntriesChanged();
    }

    private void ClearStatus()
    {
        Interlocked.Increment(ref _statusGeneration);
        _statusIndex = -1;
        _status = null;
    }
}
