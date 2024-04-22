// See https://aka.ms/new-console-template for more information


using System.Text;
using Soulseek;
using Spectre.Console;
using SpotifyAPI.Web;
using SpotSeeker;
using Tomlyn;
using Directory = System.IO.Directory;
using File = System.IO.File;
using SearchResponse = Soulseek.SearchResponse;

var invalidFileNameChars = Path.GetInvalidFileNameChars();


Console.OutputEncoding = Encoding.UTF8;

SpotifyService spotifyService = null!;
SoulseekClient soulSeekClient = null!;

const bool debugLogs = false;
Config config = null!;
if (File.Exists("config.toml"))
    config = Toml.ToModel<Config>(File.ReadAllText("config.toml"));
else
{
    AnsiConsole.WriteLine("`config.toml` not found, I have created it, please fill it in (it's next to the exe)");
    File.WriteAllText("config.toml", Toml.FromModel(new Config()));
    Environment.Exit(0);
}

AnsiConsole.MarkupLine("[underline red]Welcome to SpotSeek[/]");
await Task.Delay(500);

await AnsiConsole.Status()
    .StartAsync("Warming up...", async ctx =>
    {
        ctx.Status("Connecting to [green]Spotify[/]...");
        spotifyService = await SpotifyService.AuthGeneral(config);
        AnsiConsole.MarkupLine(("✅ Connected successfully to [green]Spotify[/]!"));

        ctx.Status("Connecting to the SoulSeek network...");
        soulSeekClient = new SoulseekClient();
        
        await soulSeekClient.ConnectAsync(config.SoulSeekUsername, config.SoulSeekPassword);
        AnsiConsole.MarkupLine("✅ Connected to SoulSeek network!");

        

        AnsiConsole.MarkupLine("✅✅✅ Warmed up ✅✅✅");
    });
    
var playlistUrl = AnsiConsole.Prompt(new TextPrompt<string>("Please enter the Spotify playlist URL (Or just ID) >"));

var playlistId = playlistUrl.ToSpotifyId();

FullPlaylist playlist = null!;
await AnsiConsole.Status()
    .StartAsync("Pulling Spotify data...", async ctx =>
    {

        playlist = await spotifyService.GetPlaylist(playlistId, true);
    });
if (playlist == null)
    throw new Exception("Failed to retrieve playlist info...");
AnsiConsole.MarkupLine("✅ Retrieved Spotify playlist data!");

var tracks = playlist!.Tracks!.Items!.Select(x=> x.Track).Cast<FullTrack>()!;

var infoTable = new Table
{
    Title = new TableTitle("Playlist Info")
};
infoTable.AddColumn("Key");
infoTable.AddColumn("Prop");

infoTable.AddRow("Name", playlist.Name ?? string.Empty);
infoTable.AddRow("Description", playlist.Description ?? string.Empty);
infoTable.AddRow("Author", playlist.Owner?.DisplayName ?? "");
infoTable.AddRow("Track Count", playlist.Tracks?.Items?.Count.ToString() ?? "");

AnsiConsole.Write(infoTable);
AnsiConsole.WriteLine();

var trackTable = new Table
{
    Title = new TableTitle("Playlist Tracks")
};

trackTable.AddColumn("Title");
trackTable.AddColumn("Length");
trackTable.AddColumn("Artist");
trackTable.AddColumn("Album");

foreach (var t in tracks)
{
    if(t is null)
        continue;
    
    var duration = TimeSpan.FromMilliseconds(t.DurationMs);
    try
    {
        trackTable.AddRow(new Text(t.Name), new Text($"{duration.Minutes}:{duration.Seconds:00}"), new Text(t.Artists.ArtistsToString()),
            new Text(t.Album.Name));
    }
    catch
    {
        Console.WriteLine(t.Name);
        throw;
    }
}

AnsiConsole.Write(trackTable);

var consent = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Is this the right playlist?").AddChoices(["Yes", "No - exit"]));
if(consent != "Yes")
    Environment.Exit(0);

AnsiConsole.WriteLine($"The output folder will be named `{playlist.Name!.RemoveIllegalChars().Trim()}`");
var usePlaylistName = AnsiConsole.Prompt(new SelectionPrompt<string>()
    .Title("Is this folder name ok with you (consider the characters on your OS)")
    .AddChoices(["Yes", "No - type my own name"])) == "Yes";

var outputFolder = Path.Combine(new DirectoryInfo(Directory.GetCurrentDirectory()).FullName, "completed", (usePlaylistName ? playlist.Name!.RemoveIllegalChars().Trim() : AnsiConsole.Prompt(new TextPrompt<string>("Type the preferred path here:")).Trim()));

var autoDownload = AnsiConsole.Prompt(new SelectionPrompt<string>()
    .Title(
        "Would you like SpotSeeker to automatically select the highest quality file available? or would you like to pick each one?")
    .AddChoices(["Auto-download", "Manual"])) == "Auto-download";




