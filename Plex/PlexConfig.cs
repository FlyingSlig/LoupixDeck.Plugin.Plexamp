using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>The settings the plugin needs, read fresh from the host store.</summary>
internal sealed record PlexConfig(string ServerUrl, string Token, string PlayerUrl, string Library)
{
    public const string DefaultServerUrl = "http://127.0.0.1:32400";
    public const string DefaultPlayerUrl = "http://127.0.0.1:32500";

    public const string ServerUrlKey = "serverUrl";
    public const string TokenKey = "token";
    public const string PlayerUrlKey = "playerUrl";
    public const string LibraryKey = "library";

    public bool HasServer => ServerUrl.Length > 0 && Token.Length > 0;

    /// <summary>
    /// Identity of the configuration the caches belong to. A changed server, token or
    /// library selection invalidates every cached list.
    /// </summary>
    public string Signature => $"{ServerUrl}|{Token}|{Library}";

    public static PlexConfig Read(IPluginSettings settings) => new(
        NormalizeUrl(settings.Get<string>(ServerUrlKey), DefaultServerUrl),
        settings.Get<string>(TokenKey)?.Trim() ?? string.Empty,
        NormalizeUrl(settings.Get<string>(PlayerUrlKey), DefaultPlayerUrl),
        settings.Get<string>(LibraryKey)?.Trim() ?? string.Empty);

    /// <summary>Accepts "host:32400", "http://host:32400/" and a full URL alike.</summary>
    public static string NormalizeUrl(string? value, string fallback)
    {
        string url = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "http://" + url;

        return url.TrimEnd('/');
    }
}
