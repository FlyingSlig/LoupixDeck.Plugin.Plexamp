namespace LoupixDeck.Plugin.Plexamp.Plex;

internal sealed record PlexLibrarySection(string Key, string Title);

internal sealed record PlexArtist(string RatingKey, string Title, string? Thumb);

internal sealed record PlexAlbum(
    string RatingKey,
    string Title,
    string? Artist,
    int? Year,
    int? LeafCount,
    string? Thumb,
    string? Format,
    string? Subformat);

/// <summary>How the artist page groups releases. "Live", "Soundtrack", "Remix" and the
/// other Plex release types stay in <see cref="Albums"/>.</summary>
internal enum AlbumGroup
{
    Albums,
    Singles,
    Compilations
}

internal static class AlbumGrouping
{
    public static AlbumGroup Classify(PlexAlbum album)
    {
        // Plex exposes the primary release type in <Format> (Album, Single, EP, …) and
        // secondary types in <Subformat> (Compilation, Live, …). A compilation should
        // win over its "Album" format, and a single should win over its "Live" subtype.
        string[] formats = SplitTypes(album.Format);
        string[] subformats = SplitTypes(album.Subformat);

        if (formats.Any(IsCompilation) || subformats.Any(IsCompilation))
            return AlbumGroup.Compilations;

        if (formats.Any(IsSingleOrEp) || subformats.Any(IsSingleOrEp))
            return AlbumGroup.Singles;

        return AlbumGroup.Albums;
    }

    public static string Label(AlbumGroup group) => group switch
    {
        AlbumGroup.Singles => "Singles & EPs",
        AlbumGroup.Compilations => "Compilations",
        _ => "Albums"
    };

    private static bool IsCompilation(string type) =>
        type.Contains("compilation", StringComparison.Ordinal);

    private static bool IsSingleOrEp(string type) =>
        type is "single" or "ep" or "extended play"
        || type.Contains("single", StringComparison.Ordinal);

    private static string[] SplitTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types))
            return [];

        return types
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();
    }
}

internal sealed record PlexServerIdentity(
    string MachineIdentifier,
    string Address,
    string Port,
    string Protocol);

internal sealed record PlexPlayQueue(string Id, string SelectedKey);

internal sealed record PlexClientInfo(
    string Title,
    string? MachineIdentifier,
    string? Product);

internal sealed record PlexPlayerEndpoint(string Name, string Url);

internal sealed class PlexApiException(string message, Exception? innerException = null)
    : Exception(message, innerException);
