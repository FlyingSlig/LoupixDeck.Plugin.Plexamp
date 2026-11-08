using LoupixDeck.Plugin.Plexamp.Plex;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>All artists in one music library, alphabetically and paged.</summary>
internal sealed class ArtistsFolder(
    IPluginHost host,
    MusicLibrary library,
    AlbumPlaybackService playback,
    PlexConfig config,
    PlexLibrarySection section)
    : PlexFolderBase(host, library, playback, config)
{
    private IReadOnlyList<PlexArtist> _artists = [];

    public override string Title => section.Title + PageSuffix;

    protected override int ItemCount => _artists.Count;

    protected override string EmptyText => "No artists in this library";

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken) =>
        _artists = await Library.GetArtistsAsync(Config, section, cancellationToken);

    protected override IEnumerable<(int Index, string? Thumb)> PageArtwork(int start, int count)
    {
        for (int i = start; i < Math.Min(start + count, _artists.Count); i++)
            yield return (i, _artists[i].Thumb);
    }

    protected override FolderEntry BuildItem(int index, int slot)
    {
        PlexArtist artist = _artists[index];
        byte[]? artwork = Artwork(index);

        Action<IRenderCanvas>? render = null;
        if (artwork is not null)
        {
            byte[] image = artwork;
            render = canvas => TileRenderer.Render(canvas, image, artist.Title, null);
        }

        return new FolderEntry
        {
            SlotIndex = slot,
            Text = artwork is null ? artist.Title : string.Empty,
            TextSize = 14,
            BackColor = FolderTheme.Artist,
            Render = render,
            OnPress = () =>
            {
                Host.OpenFolder(new ArtistFolder(Host, Library, Playback, Config, section, artist));
                return Task.CompletedTask;
            }
        };
    }
}
