using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using DJBuddy.Agent;
using DJBuddy.Agent.Tools;
using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Xml;
using DJBuddy.Rekordbox.Query;
using QuikGraph;
using Spectre.Console;

internal static class Program
{
    private static readonly MarkdownRenderer _mdRenderer = new();

    static async Task<int> Main(string[] args)
    {
        ConsoleUi.EnableUtf8();

        // ── Resolve rekordbox.xml path ──────────────────────────────────────────────

        var xmlPath = args.Length > 0
            ? args[0]
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "rekordbox", "rekordbox.xml");

        if (!File.Exists(xmlPath))
        {
            if (args.Length > 0)
            {
                // Explicit CLI arg was wrong — exit immediately.
                ConsoleUi.PrintError($"rekordbox.xml not found: {xmlPath}");
                ConsoleUi.PrintError("Usage: Agent [path-to-rekordbox.xml]");
                return 1;
            }

            // Default path missing — prompt interactively.
            ConsoleUi.PrintStatus($"No library found at default location ({xmlPath}).");
            ConsoleUi.PrintStatus("Enter the path to your rekordbox.xml (or 'exit' to quit):");

            while (true)
            {
                Console.Write("> ");
                var line = Console.ReadLine();

                if (line is null || string.Equals(line.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
                    return 0;

                var candidate = line.Trim().Trim('"');
                if (File.Exists(candidate))
                {
                    xmlPath = candidate;
                    break;
                }

                ConsoleUi.PrintError($"File not found: {candidate}");
            }
        }

        // ── Load library ────────────────────────────────────────────────────────────

        var (library, playlistCount) = await LoadLibraryAsync(xmlPath);
        ConsoleUi.PrintBanner(xmlPath, library.Tracks.Count, playlistCount);

        // ── Build graph in background ───────────────────────────────────────────────
        // Graph construction is CPU-bound and can take a moment on large libraries. Kick it
        // off immediately so it overlaps with the Copilot connection; graph tools await the
        // resulting task before traversing it.
        var graphTask = Task.Run(() => TrackGraphBuilder.Build(library));

        // ── Connect to GitHub Copilot ───────────────────────────────────────────────

        ConsoleUi.PrintStatus("Connecting to GitHub Copilot...");
        var client = new CopilotClient();
        await client.StartAsync();

        // ── Create session ──────────────────────────────────────────────────────────

        var store = new AgentPlaylistStore();
        var session = CreateSession(client, library, graphTask, playlistCount, store);

        // ── REPL ────────────────────────────────────────────────────────────────────

        ConsoleUi.PrintWelcome();

        // Per-request state shared with the event handler registered below.
        // Reset at the start of each SendAndWaitAsync call.
        CancellationTokenSource? loopCts = null;
        bool loopStreamingStarted = false;
        TaskCompletionSource? loopSpinnerCleared = null;
        const string spinnerSuffix = " Thinking...";
        const int spinnerLineLen = 15; // "  X Thinking..." — 2 indent + 1 frame char + 12 suffix chars

        // Register the event handler exactly once per session object.
        // Calling session.On() multiple times adds multiple handlers — registering
        // inside the REPL loop caused one extra handler per message (hence duplication).
        void RegisterSessionHandler(CopilotSession s) => s.On(ev =>
        {
            if (ev is ToolExecutionStartEvent toolStart)
            {
                // Clear the spinner line before printing the tool call;
                // the spinner will redraw on the new current line on its next tick.
                Console.Write($"\r{new string(' ', spinnerLineLen)}\r");
                var toolName = toolStart.Data.ToolName;
                var argStr = FormatToolArgs(toolStart.Data.Arguments);
                AnsiConsole.MarkupLine(argStr.Length > 0
                    ? $"  [dim]⚙ {Markup.Escape(toolName)}({Markup.Escape(argStr)})[/]"
                    : $"  [dim]⚙ {Markup.Escape(toolName)}[/]");
            }
            else if (ev is AssistantMessageDeltaEvent delta)
            {
                if (!loopStreamingStarted)
                {
                    loopStreamingStarted = true;
                    loopCts?.Cancel();
                    loopSpinnerCleared?.Task.Wait(); // blocks until spinner clears its line
                    Console.WriteLine();             // fresh line before response text
                }
                Console.Write(_mdRenderer.Process(delta.Data.DeltaContent));
            }
        });

        RegisterSessionHandler(session);

        while (true)
        {
            ConsoleUi.PrintPrompt();
            var input = Console.ReadLine();

            if (input is null)
                break;

            var trimmed = input.Trim();

            if (trimmed.Length == 0)
                continue;

            // Exit commands
            if (string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "/exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "/quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // Slash commands
            if (trimmed.StartsWith('/'))
            {
                var parts = trimmed.Split(' ', 2);
                var cmd = parts[0].ToLowerInvariant();

                switch (cmd)
                {
                    case "/help":
                        ConsoleUi.PrintHelp();
                        continue;

                    case "/tools":
                        ConsoleUi.PrintTools();
                        continue;

                    case "/clear":
                        AnsiConsole.Clear();
                        continue;

                    case "/stats":
                        ConsoleUi.PrintStats(library);
                        continue;

                    case "/load":
                        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                        {
                            ConsoleUi.PrintError("Usage: /load <path-to-rekordbox.xml>");
                            continue;
                        }

                        var newPath = parts[1].Trim().Trim('"');
                        if (!File.Exists(newPath))
                        {
                            ConsoleUi.PrintError($"File not found: {newPath}");
                            continue;
                        }

                        try
                        {
                            (library, playlistCount) = await LoadLibraryAsync(newPath);
                            xmlPath = newPath;
                            store = new AgentPlaylistStore();
                            graphTask = Task.Run(() => TrackGraphBuilder.Build(library));
                            session = CreateSession(client, library, graphTask, playlistCount, store);
                            RegisterSessionHandler(session); // new session object — register once
                            _mdRenderer.Flush(); // Reset renderer state for new conversation.
                            ConsoleUi.PrintBanner(xmlPath, library.Tracks.Count, playlistCount);
                            ConsoleUi.PrintStatus("Library reloaded. Conversation history has been reset.");
                            Console.WriteLine();
                        }
                        catch (Exception ex)
                        {
                            ConsoleUi.PrintError($"Failed to load library: {ex.Message}");
                        }

                        continue;

                    case "/export":
                    {
                        if (!store.HasAnyTracks)
                        {
                            ConsoleUi.PrintError("No tracks in agent playlists to export. Ask DJ Buddy to build a playlist first.");
                            continue;
                        }

                        var outputPath = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                            ? parts[1].Trim().Trim('"')
                            : xmlPath;

                        try
                        {
                            await HandleExportAsync(store, xmlPath, outputPath);
                        }
                        catch (Exception ex)
                        {
                            ConsoleUi.PrintError($"Export failed: {ex.Message}");
                        }

                        continue;
                    }

                    default:
                        ConsoleUi.PrintError($"Unknown command: {cmd}. Type /help for available commands.");
                        continue;
                }
            }

            // Send to Copilot with a cancellable background spinner
            try
            {
                using var cts = new CancellationTokenSource();
                var spinnerFrames = new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };

                var spinnerCleared = new TaskCompletionSource();
                loopCts = cts;
                loopStreamingStarted = false;
                loopSpinnerCleared = spinnerCleared;

                var spinnerBg = Task.Run(async () =>
                {
                    int i = 0;
                    while (!cts.Token.IsCancellationRequested)
                    {
                        Console.Write($"\r  \x1b[2m\x1b[33m{spinnerFrames[i++ % spinnerFrames.Length]}{spinnerSuffix}\x1b[0m");
                        try { await Task.Delay(80, cts.Token); }
                        catch (OperationCanceledException) { break; }
                    }
                    Console.Write($"\r{new string(' ', spinnerLineLen)}\r");
                    spinnerCleared.SetResult();
                });

                await session.SendAndWaitAsync(new MessageOptions { Prompt = trimmed });

                // Cancel spinner in case no deltas arrived (e.g. empty response).
                cts.Cancel();
                await spinnerBg;
                Console.Write(_mdRenderer.Flush());
            }
            catch (Exception ex)
            {
                ConsoleUi.PrintError($"Error: {ex.Message}");
            }

            Console.WriteLine();
            Console.WriteLine();
        }

        return 0;
    }

    // ── Private methods ─────────────────────────────────────────────────────────────

    private static async Task<(RekordboxLibrary Library, int PlaylistCount)> LoadLibraryAsync(string path)
    {
        ConsoleUi.PrintStatus($"Loading library from {path}...");
        await using var stream = File.OpenRead(path);
        var lib = await RekordboxParser.ParseAsync(stream);
        var plCount = lib.Root.EnumeratePlaylists().Count();
        return (lib, plCount);
    }

    private static CopilotSession CreateSession(
        CopilotClient copilotClient,
        RekordboxLibrary lib,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        int plCount,
        AgentPlaylistStore store)
    {
        var tools = new List<AIFunction>
        {
            AIFunctionFactory.Create(
                (string query, string? genre, string? key, string? minBpm, string? maxBpm, string? sortBy, string? limit) =>
                    LibraryTools.SearchTracks(lib, query, genre, key, minBpm, maxBpm, sortBy, limit),
                "search_tracks",
                "Search tracks by name/artist with optional filters for genre, BPM range, and musical key (Camelot notation like '8A'). Returns up to `limit` results (default 20). Omit optional parameters you don't need — do not pass empty strings or nulls."),

            AIFunctionFactory.Create(
                (string trackId) =>
                    LibraryTools.GetTrackDetails(lib, trackId),
                "get_track_details",
                "Get full metadata for a specific track by its track ID."),

            AIFunctionFactory.Create(
                () => LibraryTools.ListPlaylists(lib),
                "list_playlists",
                "List all playlists in the library with their track counts."),

            AIFunctionFactory.Create(
                (string playlistName) =>
                    LibraryTools.GetPlaylistTracks(lib, playlistName),
                "get_playlist_tracks",
                "Get all tracks in a specific playlist by name."),

            AIFunctionFactory.Create(
                () => LibraryTools.GetLibraryStats(lib),
                "get_library_stats",
                "Get summary statistics about the library: total tracks, artist/key distribution, BPM range."),

            AIFunctionFactory.Create(
                (string name) => PlaylistTools.CreatePlaylist(store, name),
                "create_playlist",
                "Create a new named playlist in the agent's DJ_BUDDY folder. The playlist name must be unique and non-empty."),

            AIFunctionFactory.Create(
                (string playlistName, string trackId) =>
                    PlaylistTools.AddTrackToPlaylist(store, lib, playlistName, trackId),
                "add_track_to_playlist",
                "Add a track to a named agent playlist by its track ID. Use search_tracks or get_track_details first to find a valid track ID."),

            AIFunctionFactory.Create(
                (string playlistName, string trackId) =>
                    PlaylistTools.RemoveTrackFromPlaylist(store, playlistName, trackId),
                "remove_track_from_playlist",
                "Remove a track from a named agent playlist."),

            AIFunctionFactory.Create(
                () => PlaylistTools.ListAgentPlaylists(store, lib),
                "list_agent_playlists",
                "List all agent-created playlists with their track counts and full track details."),

            AIFunctionFactory.Create(
                (string trackId, string? key, string? genre, string? minBpm, string? maxBpm, string? limit) =>
                    GraphTools.SuggestNextTrack(lib, graphTask, trackId, key, genre, minBpm, maxBpm, limit),
                "suggest_next_track",
                "Given a current track ID, return compatible next-track candidates sorted by transition quality (harmonic key + BPM proximity). Each result includes the harmonic relation (Same/Adjacent/EnergyBoost/EnergyDrop), BPM delta percent, and whether it's a half/double-time match. Optional filters: key (Camelot like '8A'), genre, minBpm, maxBpm, limit (default 10)."),

            AIFunctionFactory.Create(
                (string trackId, string? limit) =>
                    GraphTools.FindSimilarTracks(lib, graphTask, trackId, limit),
                "find_similar_tracks",
                "Given a track ID, return tracks that are both harmonically compatible AND/OR frequently co-occur in playlists. Each result includes both compatibility and co-occurrence evidence where available, plus a combined similarity score. Optional limit (default 10)."),
        };

        var sess = copilotClient.CreateSessionAsync(new SessionConfig
        {
            Model = "claude-haiku-4.5",
            OnPermissionRequest = PermissionHandler.ApproveAll,
            Streaming = true,
            Tools = tools,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = SystemPrompt.Create(lib.Tracks.Count, plCount),
            },
        }).GetAwaiter().GetResult();

        return sess;
    }