AnsiConsole.MarkupLine("Starting search...");

List<Task> downloadTasks = [];
Directory.CreateDirectory(outputFolder);
foreach (var track in tracks)
{
    if(track is null)
        continue;
    try
    {
        await Task.Delay(1000);
        Search search = null!;
        IReadOnlyCollection<SearchResponse> responses = null!;
        if (Directory.EnumerateFiles(outputFolder, $"{track.ToPathName()}.*").Any(x => new FileInfo(x).Length != 0))
        {
            AnsiConsole.WriteLine($"Skipping: {track.ToPathName()} as it exists");
            continue;
        }

        await AnsiConsole.Status().StartAsync("Starting...",
            async context =>
            {
                context.Status(
                    $"Searching for {track.Name.Replace("[", "[[").Replace("]", "]]")} by " +
                    $"{track.Artists.ArtistsToString().Replace("[", "[[").Replace("]", "]]")}");
                var res = await soulSeekClient.SearchAsync(
                    new SearchQuery([track.Name]), 
                    options: new SearchOptions( responseFilter: response => 
                        response.Files.Any(x=> 
                            x.Filename.Contains(track.Artists.First().Name, StringComparison.CurrentCultureIgnoreCase))));
                search = res.Search;
                responses = res.Responses;
            });
        if (autoDownload)
        {
            var file = AutoSelectFromResponses(responses, track);
            if (!file.HasValue)
            {
                AnsiConsole.WriteLine($"Sorry, I couldn't find {track.Name} by {track.Artists.ArtistsToString()}");
                continue;
            }

            downloadTasks.Add(DownloadFile(file!.Value.username, file.Value.file, track, outputFolder));
        }


    }
    catch (Exception ex)
    {
        AnsiConsole.WriteLine($"Failed to download {track.ToPathName()}: {ex.Message}");
    }
}

await Task.WhenAll(downloadTasks);

return;

async Task DownloadFile(string username, Soulseek.File file, FullTrack track, string outputDir)
{
    try
    {
        AnsiConsole.WriteLine(
            $"({downloadTasks.Count(c => !c.IsCompleted)}/{downloadTasks.Count})Started download of {track.ToPathName()}");
        var path = Path.Combine(outputDir,
            $"{track.Name.RemoveIllegalChars()} - {track.Artists.First().Name.RemoveIllegalChars()}{file.GetFileExtension()}");
        await soulSeekClient.DownloadAsync(username, file.Filename, path);
        AnsiConsole.WriteLine(
            $"({downloadTasks.Count(c => !c.IsCompleted)}/{downloadTasks.Count})Downloaded {track.ToPathName()} successfully!");
    }
    catch(Exception ex)
    {
        AnsiConsole.WriteLine(
            $"({downloadTasks.Count(c => !c.IsCompleted)}/{downloadTasks.Count})Failed to download: {track.ToPathName()}: {ex.Message}");
    }
}

(string username, Soulseek.File file)? AutoSelectFromResponses(IEnumerable<SearchResponse> responses, FullTrack track)
{
    var files = new Dictionary<Soulseek.File, SearchResponse>();
    foreach (var res in responses)
    {
        foreach (var file in res.Files)
        {
            files.Add(file, res);
        }
    }
    
    if(debugLogs)
        AnsiConsole.WriteLine($"Found {files.Count} files total");

    var flacFiles = files.Where(x => x.Value.HasFreeUploadSlot && x.Key.GetFileExtension().Equals(".flac", StringComparison.CurrentCultureIgnoreCase));
    if(debugLogs)
        AnsiConsole.WriteLine($"{flacFiles.Count()} .flac files found");
    if (flacFiles.Any())
    {
        var r = flacFiles.MaxBy(x => x.Key.Size);
        return (r.Value.Username, r.Key);
    }
    
    var wavFiles = files.Where(x => x.Value.HasFreeUploadSlot && x.Key.GetFileExtension().Equals(".wav", StringComparison.CurrentCultureIgnoreCase));
    if(debugLogs)
        AnsiConsole.WriteLine($"{wavFiles.Count()} .wav files found");
    if (wavFiles.Any())
    {
        var r = wavFiles.MaxBy(x => x.Key.Size);
        return (r.Value.Username, r.Key);
    }
    
    var mp3Files = files.Where(x => x.Value.HasFreeUploadSlot && x.Key.GetFileExtension().Equals(".mp3", StringComparison.CurrentCultureIgnoreCase));
    if(debugLogs)
        AnsiConsole.WriteLine($"{mp3Files.Count()} .mp3 files found");
    if (mp3Files.Any())
    {
        var r = mp3Files.MaxBy(x => x.Key.BitRate);
        return (r.Value.Username, r.Key);
    }

    return null;
}





