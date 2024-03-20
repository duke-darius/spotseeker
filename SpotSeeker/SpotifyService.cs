using SpotifyAPI.Web;

namespace SpotSeeker;

public class SpotifyService(ISpotifyClient client)
{
    public async Task<FullPlaylist> GetPlaylist(string id, bool includeAll = false)
    {
        Console.WriteLine("Get Playlist");
        var res = await client.Playlists.Get(id);
        if (!includeAll) return res;
        
        while (res.Tracks?.Items?.Count < res.Tracks?.Total)
        {
            var items = await client.Playlists.GetItems(id, new PlaylistGetItemsRequest()
            {
                Limit = res.Tracks.Limit,
                Offset = res.Tracks.Items.Count
            });
            res.Tracks.Items.AddRange(items?.Items ?? Enumerable.Empty<PlaylistTrack<IPlayableItem>>());
        }

        return res;
    }

    
    
    public static async Task<SpotifyService> AuthGeneral(Config config)
    {
        var cfg = SpotifyClientConfig.CreateDefault();
        var req = new ClientCredentialsRequest(config.SpotifyClientId, config.SpotifyClientKey);
        var res = await new OAuthClient(cfg).RequestToken(req);
        return new SpotifyService(new SpotifyClient(res));
    }
}