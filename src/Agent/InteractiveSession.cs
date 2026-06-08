using DJBuddy.Agent.Tools;
using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using NAudio.Wave;
using QuikGraph;
using Spectre.Console;

namespace DJBuddy.Agent;

/// <summary>
/// Offline interactive workspace builder and graph-walk picker — no LLM calls, zero tokens.
/// Entered via the <c>/interactive</c> REPL command.
/// </summary>
internal static class InteractiveSession
{
    private const string DefaultScratchName = "session";
    private const int NeighborLimit = 10;
    private const int DefaultShowLimit = 20;

    /// <summary>Enters the interactive sub-REPL.</summary>
    /// <param name="store">Session workspace store; mutations are persisted here.</param>
    /// <param name="graphTask">Shared compatibility graph; awaited on first <c>order</c> or <c>next</c> call.</param>
    /// <param name="workspaceName">
    /// Optional workspace to operate on. Omit to use a scratch workspace named <c>session</c>.
    /// The workspace is created empty if it doesn't exist yet.
    /// </param>
    public static async Task RunAsync(
        WorkspaceStore store,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        string? workspaceName)
    {
        var resolvedName = string.IsNullOrWhiteSpace(workspaceName)
            ? DefaultScratchName
            : workspaceName.Trim();

        var ws = store.GetOrCreate(resolvedName);
        string? seedId = null;

        // Pre-load the full library into any empty workspace so the user can filter down immediately.
        if (ws.Count == 0)
            ws.Apply(TrackSetMode.Union, store.Library.Tracks.Keys);

        // Undo history: parallel stacks of (workspace IDs snapshot, seed snapshot)
        var undoIds = new Stack<string[]>();
        var undoSeed = new Stack<string?>();

        using var player = new MediaPlayer();

        // Tracks from the most recent display command; lets "play 5" reference row 5.
        List<Track> lastShown = [];

        MediaKeyHook? mediaKeyHook = null;
        if (OperatingSystem.IsWindows())
        {
            mediaKeyHook = new MediaKeyHook(
                onPlayPause: () => player.Pause(),
                onStop:      () => player.Stop(),
                onNext:      () => NavigateTrack(player, ws, +1),
                onPrev:      () => NavigateTrack(player, ws, -1));
        }

        PrintOpeningScreen(ws);

        try
        {
        while (true)
        {
            Console.Write(BuildPrompt(ws, seedId, store.Library, player));
            var raw = Console.ReadLine();
            if (raw is null) return;

            var line = raw.Trim();
            if (line.Length == 0) continue;

            var tokens = ParseTokens(line);
            if (tokens.Length == 0) continue;

            var verb = tokens[0].ToLowerInvariant();
            var sub  = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : string.Empty;

            try
            {
                switch (verb)
                {
                    case "help":
                    case "?":
                        PrintHelp();
                        break;

                    // ── Workspace ────────────────────────────────────────────────────────────

                    case "add" when sub == "playlist":
                    {
                        var name = JoinFrom(tokens, 2);
                        if (!RequireArg(name, "Usage: add playlist <name>  e.g. add playlist \"Dubstep Favorites\"")) break;
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = WorkspaceTools.WorkspaceFromLibraryPlaylist(store, ws.Name, "union", name);
                        PrintResult(result, ws);
                        break;
                    }

                    case "add":
                    {
                        var query = JoinFrom(tokens, 1);
                        if (!RequireArg(query, "Usage: add <query>  e.g. add zeds dead")) break;
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = ApplySearch(store, ws.Name, "union", query);
                        PrintResult(result, ws);
                        break;
                    }

                    case "filter" when sub == "key":
                    {
                        var camelot = JoinFrom(tokens, 2);
                        if (!RequireArg(camelot, "Usage: filter key <camelot>  e.g. filter key 8A")) break;
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = WorkspaceTools.WorkspaceFromSearch(
                            store, ws.Name, "intersect", query: "", playlistFilter: null,
                            key: camelot, minBpm: null, maxBpm: null, limit: "5000");
                        PrintResult(result, ws);
                        break;
                    }

                    case "filter" when sub == "bpm":
                    {
                        if (tokens.Length < 4)
                        {
                            ConsoleUi.PrintError("Usage: filter bpm <min> <max>  e.g. filter bpm 140 160");
                            break;
                        }
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = WorkspaceTools.WorkspaceFromSearch(
                            store, ws.Name, "intersect", query: "", playlistFilter: null,
                            key: null, minBpm: tokens[2], maxBpm: tokens[3], limit: "5000");
                        PrintResult(result, ws);
                        break;
                    }

                    case "filter" when sub == "playlist":
                    {
                        var name = JoinFrom(tokens, 2);
                        if (!RequireArg(name, "Usage: filter playlist <name>")) break;
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = WorkspaceTools.WorkspaceFromSearch(
                            store, ws.Name, "intersect", query: "", playlistFilter: name,
                            key: null, minBpm: null, maxBpm: null, limit: "5000");
                        PrintResult(result, ws);
                        break;
                    }

                    case "filter":
                    {
                        var query = JoinFrom(tokens, 1);
                        if (!RequireArg(query, "Usage: filter <query>  (keeps only tracks matching the query)")) break;
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = ApplySearch(store, ws.Name, "intersect", query);
                        PrintResult(result, ws);
                        break;
                    }

                    case "remove":
                    {
                        var query = JoinFrom(tokens, 1);
                        if (!RequireArg(query, "Usage: remove <query>")) break;
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = ApplySearch(store, ws.Name, "except", query);
                        PrintResult(result, ws);
                        break;
                    }

                    case "clear":
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        ws.Apply(TrackSetMode.Replace, []);
                        seedId = null;
                        AnsiConsole.MarkupLine("  [dim]Workspace cleared.[/]");
                        break;

                    case "undo":
                        if (undoIds.Count == 0)
                        {
                            AnsiConsole.MarkupLine("  [dim]Nothing to undo.[/]");
                            break;
                        }
                        ws.Apply(TrackSetMode.Replace, undoIds.Pop());
                        seedId = undoSeed.Pop();
                        AnsiConsole.MarkupLine($"  [dim]Undone. {ws.Count} tracks.[/]");
                        break;

                    // ── Preview ──────────────────────────────────────────────────────────────

                    case "search" when HasInPlaylist(tokens, out var sq, out var sp):
                        lastShown = ShowPlaylistSearch(store, sp!, sq!, player.CurrentTrack);
                        break;

                    case "search":
                    case "find":
                    {
                        var query = JoinFrom(tokens, 1);
                        if (!RequireArg(query, $"Usage: {verb} <query>")) break;
                        lastShown = PreviewSearch(store, query, player.CurrentTrack);
                        break;
                    }

                    case "show" when sub == "playlist":
                    {
                        var name = JoinFrom(tokens, 2);
                        if (!RequireArg(name, "Usage: show playlist <name>")) break;
                        lastShown = ShowPlaylist(store, name, player.CurrentTrack);
                        break;
                    }

                    case "show":
                    {
                        var n = tokens.Length > 1 && int.TryParse(tokens[1], out var p) && p > 0
                            ? p : DefaultShowLimit;
                        var tracks = ws.Tracks.Take(n).ToList();
                        ConsoleUi.RenderTrackTable(
                            tracks,
                            ["artist", "name", "bpm", "key"],
                            $"{ws.Name} — {tracks.Count} of {ws.Count} tracks",
                            player.CurrentTrack?.TrackId,
                            player.CurrentTrack?.Key);
                        lastShown = tracks;
                        break;
                    }

                    case "count":
                        AnsiConsole.MarkupLine(
                            $"  [dim]{ws.Count} tracks{(ws.Ordered ? ", ordered" : "")}.[/]");
                        break;

                    case "stats":
                        PrintStats(ws);
                        break;

                    case "playlists":
                    {
                        var filter = JoinFrom(tokens, 1);
                        ListPlaylists(store, filter);
                        break;
                    }

                    // ── Playback ─────────────────────────────────────────────────────────────

                    case "play":
                    {
                        var arg = JoinFrom(tokens, 1);
                        if (string.IsNullOrWhiteSpace(arg))
                        {
                            if (player.CurrentTrack is null)
                            {
                                ConsoleUi.PrintError("Nothing loaded. Usage: play <query or id>");
                                break;
                            }
                            player.Pause();
                        }
                        else
                        {
                            Track? resolved = null;
                            if (int.TryParse(arg, out var idx) && idx >= 1 && idx <= lastShown.Count)
                                resolved = lastShown[idx - 1];
                            else if (store.Library.Tracks.TryGetValue(arg, out var byId))
                                resolved = byId;
                            else
                                resolved = store.Library.Tracks.Values
                                    .Search(arg)
                                    .FirstOrDefault();

                            if (resolved is null)
                            {
                                ConsoleUi.PrintError($"No track found matching '{Markup.Escape(arg)}'.");
                                break;
                            }
                            if (!TryLoadAndPlay(player, resolved, out var loadError))
                            {
                                ConsoleUi.PrintError(Markup.Escape(loadError!));
                                break;
                            }
                        }
                        PrintPlaybackStatus(player);
                        break;
                    }

                    case "pause":
                    {
                        if (player.CurrentTrack is null)
                        {
                            ConsoleUi.PrintError("Nothing is playing.");
                            break;
                        }
                        player.Pause();
                        PrintPlaybackStatus(player);
                        break;
                    }

                    case "ff":
                    {
                        if (player.CurrentTrack is null) { ConsoleUi.PrintError("Nothing is playing."); break; }
                        player.SkipSeconds(5);
                        PrintPlaybackStatus(player);
                        break;
                    }

                    case "rw":
                    {
                        if (player.CurrentTrack is null) { ConsoleUi.PrintError("Nothing is playing."); break; }
                        player.SkipSeconds(-5);
                        PrintPlaybackStatus(player);
                        break;
                    }

                    case "stop":
                    {
                        if (player.CurrentTrack is null) { ConsoleUi.PrintError("Nothing is playing."); break; }
                        player.Stop();
                        AnsiConsole.MarkupLine("  [dim]Stopped.[/]");
                        break;
                    }

                    // ── Ordering ─────────────────────────────────────────────────────────────

                    case "seed":
                    {
                        var arg = JoinFrom(tokens, 1);
                        if (!RequireArg(arg, "Usage: seed <trackId or query>  e.g. seed \"One Who Lost\"")) break;
                        var resolved = ResolveSeedArg(store, ws, arg);
                        if (resolved is null) break;
                        seedId = resolved;
                        var t = store.Library.Tracks.GetValueOrDefault(seedId);
                        var label = t is not null
                            ? $"{Markup.Escape(t.Artist)} — {Markup.Escape(t.Name)}"
                            : Markup.Escape(seedId);
                        AnsiConsole.MarkupLine($"  [dim]Seed: {label}[/]");
                        break;
                    }

                    case "order":
                    {
                        if (ws.Count == 0) { ConsoleUi.PrintError("Workspace is empty."); break; }
                        var target = tokens.Length > 1 && int.TryParse(tokens[1], out var t) && t > 0
                            ? (string?)t.ToString() : null;
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = await WorkspaceTools.OrderWorkspace(store, graphTask, ws.Name, seedId, target);
                        PrintResult(result, ws);
                        ConsoleUi.RenderTrackTable(
                            ws.Tracks.ToList(), ["artist", "name", "bpm", "key"],
                            $"{ws.Name} (ordered)",
                            player.CurrentTrack?.TrackId,
                            player.CurrentTrack?.Key);
                        break;
                    }

                    case "sort" when sub == "by":
                    {
                        var criterion = tokens.Length > 2 ? tokens[2].ToLowerInvariant() : string.Empty;
                        if (!RequireArg(criterion, "Usage: sort by rating|recent|playCount")) break;
                        if (ws.Count == 0) { ConsoleUi.PrintError("Workspace is empty."); break; }
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        SortWorkspace(ws, criterion);
                        AnsiConsole.MarkupLine($"  [dim]Sorted by {criterion}. {ws.Count} tracks, ordered.[/]");
                        break;
                    }

                    case "take":
                    {
                        if (tokens.Length < 2)
                        {
                            ConsoleUi.PrintError("Usage: take <n> [by rating|recent|playCount|random]");
                            break;
                        }
                        var byIdx = Array.FindIndex(tokens, 2, t =>
                            t.Equals("by", StringComparison.OrdinalIgnoreCase));
                        var by = byIdx >= 0 && byIdx + 1 < tokens.Length ? tokens[byIdx + 1] : null;
                        PushUndo(undoIds, undoSeed, ws, seedId);
                        var result = WorkspaceTools.TrimWorkspace(store, ws.Name, tokens[1], by);
                        PrintResult(result, ws);
                        break;
                    }

                    case "next":
                        if (ws.Count == 0) { ConsoleUi.PrintError("Workspace is empty."); break; }
                        await RunPickPhaseAsync(store, graphTask, ws, seedId);
                        break;

                    // ── Session ──────────────────────────────────────────────────────────────

                    case "save":
                    {
                        if (ws.Count == 0) { ConsoleUi.PrintError("Workspace is empty. Nothing to save."); break; }
                        var name = JoinFrom(tokens, 1);
                        if (string.IsNullOrWhiteSpace(name)) name = ws.Name;
                        var (ok, message, count) = store.Commit(ws.Name, name);
                        if (ok)
                            ConsoleUi.PrintStatus(
                                $"Saved '{message}' ({count} tracks) to DJ_BUDDY. Use /export to write rekordbox.xml.");
                        else
                            ConsoleUi.PrintError(message);
                        return;
                    }

                    case "exit":
                        AnsiConsole.MarkupLine("  [dim]Exited without saving.[/]");
                        return;

                    default:
                        // Bare query — preview without mutating the workspace
                        PreviewSearch(store, line);
                        break;
                }
            }
            catch (Exception ex)
            {
                ConsoleUi.PrintError($"Error: {ex.Message}");
            }
        }
        } // while (true)
        finally
        {
            if (OperatingSystem.IsWindows())
                mediaKeyHook?.Dispose();
        }
    }

