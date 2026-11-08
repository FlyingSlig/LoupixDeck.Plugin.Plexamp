using System.Xml.Linq;

namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>
/// The Plex Companion side of Plexamp's local HTTP API (port 32500 by default).
/// Commands are GETs with a monotonically increasing commandID; Plexamp answers with
/// either XML or a plain "OK".
/// </summary>
internal sealed class PlexampPlayer : IDisposable
{
    private static readonly TimeSpan InfoTtl = TimeSpan.FromMinutes(5);

    private readonly PlexHttp _http = new();
    private readonly string _clientIdentifier;
    private readonly object _infoLock = new();

    private long _commandId;
    private string _infoUrl = string.Empty;
    private PlexClientInfo? _info;
    private DateTime _infoAt;

    public PlexampPlayer(string clientIdentifier) => _clientIdentifier = clientIdentifier;

    public void InvalidateInfo()
    {
        lock (_infoLock)
        {
            _info = null;
            _infoUrl = string.Empty;
        }
    }

    public async Task<PlexClientInfo> GetInfoAsync(PlexConfig config, CancellationToken cancellationToken)
    {
        lock (_infoLock)
        {
            if (_info is not null
                && string.Equals(_infoUrl, config.PlayerUrl, StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow - _infoAt < InfoTtl)
                return _info;
        }

        XElement root = await _http
            .GetAsync(config.PlayerUrl, "/resources", config.Token, _clientIdentifier, cancellationToken)
            .ConfigureAwait(false);

        XElement node = root.Elements()
                            .FirstOrDefault(e => e.Name.LocalName is "Player" or "Device" or "Server")
                        ?? root;

        PlexClientInfo info = new(
            PlexClient.Attr(node, "title") ?? PlexClient.Attr(node, "name") ?? "Plexamp",
            PlexClient.Attr(node, "machineIdentifier"),
            PlexClient.Attr(node, "product"));

        lock (_infoLock)
        {
            _info = info;
            _infoUrl = config.PlayerUrl;
            _infoAt = DateTime.UtcNow;
        }

        return info;
    }

    /// <summary>Best-effort player identity; playback still proceeds without it.</summary>
    private async Task<PlexClientInfo?> TryGetInfoAsync(PlexConfig config, CancellationToken cancellationToken)
    {
        try
        {
            return await GetInfoAsync(config, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task PlayMediaAsync(
        PlexConfig config,
        PlexServerIdentity server,
        PlexPlayQueue queue,
        CancellationToken cancellationToken)
    {
        await SendCommandAsync(
                config,
                "/player/playback/playMedia",
                [
                    new("machineIdentifier", server.MachineIdentifier),
                    new("address", server.Address),
                    new("port", server.Port),
                    new("protocol", server.Protocol),
                    new("token", config.Token),
                    new("key", queue.SelectedKey),
                    new("containerKey", $"/playQueues/{queue.Id}?window=200&own=1"),
                    new("providerIdentifier", "com.plexapp.plugins.library"),
                    new("type", "music")
                ],
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the player's current music state (playing, paused, stopped, …).</summary>
    public async Task<string> GetPlaybackStateAsync(PlexConfig config, CancellationToken cancellationToken)
    {
        XElement root = await SendCommandAsync(
                config,
                "/player/timeline/poll",
                [new("wait", "0")],
                cancellationToken)
            .ConfigureAwait(false);

        XElement? timeline = root.Elements()
                                 .FirstOrDefault(e => e.Name.LocalName == "Timeline"
                                                      && PlexClient.Attr(e, "type") == "music")
                             ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "Timeline");

        return PlexClient.Attr(timeline ?? root, "state") ?? "stopped";
    }

    private async Task<XElement> SendCommandAsync(
        PlexConfig config,
        string path,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken)
    {
        PlexClientInfo? info = await TryGetInfoAsync(config, cancellationToken).ConfigureAwait(false);
        long commandId = Interlocked.Increment(ref _commandId);

        List<KeyValuePair<string, string?>> query = [new("commandID", commandId.ToString())];
        query.AddRange(parameters);

        return await _http.GetAsync(
                config.PlayerUrl,
                path,
                config.Token,
                _clientIdentifier,
                cancellationToken,
                query,
                info?.MachineIdentifier,
                tolerateNonXml: true)
            .ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}
