using LoupixDeck.Plugin.Plexamp.Plex;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>
/// One artist. The releases are split into Albums, Singles &amp; EPs and Compilations from
/// Plex's release-type metadata; empty groups are not shown.
/// </summary>
internal sealed class ArtistFolder(
    IPluginHost host,
    MusicLibrary library,
    AlbumPlaybackService playback,
    PlexConfig config,
    PlexLibrarySection section,
    PlexArtist artist)
    : PlexFolderBase(host, library, playback, config)
{
    private static readonly AlbumGroup[] GroupOrder =
        [AlbumGroup.Albums, AlbumGroup.Singles, AlbumGroup.Compilations];

    private readonly List<(AlbumGroup Group, IReadOnlyList<PlexAlbum> Albums)> _groups = [];

    public override string Title => artist.Title;

    protected override int ItemCount => _groups.Count;

    protected override string EmptyText => "No releases for this artist";

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PlexAlbum> albums = await Library.GetAlbumsAsync(Config, section, artist, cancellationToken);

        _groups.Clear();

        foreach (AlbumGroup group in GroupOrder)
        {
            List<PlexAlbum> items = albums
                .Where(a => AlbumGrouping.Classify(a) == group)
                .OrderByDescending(a => a.Year ?? int.MinValue)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count > 0)
                _groups.Add((group, items));
        }
    }

    protected override FolderEntry BuildItem(int index, int slot)
    {
        (AlbumGroup group, IReadOnlyList<PlexAlbum> albums) = _groups[index];

        return new FolderEntry
        {
            SlotIndex = slot,
            Text = $"{AlbumGrouping.Label(group)}\n{CountText(albums.Count)}",
            TextSize = 14,
            Bold = true,
            BackColor = FolderTheme.ForGroup(group),
            OnPress = () =>
            {
                Host.OpenFolder(new AlbumListFolder(Host, Library, Playback, Config, artist, group, albums));
                return Task.CompletedTask;
            }
        };
    }

    private static string CountText(int count) => count == 1 ? "1 release" : $"{count} releases";
}
