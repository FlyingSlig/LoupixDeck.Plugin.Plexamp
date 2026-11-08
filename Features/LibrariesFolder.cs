using LoupixDeck.Plugin.Plexamp.Plex;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>Shown when the account has more than one music library and none is configured.</summary>
internal sealed class LibrariesFolder(
    IPluginHost host,
    MusicLibrary library,
    AlbumPlaybackService playback,
    PlexConfig config,
    IReadOnlyList<PlexLibrarySection> sections)
    : FolderProviderBase
{
    public override string Title => "Plex Music";

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        List<FolderEntry> entries = [];

        for (int i = 0; i < sections.Count; i++)
        {
            int slot = host.FolderGrid.SlotForIndex(i);
            if (slot < 0) break;

            PlexLibrarySection section = sections[i];
            entries.Add(new FolderEntry
            {
                SlotIndex = slot,
                Text = section.Title,
                TextSize = 14,
                BackColor = FolderTheme.Artist,
                OnPress = () =>
                {
                    host.OpenFolder(new ArtistsFolder(host, library, playback, config, section));
                    return Task.CompletedTask;
                }
            });
        }

        if (entries.Count == 0)
        {
            entries.Add(new FolderEntry
            {
                SlotIndex = 0,
                Text = "No music libraries found",
                TextSize = 14,
                BackColor = FolderTheme.Error
            });
        }

        return entries;
    }
}