    /// <summary>
    /// Tokenizes a command line, respecting double-quoted strings as single tokens.
    /// </summary>
    /// <param name="line">Raw input line (already trimmed).</param>
    /// <returns>Array of tokens; quoted strings have their quotes stripped.</returns>
    internal static string[] ParseTokens(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
            }
            else if (ch == ' ' && !inQuote)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return [.. tokens];
    }

    // ── Graph walk ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks <paramref name="ws"/> one neighbor at a time using the compatibility graph.
    /// Entered via the <c>next</c> command; returns to the workspace REPL when done.
    /// </summary>
    private static async Task RunPickPhaseAsync(
        WorkspaceStore store,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        TrackSet ws,
        string? seedId)
    {
        var graph = await graphTask.ConfigureAwait(false);
        var workspaceTracks = ws.Tracks.ToList();

        var currentTrack = ResolveSeed(workspaceTracks, seedId);
        if (currentTrack is null)
        {
            ConsoleUi.PrintError("Could not determine a seed track.");
            return;
        }

        var picked = new List<Track> { currentTrack };
        var pickedIds = new HashSet<string> { currentTrack.TrackId };
        var committed = false;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]Graph walk — '{Markup.Escape(ws.Name)}'[/]");
        PrintPickHelp();
        AnsiConsole.WriteLine();
        PrintCurrent(currentTrack, step: 1);

        var done = false;
        while (!done)
        {
            var allNeighbors = NeighborsInWorkspace(graph, currentTrack, ws, pickedIds);
            if (allNeighbors.Count == 0)
            {
                AnsiConsole.MarkupLine("  [yellow]No more compatible neighbors in this workspace.[/]");
                break;
            }

            var shownCount = Math.Min(NeighborLimit, allNeighbors.Count);
            PrintNeighbors(allNeighbors, 0, shownCount);

            var advance = false;
            while (!advance && !done)
            {
                Console.Write("  next> ");
                var input = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (input is null) { done = true; break; }

                switch (input)
                {
                    case "quit" or "q":
                        AnsiConsole.MarkupLine("  [dim]Graph walk ended. Back to workspace.[/]");
                        done = true;
                        break;

                    case "help" or "?":
                        PrintPickHelp();
                        break;

                    case "commit" or "c":
                        CommitPicked(store, ws, picked);
                        committed = true;
                        done = true;
                        break;

                    case "more" or "m":
                        if (shownCount >= allNeighbors.Count)
                            AnsiConsole.MarkupLine($"  [dim]All {allNeighbors.Count} compatible neighbors shown.[/]");
                        else
                        {
                            var prev = shownCount;
                            shownCount = Math.Min(shownCount + NeighborLimit, allNeighbors.Count);
                            PrintNeighbors(allNeighbors, prev, shownCount);
                        }
                        break;

                    case "skip" or "s":
                        // Refresh neighbor list from the same track (order may differ after re-sort)
                        advance = true;
                        break;

                    default:
                        if (int.TryParse(input, out var choice) && choice >= 1 && choice <= shownCount)
                        {
                            var nextEdge = allNeighbors[choice - 1];
                            currentTrack = nextEdge.Target;
                            picked.Add(currentTrack);
                            pickedIds.Add(currentTrack.TrackId);
                            AnsiConsole.WriteLine();
                            PrintCurrent(currentTrack, step: picked.Count);
                            advance = true;
                        }
                        else
                        {
                            AnsiConsole.MarkupLine(
                                $"  [red]Enter 1–{shownCount}, more, skip, commit, or quit.[/]");
                        }
                        break;
                }
            }
        }

        if (!committed && picked.Count > 1)
            OfferCommit(store, ws, picked);
    }

    // ── Playback helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a rekordbox <c>file://</c> URI to a local file path, handling the Windows
    /// loopback case where <see cref="Uri.LocalPath"/> returns a UNC path instead.
    /// </summary>
    private static string UriToLocalPath(string location)
    {
        var uri = new Uri(location);
        return uri.IsLoopback
            ? Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/')
            : uri.LocalPath;
    }

    /// <summary>
    /// Loads and plays <paramref name="track"/> in <paramref name="player"/>.
    /// Returns <c>false</c> (and sets <paramref name="error"/>) if the track has no location
    /// or the file cannot be opened.
    /// </summary>
    private static bool TryLoadAndPlay(MediaPlayer player, Track track, out string? error)
    {
        if (string.IsNullOrEmpty(track.Location))
        {
            error = $"Track '{track.Name}' has no file path.";
            return false;
        }
        try
        {
            player.Load(UriToLocalPath(track.Location), track);
            player.Play();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Advances to the next (<paramref name="delta"/> = +1) or previous (−1) track in the
    /// workspace relative to the currently playing track. Called from the media-key hook thread.
    /// </summary>
    private static void NavigateTrack(MediaPlayer player, TrackSet ws, int delta)
    {
        var tracks = ws.Tracks.ToList();
        if (tracks.Count == 0) return;

        var idx = player.CurrentTrack is not null
            ? tracks.FindIndex(t => t.TrackId == player.CurrentTrack.TrackId)
            : -1;

        var next = tracks[Math.Clamp(idx + delta, 0, tracks.Count - 1)];

        // Prev on the first track (or no current) restarts the current track instead.
        if (next.TrackId == player.CurrentTrack?.TrackId)
        {
            player.Seek(TimeSpan.Zero);
            if (!player.IsPlaying) player.Play();
            return;
        }

        TryLoadAndPlay(player, next, out _);
    }

    // ── Command helpers ──────────────────────────────────────────────────────────────────────

    private static object ApplySearch(WorkspaceStore store, string wsName, string mode, string query)
    {
        return WorkspaceTools.WorkspaceFromSearch(
            store, wsName, mode, query,
            playlistFilter: null, key: null, minBpm: null, maxBpm: null, limit: "5000");
    }

    private static List<Track> PreviewSearch(WorkspaceStore store, string query, Track? nowPlaying = null)
    {
        var tracks = store.Library.Tracks.Values
            .Search(query, TrackSearchFields.All)
            .Take(50).ToList();
        ConsoleUi.RenderTrackTable(tracks, ["artist", "name", "bpm", "key"],
            $"Search: {Markup.Escape(query)} ({tracks.Count} results)",
            nowPlaying?.TrackId, nowPlaying?.Key);
        return tracks;
    }

    private static List<Track> ShowPlaylist(WorkspaceStore store, string name, Track? nowPlaying = null)
    {
        var node = FindPlaylist(store, name);
        if (node is null)
        {
            ConsoleUi.PrintError($"Playlist '{name}' not found. Try: playlists {name}");
            return [];
        }
        var tracks = node.TrackKeys
            .Select(id => store.Library.Tracks.TryGetValue(id, out var t) ? t : null)
            .OfType<Track>()
            .Take(200).ToList();
        ConsoleUi.RenderTrackTable(tracks, ["artist", "name", "bpm", "key"],
            $"{Markup.Escape(node.Name)} ({tracks.Count} tracks)",
            nowPlaying?.TrackId, nowPlaying?.Key);
        return tracks;
    }

    private static List<Track> ShowPlaylistSearch(WorkspaceStore store, string playlistName, string query, Track? nowPlaying = null)
    {
        var node = FindPlaylist(store, playlistName);
        if (node is null)
        {
            ConsoleUi.PrintError($"Playlist '{playlistName}' not found. Try: playlists {playlistName}");
            return [];
        }
        var tracks = node.TrackKeys
            .Select(id => store.Library.Tracks.TryGetValue(id, out var t) ? t : null)
            .OfType<Track>()
            .Search(query, TrackSearchFields.All)
            .Take(200).ToList();
        ConsoleUi.RenderTrackTable(tracks, ["artist", "name", "bpm", "key"],
            $"{Markup.Escape(node.Name)} / {Markup.Escape(query)} ({tracks.Count} results)",
            nowPlaying?.TrackId, nowPlaying?.Key);
        return tracks;
    }

    private static void ListPlaylists(WorkspaceStore store, string filter)
    {
        var list = store.Library.Root.EnumeratePlaylists()
            .Where(p => string.IsNullOrEmpty(filter)
                || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (list.Count == 0)
        {
            ConsoleUi.PrintError(string.IsNullOrEmpty(filter)
                ? "No playlists found in library."
                : $"No playlists matching '{filter}'.");
            return;
        }
        var pt = new Table { ShowHeaders = false, Border = TableBorder.None };
        pt.AddColumn(new TableColumn(string.Empty));
        pt.AddColumn(new TableColumn(string.Empty) { Alignment = Justify.Right, NoWrap = true });
        foreach (var p in list)
            pt.AddRow($"[bold]{Markup.Escape(p.Name)}[/]", $"[dim]{p.TrackKeys.Count} tracks[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.Write(pt);
        AnsiConsole.WriteLine();
    }

    private static void SortWorkspace(TrackSet ws, string criterion)
    {
        var tracks = ws.Tracks.ToList();
        IOrderedEnumerable<Track> sorted = criterion switch
        {
            "recent" => tracks.OrderByDescending(t => t.DateAdded),
            "playcount" => tracks.OrderByDescending(t => t.PlayCount),
            _ => tracks.OrderByDescending(t => t.Rating).ThenByDescending(t => t.PlayCount),
        };
        ws.SetOrder(sorted.Select(t => t.TrackId));
    }

    private static void PrintStats(TrackSet ws)
    {
        if (ws.Count == 0)
        {
            AnsiConsole.MarkupLine("  [dim]Workspace is empty.[/]");
            return;
        }

        var tracks = ws.Tracks.ToList();
        var bpmTracks = tracks.Where(t => t.Bpm > 0).ToList();
        var bpmRange = bpmTracks.Count > 0
            ? $"{bpmTracks.Min(t => t.Bpm):0.#} – {bpmTracks.Max(t => t.Bpm):0.#}"
            : "—";

        var topKeys = tracks
            .Where(t => !string.IsNullOrEmpty(t.Key))
            .GroupBy(t => t.Key)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{Markup.Escape(g.Key)} ({g.Count()})");

        var topArtists = tracks
            .Where(t => !string.IsNullOrEmpty(t.Artist))
            .GroupBy(t => t.Artist)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{Markup.Escape(g.Key)} ({g.Count()})");

        AnsiConsole.WriteLine();
        var table = new Table { ShowHeaders = false, Border = TableBorder.None };
        table.AddColumn(new TableColumn(string.Empty) { Width = 14, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));
        table.AddRow("[dim]Tracks[/]", $"{ws.Count}{(ws.Ordered ? ", ordered" : "")}");
        table.AddRow("[dim]BPM range[/]", bpmRange);
        table.AddRow("[dim]Keys[/]", string.Join("  ", topKeys));
        table.AddRow("[dim]Artists[/]", string.Join("  ", topArtists));
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    // ── Seed resolution ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a seed argument — tries direct ID match first, then library search restricted to
    /// tracks in the workspace. Returns <c>null</c> and prints an error on failure.
    /// </summary>
    private static string? ResolveSeedArg(WorkspaceStore store, TrackSet ws, string arg)
    {
        if (ws.Contains(arg))
            return arg;

        var match = store.Library.Tracks.Values
            .Search(arg, TrackSearchFields.All)
            .FirstOrDefault(t => ws.Contains(t.TrackId));

        if (match is null)
        {
            ConsoleUi.PrintError(
                $"No workspace track matches '{arg}'. Use 'search {arg}' to preview, then 'add' first.");
            return null;
        }

        return match.TrackId;
    }

    private static Track? ResolveSeed(List<Track> workspaceTracks, string? seedTrackId)
    {
        if (!string.IsNullOrWhiteSpace(seedTrackId))
        {
            var match = workspaceTracks.FirstOrDefault(t => t.TrackId == seedTrackId);
            if (match is not null) return match;
        }

        return workspaceTracks
            .OrderBy(t => t.Bpm > 0 ? t.Bpm : double.PositiveInfinity)
            .FirstOrDefault();
    }

    // ── Parsing helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects the "search &lt;q&gt; in playlist &lt;name&gt;" pattern in a token array.
    /// </summary>
    private static bool HasInPlaylist(string[] tokens, out string? query, out string? playlistName)
    {
        for (var i = 2; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Equals("in", StringComparison.OrdinalIgnoreCase)
                && tokens[i + 1].Equals("playlist", StringComparison.OrdinalIgnoreCase))
            {
                query        = JoinFrom(tokens, 1, i);
                playlistName = JoinFrom(tokens, i + 2);
                if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(playlistName))
                    return true;
            }
        }
        query = null;
        playlistName = null;
        return false;
    }

    /// <summary>Joins tokens from <paramref name="start"/> (inclusive) to <paramref name="end"/> (exclusive).</summary>
    private static string JoinFrom(string[] tokens, int start, int end = -1)
    {
        if (end < 0) end = tokens.Length;
        return start >= end ? string.Empty : string.Join(' ', tokens[start..end]);
    }

    private static void PushUndo(Stack<string[]> undoIds, Stack<string?> undoSeed, TrackSet ws, string? seedId)
    {
        undoIds.Push(ws.Ids.ToArray());
        undoSeed.Push(seedId);
    }

    private static bool RequireArg(string value, string usage)
    {
        if (!string.IsNullOrWhiteSpace(value)) return true;
        ConsoleUi.PrintError(usage);
        return false;
    }

    private static PlaylistNode? FindPlaylist(WorkspaceStore store, string name)
    {
        return store.Library.Root.EnumeratePlaylists()
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? store.Library.Root.EnumeratePlaylists()
                .FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    // ── Output helpers ───────────────────────────────────────────────────────────────────────

    private static string BuildPrompt(TrackSet ws, string? seedId, RekordboxLibrary library, MediaPlayer player)
    {
        var seedLabel = seedId is not null && library.Tracks.TryGetValue(seedId, out var t)
            ? $" | seed: {t.Name}"
            : string.Empty;

        var nowPlaying = string.Empty;
        if (player.CurrentTrack is not null)
        {
            var state = player.IsPlaying ? "▶" : "⏸";
            var elapsed = FormatTime(player.CurrentTime);
            var total = FormatTime(player.TotalTime);
            nowPlaying = $" [{state} {player.CurrentTrack.Name} {elapsed}/{total}]";
        }

        return $"🎧 [{ws.Count} tracks{seedLabel}]{nowPlaying} > ";
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static void PrintPlaybackStatus(MediaPlayer player)
    {
        if (player.CurrentTrack is null) return;
        var state = player.IsPlaying ? "▶ Playing" : "⏸ Paused";
        var elapsed = FormatTime(player.CurrentTime);
        var total = FormatTime(player.TotalTime);
        var name = Markup.Escape(player.CurrentTrack.Name);
        var artist = Markup.Escape(player.CurrentTrack.Artist);
        AnsiConsole.MarkupLine($"  [dim]{state}: {artist} — {name} ({elapsed} / {total})[/]");
    }

    private static void PrintResult(object result, TrackSet ws)
    {
        var errorProp = result.GetType().GetProperty("error");
        if (errorProp?.GetValue(result) is string err)
        {
            ConsoleUi.PrintError(err);
            return;
        }
        AnsiConsole.MarkupLine($"  [dim]→ {ws.Count} tracks{(ws.Ordered ? ", ordered" : "")}.[/]");
    }

    private static void PrintOpeningScreen(TrackSet ws)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]🎧 DJ Buddy Interactive[/]");
        AnsiConsole.MarkupLine($"  Workspace: [dim]{ws.Count} tracks[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]Type a search query to preview matches.[/]");
        AnsiConsole.MarkupLine("  [dim]Use [bold]add <query>[/] to add tracks to the workspace.[/]");
        AnsiConsole.MarkupLine("  [dim]Use [bold]help[/] for all commands.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim italic]Examples:[/]");
        AnsiConsole.MarkupLine("    [dim]add zeds dead[/]");
        AnsiConsole.MarkupLine("    [dim]add playlist \"Dubstep Favorites\"[/]");
        AnsiConsole.MarkupLine("    [dim]filter key 8A[/]");
        AnsiConsole.MarkupLine("    [dim]filter bpm 140 150[/]");
        AnsiConsole.MarkupLine("    [dim]seed \"One Who Lost\"[/]");
        AnsiConsole.MarkupLine("    [dim]order 20[/]");
        AnsiConsole.MarkupLine("    [dim]save \"Friday Set\"[/]");
        AnsiConsole.WriteLine();
    }

    private static void PrintHelp()
    {
        AnsiConsole.WriteLine();

        var table = new Table { ShowHeaders = false, Border = TableBorder.None };
        table.AddColumn(new TableColumn(string.Empty) { Width = 38, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));

        static void Section(Table t, string label)
        {
            t.AddEmptyRow();
            t.AddRow($"[dim italic]{label}[/]", string.Empty);
        }

        Section(table, "Workspace");
        table.AddRow("[bold]add <query>[/]",                    "Add matching tracks");
        table.AddRow("[bold]add playlist <name>[/]",            "Add all tracks from a playlist");
        table.AddRow("[bold]filter <query>[/]",                 "Keep only matching tracks");
        table.AddRow("[bold]filter key <camelot>[/]",           "Keep tracks in key, e.g. 8A");
        table.AddRow("[bold]filter bpm <min> <max>[/]",         "Keep tracks in BPM range");
        table.AddRow("[bold]filter playlist <name>[/]",         "Keep tracks from a playlist");
        table.AddRow("[bold]remove <query>[/]",                 "Remove matching tracks");
        table.AddRow("[bold]clear[/]",                          "Empty workspace");
        table.AddRow("[bold]undo[/]",                           "Undo last workspace change");

        Section(table, "Preview");
        table.AddRow("[bold]search <query>[/]",                 "Preview results without changing workspace");
        table.AddRow("[bold]find <query>[/]",                   "Alias for search");
        table.AddRow("[bold]search <q> in playlist <name>[/]",  "Search inside a playlist");
        table.AddRow("[bold]show [[n]][/]",                     "Show workspace tracks (default 20)");
        table.AddRow("[bold]show playlist <name>[/]",           "Show all tracks in a playlist");
        table.AddRow("[bold]playlists [[query]][/]",            "List library playlists");
        table.AddRow("[bold]count[/]",                          "Show workspace track count");
        table.AddRow("[bold]stats[/]",                          "Show BPM range, keys, artists");

        Section(table, "Ordering");
        table.AddRow("[bold]seed <trackId or query>[/]",        "Set the starting track");
        table.AddRow("[bold]order [[n]][/]",                    "Build a compatible ordered sequence");
        table.AddRow("[bold]next[/]",                           "Walk the graph one neighbor at a time");
        table.AddRow("[bold]sort by rating|recent|playCount[/]","Sort workspace without removing tracks");
        table.AddRow("[bold]take <n> [[by criterion]][/]",       "Keep top N tracks");

        Section(table, "Playback");
        table.AddRow("[bold]play <query or id>[/]",             "Load and play a track");
        table.AddRow("[bold]play[/]",                           "Resume / toggle play–pause");
        table.AddRow("[bold]pause[/]",                          "Pause or resume playback");
        table.AddRow("[bold]ff[/]",                             "Skip forward 5 seconds");
        table.AddRow("[bold]rw[/]",                             "Skip backward 5 seconds");
        table.AddRow("[bold]stop[/]",                           "Stop playback");

        Section(table, "Session");
        table.AddRow("[bold]save [[name]][/]",                  "Save to DJ_BUDDY and exit");
        table.AddRow("[bold]exit[/]",                           "Exit without saving");
        table.AddRow("[bold]help | ?[/]",                       "Show this help");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    // ── Pick-phase helpers ───────────────────────────────────────────────────────────────────

    private static List<CompatibilityEdge> NeighborsInWorkspace(
        BidirectionalGraph<Track, TrackEdge> graph,
        Track current,
        TrackSet workspace,
        HashSet<string> picked)
    {
        if (!graph.TryGetOutEdges(current, out var outEdges))
            return [];

        return outEdges.OfType<CompatibilityEdge>()
            .Where(e => workspace.Contains(e.Target.TrackId) && !picked.Contains(e.Target.TrackId))
            .OrderBy(e => e.Weight)
            .ToList();
    }

    private static void PrintCurrent(Track t, int step)
    {
        AnsiConsole.MarkupLine(
            $"  [bold]#{step}[/]  🎵 [bold]{Markup.Escape(t.Artist)}[/] — {Markup.Escape(t.Name)}  " +
            $"[dim]{t.Bpm:0.#} BPM  {Markup.Escape(t.Key)}[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>Prints neighbors[from..to] with 1-based numbers starting at from+1.</summary>
    private static void PrintNeighbors(List<CompatibilityEdge> neighbors, int from, int to)
    {
        if (from == 0)
            AnsiConsole.MarkupLine("  [dim]Compatible next tracks:[/]");

        var table = new Table { ShowHeaders = false, Border = TableBorder.None };
        table.AddColumn(new TableColumn(string.Empty) { Width = 5, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty) { Alignment = Justify.Right });

        for (var i = from; i < to; i++)
        {
            var e = neighbors[i];
            table.AddRow(
                $"[bold]{i + 1}.[/]",
                $"{Markup.Escape(e.Target.Artist)} — {Markup.Escape(e.Target.Name)}",
                $"[dim]{e.Target.Bpm:0.#} BPM {Markup.Escape(e.Target.Key)} · {e.Relation}/{e.Tier}[/]");
        }

        AnsiConsole.Write(table);
        if (to < neighbors.Count)
            AnsiConsole.MarkupLine($"  [dim]… {neighbors.Count - to} more. Type [bold]more[/] to show.[/]");
        AnsiConsole.WriteLine();
    }

    private static void PrintPickHelp()
    {
        AnsiConsole.MarkupLine(
            "  [dim]Pick a neighbor by number. " +
            "Commands: [bold]more[/] (show more), [bold]skip[/] (refresh list), " +
            "[bold]commit[/] (save as playlist), [bold]quit[/] (back to workspace), [bold]help[/][/]");
    }

    private static void OfferCommit(WorkspaceStore store, TrackSet ws, List<Track> picked)
    {
        AnsiConsole.MarkupLine($"  [dim]Picked {picked.Count} tracks. Save as a new playlist? (y/N)[/]");
        Console.Write("  > ");
        var answer = Console.ReadLine()?.Trim();
        if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
            CommitPicked(store, ws, picked);
    }

    private static void CommitPicked(WorkspaceStore store, TrackSet ws, List<Track> picked)
    {
        var suggested = $"{ws.Name}-picked";
        AnsiConsole.MarkupLine($"  [dim]Playlist name? (default: {Markup.Escape(suggested)})[/]");
        Console.Write("  > ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(name)) name = suggested;

        var staging = store.GetOrCreate(name);
        staging.SetOrder(picked.Select(t => t.TrackId));

        var (ok, message, count) = store.Commit(staging.Name, name);
        if (ok)
            ConsoleUi.PrintStatus(
                $"Saved '{message}' ({count} tracks) to DJ_BUDDY. Use /export to write rekordbox.xml.");
        else
            ConsoleUi.PrintError(message);
    }
}
