using LoupixDeck.Plugin.Plexamp.Features;
using LoupixDeck.Plugin.Plexamp.Plex;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp;

public sealed class PlexampPlugin : LoupixPlugin, IPluginSettingsPage, IPluginRequirements
{
    /// <summary>Material Design Icons "music" / "album" glyphs.</summary>
    internal const string MusicGlyph = "\U000F075A";
    internal const string AlbumGlyph = "\U000F0025";

    private const string ClientIdentifierKey = "clientIdentifier";

    private readonly List<IPluginCommand> _commands = [];

    private IPluginHost? _host;
    private PlexClient? _client;
    private PlexampPlayer? _player;
    private MusicLibrary? _library;
    private AlbumPlaybackService? _playback;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "plexamp",
        Name = "Plexamp",
        Version = new Version(1, 0, 0),
        SdkVersion = SdkInfo.Version,
        Author = "FlyingSlig",
        Description = "Browse Plex music and play albums, singles and compilations on a local Plexamp player."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;

        string clientIdentifier = host.Settings.Get<string>(ClientIdentifierKey)?.Trim() ?? string.Empty;
        if (clientIdentifier.Length < 8)
        {
            clientIdentifier = Guid.NewGuid().ToString();

            try
            {
                host.Settings.Set(ClientIdentifierKey, clientIdentifier);
                host.Settings.Save();
            }
            catch (Exception ex)
            {
                // A read-only settings file must not stop the plugin from loading.
                host.Logger.Warn($"Could not persist the Plex client identifier: {ex.Message}");
            }
        }

        _client = new PlexClient(clientIdentifier);
        _player = new PlexampPlayer(clientIdentifier);
        _library = new MusicLibrary(_client);
        _playback = new AlbumPlaybackService(_client, _player);

        _commands.Add(new BrowseMusicCommand(_library, _playback));

