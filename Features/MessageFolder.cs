using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>A one-tile folder for configuration and connection errors.</summary>
internal sealed class MessageFolder(
    string title,
    string text,
    string? detail = null,
    Func<Task>? onPress = null)
    : FolderProviderBase
{
    public override string Title => title;

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        string message = detail is null ? text : $"{text}\n{detail}";

        return
        [
            new FolderEntry
            {
                SlotIndex = 0,
                Text = message,
                TextSize = 13,
                BackColor = FolderTheme.Error,
                OnPress = onPress
            }
        ];
    }
}
