namespace LoupixDeck.Plugin.Plexamp.Plex;

/// <summary>Turns an album into playback on the configured Plexamp player.</summary>
internal sealed class AlbumPlaybackService(PlexClient client, PlexampPlayer player)
{
    public async Task PlayAsync(PlexConfig config, PlexAlbum album, CancellationToken cancellationToken)
    {
        PlexServerIdentity identity = await client
            .GetIdentityAsync(config, cancellationToken)
            .ConfigureAwait(false);

        PlexPlayQueue queue = await client
            .CreatePlayQueueAsync(config, identity, album.RatingKey, cancellationToken)
            .ConfigureAwait(false);

        await player
            .PlayMediaAsync(config, identity, queue, cancellationToken)
            .ConfigureAwait(false);
    }
}
