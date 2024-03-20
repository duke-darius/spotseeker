using SpotifyAPI.Web;

namespace SpotSeeker;

public static class Utils
{
    public static string ToSpotifyId(this string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.PathAndQuery.Split("?").First().Split("/").Last();
        }
        catch
        {
            return url;
        }
    }

    public static string GetFileExtension(this Soulseek.File file)
    {
        return new FileInfo(file.Filename).Extension;
    }

    public static string RemoveIllegalChars(this string str)
    {
        return Path.GetInvalidFileNameChars().Aggregate(str, (current, c) => current.Replace(c, '-'));
    }

    
    public static string ToAbsolutePath(this FullTrack track, string outputDir, Soulseek.File file)
    {
        return Path.Combine(outputDir,
            $"{track.Name.RemoveIllegalChars()} - {track.Artists.First().Name.RemoveIllegalChars()}{file.GetFileExtension()}");
    }
    
    public static string ToPathName(this FullTrack track)
    {
        return $"{track.Name.RemoveIllegalChars()} - {track.Artists.First().Name.RemoveIllegalChars()}";
    }
    
    public static string ArtistsToString(this IEnumerable<SimpleArtist> artists)
    {
        return string.Join(", ", artists.Select(x => x.Name));
    }
}