    /// <summary>
    /// Patches the source rekordbox.xml with the agent's DJ_BUDDY folder and writes
    /// the result to <paramref name="outputPath"/>. Creates a <c>.bak</c> backup first
    /// when overwriting the source file in-place.
    /// </summary>
    private static async Task HandleExportAsync(
        AgentPlaylistStore store, string sourceXmlPath, string outputPath)
    {
        if (string.Equals(sourceXmlPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            var backupPath = sourceXmlPath + ".bak";
            File.Copy(sourceXmlPath, backupPath, overwrite: true);
            ConsoleUi.PrintStatus($"Backup saved to {backupPath}");
        }

        byte[] patchedBytes;
        await using (var inputStream = File.OpenRead(sourceXmlPath))
        {
            patchedBytes = await RekordboxExporter.PatchPlaylistNodeAsync(
                inputStream, store.DjBuddyFolder);
        }

        await File.WriteAllBytesAsync(outputPath, patchedBytes);

        var totalTracks = store.DjBuddyFolder.Children.Sum(c => c.TrackKeys.Count);
        ConsoleUi.PrintStatus(
            $"Exported {store.DjBuddyFolder.Children.Count} playlist(s) ({totalTracks} track(s)) to {outputPath}");
    }

    /// <summary>
    /// Formats tool arguments as a compact "key: value, ..." string for debug display.
    /// Handles Dictionary, IDictionary, and falls back to JSON serialization.
    /// </summary>
    private static string FormatToolArgs(object? arguments)
    {
        if (arguments is null)
            return "";

        if (arguments is IDictionary<string, object?> dict)
        {
            return string.Join(", ", dict
                .Where(kv => kv.Value is not null)
                .Select(kv => $"{kv.Key}: {kv.Value}"));
        }

        if (arguments is System.Collections.IDictionary legacyDict)
        {
            var pairs = new List<string>();
            foreach (System.Collections.DictionaryEntry entry in legacyDict)
            {
                if (entry.Value is not null)
                    pairs.Add($"{entry.Key}: {entry.Value}");
            }

            return string.Join(", ", pairs);
        }

        // Fallback: serialize to JSON and strip the outer braces.
        var json = System.Text.Json.JsonSerializer.Serialize(arguments);
        return json.Trim('{', '}').Trim();
    }
}
