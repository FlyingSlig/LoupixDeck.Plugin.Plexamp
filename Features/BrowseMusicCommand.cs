using LoupixDeck.Plugin.Plexamp.Plex;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>Opens the Plex music browser on the touch screen.</summary>
internal sealed class BrowseMusicCommand(
    MusicLibrary library,
    AlbumPlaybackService playback) : IPluginCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "Plexamp.BrowseMusic",
        DisplayName = "Browse music",
        Group = "Plexamp",
        Icon = PlexampPlugin.MusicGlyph,
        Description = "Browse Plex artists and play albums, singles and compilations on Plexamp"
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton | ButtonTargets.SimpleButton;

    public async Task Execute(CommandContext ctx)
    {
        IPluginHost host = ctx.Host;
        PlexConfig config = PlexConfig.Read(host.Settings);

        if (!config.HasServer)
        {
            host.OpenFolder(new MessageFolder(
                "Plexamp",
                "Plex server not configured.",
                "Open Settings → Plugins → Plexamp and set the server URL and token."));
            return;
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));

        try
        {
            IReadOnlyList<PlexLibrarySection> sections = await library
                .GetSectionsAsync(config, timeout.Token)
                .ConfigureAwait(true);

            if (sections.Count == 0)
            {
                host.OpenFolder(new MessageFolder(
                    "Plexamp",
                    "No music libraries found on the Plex server.",
                    "Create a music library in Plex, then try again."));
                return;
            }

            PlexLibrarySection? selected = MusicLibrary.ResolveSection(sections, config.Library);

            if (selected is not null)
            {
                host.OpenFolder(new ArtistsFolder(host, library, playback, config, selected));
                return;
            }

            host.OpenFolder(new LibrariesFolder(host, library, playback, config, sections));
        }
        catch (Exception ex)
        {
            host.Logger.Error("Opening the Plex music browser failed", ex);
            host.OpenFolder(new MessageFolder(
                "Plexamp",
                "Could not reach the Plex server.",
                ex.Message,
                () =>
                {
                    host.ExecuteCommand(Descriptor.CommandName);
                    return Task.CompletedTask;
                }));
        }
    }
}