        host.Logger.Info($"Plexamp plugin loaded (client {clientIdentifier[..8]}…).");
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new()
        {
            Group = "Plexamp",
            Description = "Browse Plex music and play it on Plexamp",
            Icon = MusicGlyph
        }
    ];

    public override void Shutdown()
    {
        _client?.Dispose();
        _player?.Dispose();
        _client = null;
        _player = null;
        base.Shutdown();
    }

    // ---- IPluginSettingsPage -------------------------------------------------

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } =
    [
        new()
        {
            Key = "__heading_server",
            Label = "Plex server",
            Kind = PluginSettingKind.Heading
        },
        new()
        {
            Key = PlexConfig.ServerUrlKey,
            Label = "Server URL",
            Kind = PluginSettingKind.Text,
            DefaultValue = PlexConfig.DefaultServerUrl,
            Description = "Address of the Plex Media Server, for example http://192.168.1.10:32400"
        },
        new()
        {
            Key = PlexConfig.TokenKey,
            Label = "Plex token",
            Kind = PluginSettingKind.Password,
            DefaultValue = string.Empty,
            Description = "Your X-Plex-Token. Used to read the music library and to let Plexamp stream."
        },
        new()
        {
            Key = PlexConfig.LibraryKey,
            Label = "Music library",
            Kind = PluginSettingKind.Text,
            DefaultValue = string.Empty,
            Description = "Optional library name or ID when the server has more than one music library. Leave empty to choose each time."
        },
        new()
        {
            Key = "__heading_player",
            Label = "Plexamp player",
            Kind = PluginSettingKind.Heading
        },
        new()
        {
            Key = PlexConfig.PlayerUrlKey,
            Label = "Plexamp URL",
            Kind = PluginSettingKind.Text,
            DefaultValue = PlexConfig.DefaultPlayerUrl,
            Description = "Local Plexamp HTTP endpoint. The desktop app and headless Plexamp use port 32500."
        },
        new()
        {
            Key = "__heading_behaviour",
            Label = "Behaviour",
            Kind = PluginSettingKind.Heading
        },
        new()
        {
            Key = PlexampSettings.ReturnToWorkspaceKey,
            Label = "Return to workspace after playing",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = true,
            Description = "Close the music browser and go back to the workspace once an album starts on Plexamp."
        }
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions =>
    [
        new()
        {
            Label = "Test Plex server",
            Invoke = TestServerAsync
        },
        new()
        {
            Label = "Test Plexamp player",
            Invoke = TestPlayerAsync
        },
        new()
        {
            Label = "Discover Plexamp players",
            Invoke = DiscoverPlayersAsync
        }
    ];

    public void OnSettingsSaved()
    {
        _library?.Invalidate();
        _player?.InvalidateInfo();

        try { _host?.RequestRequirementsRefresh(); }
        catch (MissingMethodException) { /* older host */ }
    }

    // ---- IPluginRequirements -------------------------------------------------

    public IReadOnlyList<PluginRequirement> GetRequirements()
    {
        IPluginHost? host = _host;
        PlexConfig config = host is null
            ? new PlexConfig(string.Empty, string.Empty, string.Empty, string.Empty)
            : PlexConfig.Read(host.Settings);

        bool configured = config.HasServer;

        return
        [
            new PluginRequirement
            {
                Id = "plexamp-server",
                Name = "Plex server",
                IsMet = configured,
                Message = configured ? null : "No Plex server URL or token is configured.",
                InstallHint = configured
                    ? null
                    : "Open the Plexamp plugin settings and fill in the Plex server URL and token."
            }
        ];
    }

    // ---- Settings diagnostics ------------------------------------------------

    private async Task<string> TestServerAsync()
    {
        IPluginHost? host = _host;
        PlexClient? client = _client;

        if (host is null || client is null)
            return string.Empty;

        PlexConfig config = PlexConfig.Read(host.Settings);
        if (!config.HasServer)
            return "Set the server URL and token first";

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(12));

        try
        {
            var root = await client.GetServerRootAsync(config, timeout.Token).ConfigureAwait(false);
            var sections = await client.GetMusicSectionsAsync(config, timeout.Token).ConfigureAwait(false);

            string name = PlexClient.Attr(root, "friendlyName")
                          ?? PlexClient.Attr(root, "machineIdentifier")
                          ?? "Plex";

            return $"{name}: {sections.Count} music librar{(sections.Count == 1 ? "y" : "ies")}";
        }
        catch (Exception ex)
        {
            host.Logger.Error("Testing the Plex server connection failed", ex);
            return $"Failed: {Short(ex.Message)}";
        }
    }

    private async Task<string> TestPlayerAsync()
    {
        IPluginHost? host = _host;
        PlexampPlayer? player = _player;

        if (host is null || player is null)
            return string.Empty;

        PlexConfig config = PlexConfig.Read(host.Settings);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(12));

        try
        {
            var info = await player.GetInfoAsync(config, timeout.Token).ConfigureAwait(false);
            string state = await player.GetPlaybackStateAsync(config, timeout.Token).ConfigureAwait(false);
            return $"{info.Title}: {state}";
        }
        catch (Exception ex)
        {
            host.Logger.Error("Testing the Plexamp player connection failed", ex);
            return $"Failed: {Short(ex.Message)}";
        }
    }

    private async Task<string> DiscoverPlayersAsync()
    {
        IPluginHost? host = _host;
        PlexClient? client = _client;

        if (host is null || client is null)
            return string.Empty;

        PlexConfig config = PlexConfig.Read(host.Settings);
        if (!config.HasServer)
            return "Set the server URL and token first";

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(12));

        try
        {
            IReadOnlyList<PlexPlayerEndpoint> players = await client
                .GetPlexampClientsAsync(config, timeout.Token)
                .ConfigureAwait(false);

            if (players.Count == 0)
                return "No Plexamp players found";

            foreach (PlexPlayerEndpoint player in players)
                host.Logger.Info($"Plexamp player found: {player.Name} at {player.Url}");

            string first = players[0].Url;
            return players.Count == 1
                ? $"Found: {first}"
                : $"Found {players.Count}, first: {first}";
        }
        catch (Exception ex)
        {
            host.Logger.Error("Discovering Plexamp players failed", ex);
            return $"Failed: {Short(ex.Message)}";
        }
    }

    private static string Short(string text)
    {
        string single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= 120 ? single : single[..120] + "…";
    }
}
