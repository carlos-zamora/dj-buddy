using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using DJBuddy.Agent;
using DJBuddy.Agent.Tools;
using DJBuddy.Rekordbox.Xml;
using DJBuddy.Rekordbox.Query;

// 1. Determine rekordbox.xml path
var xmlPath = args.Length > 0
    ? args[0]
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "rekordbox", "rekordbox.xml");

if (!File.Exists(xmlPath))
{
    Console.Error.WriteLine($"rekordbox.xml not found: {xmlPath}");
    Console.Error.WriteLine("Usage: Agent [path-to-rekordbox.xml]");
    return 1;
}

// 2. Parse library
Console.WriteLine($"Loading library from {xmlPath}...");
await using var stream = File.OpenRead(xmlPath);
var library = await RekordboxParser.ParseAsync(stream);
Console.WriteLine($"Loaded {library.Tracks.Count} tracks, " +
    $"{library.Root.EnumeratePlaylists().Count()} playlists.");

// 3. Initialize Copilot
Console.WriteLine("Connecting to GitHub Copilot...");
var client = new CopilotClient();
await client.StartAsync();

// 4. Create tools
var tools = new List<AIFunction>
{
    AIFunctionFactory.Create(
        (string query, string? genre, string? key, double? minBpm, double? maxBpm, string? sortBy, int? limit) =>
            LibraryTools.SearchTracks(library, query, genre, key, minBpm, maxBpm, sortBy, limit),
        "search_tracks",
        "Search tracks by name/artist with optional filters for genre, BPM range, and musical key (Camelot notation like '8A'). Returns up to `limit` results (default 20)."),

    AIFunctionFactory.Create(
        (string trackId) =>
            LibraryTools.GetTrackDetails(library, trackId),
        "get_track_details",
        "Get full metadata for a specific track by its track ID."),

    AIFunctionFactory.Create(
        () => LibraryTools.ListPlaylists(library),
        "list_playlists",
        "List all playlists in the library with their track counts."),

    AIFunctionFactory.Create(
        (string playlistName) =>
            LibraryTools.GetPlaylistTracks(library, playlistName),
        "get_playlist_tracks",
        "Get all tracks in a specific playlist by name."),

    AIFunctionFactory.Create(
        () => LibraryTools.GetLibraryStats(library),
        "get_library_stats",
        "Get summary statistics about the library: total tracks, genre/artist/key distribution, BPM range."),
};

// 5. Create session
var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-4.1",
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Streaming = true,
    Tools = tools,
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Replace,
        Content = SystemPrompt.Text,
    },
});

// 6. Register streaming handler
session.On(ev =>
{
    if (ev is AssistantMessageDeltaEvent delta)
    {
        Console.Write(delta.Data.DeltaContent);
    }
});

// 7. REPL
Console.WriteLine();
Console.WriteLine("DJ Buddy ready. Type your question (or 'exit' to quit):");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (input is null || string.Equals(input.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
        break;

    if (string.IsNullOrWhiteSpace(input))
        continue;

    await session.SendAndWaitAsync(new MessageOptions { Prompt = input });
    Console.WriteLine();
    Console.WriteLine();
}

return 0;
