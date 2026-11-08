using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>Plugin behavior settings that are not part of the server/player connection.</summary>
internal static class PlexampSettings
{
    public const string ReturnToWorkspaceKey = "returnToWorkspace";

    /// <summary>The host command that closes every open folder and returns to the page.</summary>
    public const string CloseFoldersCommand = "System.CloseFolders";

    public static bool ReturnToWorkspace(IPluginSettings settings) =>
        settings.Get(ReturnToWorkspaceKey, true);
}
