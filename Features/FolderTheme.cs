using LoupixDeck.Plugin.Plexamp.Plex;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

internal static class FolderTheme
{
    public static readonly PluginColor Tile = PluginColor.FromRgb(0x26, 0x29, 0x2F);
    public static readonly PluginColor Artist = PluginColor.FromRgb(0x3A, 0x30, 0x16);
    public static readonly PluginColor Album = PluginColor.FromRgb(0x2B, 0x3A, 0x55);
    public static readonly PluginColor Single = PluginColor.FromRgb(0x42, 0x2F, 0x52);
    public static readonly PluginColor Compilation = PluginColor.FromRgb(0x55, 0x38, 0x2B);
    public static readonly PluginColor Error = PluginColor.FromRgb(0x64, 0x22, 0x22);
    public static readonly PluginColor Muted = PluginColor.FromRgb(0x9A, 0x9E, 0xA5);

    public static PluginColor ForGroup(AlbumGroup group) => group switch
    {
        AlbumGroup.Singles => Single,
        AlbumGroup.Compilations => Compilation,
        _ => Album
    };
}
