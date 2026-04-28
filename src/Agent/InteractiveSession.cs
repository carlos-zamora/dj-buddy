using DJBuddy.Agent.Tools;
using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using QuikGraph;
using Spectre.Console;

namespace DJBuddy.Agent;

/// <summary>
/// REPL-side interactive workspace builder + set picker. Runs entirely offline — no LLM calls,
/// so the whole flow costs zero tokens. Two phases share a single command loop:
/// <list type="bullet">
///   <item><description><b>Build</b> — populate / refine a workspace via search, set ops,
///   filter helpers. Think "the LLM tools, but typed by hand".</description></item>
///   <item><description><b>Pick</b> — once ordered or seeded, walk the compatibility graph one
///   neighbor at a time. Same logic as the original interactive mode.</description></item>
/// </list>
/// </summary>
internal static class InteractiveSession
{
    private const string DefaultScratchName = "_interactive";
    private const int NeighborLimit = 10;
    private const int DefaultShowLimit = 20;

    /// <summary>
    /// Enters the interactive sub-REPL.
    /// </summary>
    /// <param name="store">The session workspace store. New workspaces are persisted here.</param>
    /// <param name="graphTask">Shared compatibility graph task; awaited lazily on first ordering / pick.</param>
    /// <param name="workspaceName">
    /// Optional workspace to operate on. When null/blank, a scratch workspace named
    /// <c>_interactive</c> is created (or reused). Otherwise the named workspace is loaded
    /// (created empty if it doesn't exist).
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

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]Interactive workspace builder — '{Markup.Escape(ws.Name)}' ({ws.Count} tracks)[/]");
        PrintInteractiveHelp();

        while (true)
        {
            Console.Write($"  [{ws.Name}{(ws.Ordered ? "*" : "")}] > ");
            var raw = Console.ReadLine();
            if (raw is null) return;

            var line = raw.Trim();
            if (line.Length == 0) continue;

            var (cmd, rest) = SplitCommand(line);

            try
            {
                switch (cmd)
                {
                    case "help":
                    case "?":
                        PrintInteractiveHelp();
                        break;

                    case "show":
                    {
                        var n = int.TryParse(rest, out var parsed) && parsed > 0 ? parsed : DefaultShowLimit;
                        var tracks = ws.Tracks.Take(n).ToList();
                        ConsoleUi.RenderTrackTable(
                            tracks,
                            ["artist", "name", "bpm", "key"],
                            $"{ws.Name} (showing {tracks.Count} of {ws.Count})");
                        break;
                    }

                    case "count":
                        AnsiConsole.MarkupLine($"  [dim]{ws.Count} tracks, {(ws.Ordered ? "ordered" : "unordered")}.[/]");
                        break;

                    case "clear":
                        ws.Apply(TrackSetMode.Replace, []);
                        seedId = null;
                        AnsiConsole.MarkupLine("  [dim]Workspace cleared.[/]");
                        break;

                    case "add":
                        ApplySearch(store, ws.Name, "union", rest);
                        break;

                    case "intersect":
                        ApplySearch(store, ws.Name, "intersect", rest);
                        break;

                    case "except":
                        ApplySearch(store, ws.Name, "except", rest);
                        break;

                    case "add-playlist":
                    {
                        if (!Require(rest, "Usage: add-playlist <playlist-name>")) break;
                        var result = WorkspaceTools.WorkspaceFromLibraryPlaylist(store, ws.Name, "union", rest);
                        PrintToolResult(result, ws);
                        break;
                    }

                    case "key":
                    {
                        if (!Require(rest, "Usage: key <camelot>  (e.g. key 8A)")) break;
                        var result = WorkspaceTools.WorkspaceFromSearch(
                            store, ws.Name, "intersect", query: "", playlistFilter: null,
                            key: rest, minBpm: null, maxBpm: null, limit: "5000");
                        PrintToolResult(result, ws);
                        break;
                    }

                    case "bpm":
                    {
                        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2)
                        {
                            ConsoleUi.PrintError("Usage: bpm <min> <max>  (e.g. bpm 140 160)");
                            break;
                        }
                        var result = WorkspaceTools.WorkspaceFromSearch(
                            store, ws.Name, "intersect", query: "", playlistFilter: null,
                            key: null, minBpm: parts[0], maxBpm: parts[1], limit: "5000");
                        PrintToolResult(result, ws);
                        break;
                    }

                    case "playlist-filter":
                    {
                        if (!Require(rest, "Usage: playlist-filter <substring>")) break;
                        var result = WorkspaceTools.WorkspaceFromSearch(
                            store, ws.Name, "intersect", query: "", playlistFilter: rest,
                            key: null, minBpm: null, maxBpm: null, limit: "5000");
                        PrintToolResult(result, ws);
                        break;
                    }

                    case "trim":
                    {
                        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0)
                        {
                            ConsoleUi.PrintError("Usage: trim <count> [by]   by = rating | recent | playCount | random");
                            break;
                        }
                        var by = parts.Length > 1 ? parts[1] : null;
                        var result = WorkspaceTools.TrimWorkspace(store, ws.Name, parts[0], by);
                        PrintToolResult(result, ws);
                        break;
                    }

                    case "seed":
                        if (!Require(rest, "Usage: seed <trackId>")) break;
                        if (!ws.Contains(rest))
                        {
                            ConsoleUi.PrintError($"Track ID '{rest}' is not in this workspace.");
                            break;
                        }
                        seedId = rest;
                        AnsiConsole.MarkupLine($"  [dim]Seed set to {Markup.Escape(rest)}.[/]");
                        break;

                    case "order":
                    {
                        if (ws.Count == 0) { ConsoleUi.PrintError("Workspace is empty."); break; }
                        var target = int.TryParse(rest, out var t) && t > 0 ? (string?)t.ToString() : null;
                        var result = await WorkspaceTools.OrderWorkspace(store, graphTask, ws.Name, seedId, target);
                        PrintToolResult(result, ws);
                        ConsoleUi.RenderTrackTable(
                            ws.Tracks.ToList(),
                            ["artist", "name", "bpm", "key"],
                            $"{ws.Name} (ordered)");
                        break;
                    }

                    case "pick":
                        if (ws.Count == 0) { ConsoleUi.PrintError("Workspace is empty."); break; }
                        await RunPickPhaseAsync(store, graphTask, ws, seedId);
                        return;

                    case "commit":
                    {
                        if (ws.Count == 0) { ConsoleUi.PrintError("Workspace is empty."); break; }
                        var name = string.IsNullOrWhiteSpace(rest) ? ws.Name : rest;
                        var (ok, message, count) = store.Commit(ws.Name, name);
                        if (ok)
                            ConsoleUi.PrintStatus($"Committed '{message}' ({count} tracks) to DJ_BUDDY. Type /export to write rekordbox.xml.");
                        else
                            ConsoleUi.PrintError(message);
                        return;
                    }

                    case "q":
                    case "quit":
                    case "exit":
                        AnsiConsole.MarkupLine("  [dim]Interactive session ended.[/]");
                        return;

                    default:
                        ConsoleUi.PrintError($"Unknown command '{cmd}'. Type ? for help.");
                        break;
                }
            }
            catch (Exception ex)
            {
                ConsoleUi.PrintError($"Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Walks <paramref name="ws"/> one neighbor at a time using the compatibility graph. Used
    /// after the user finishes the build phase and chooses to hand-pick the set order.
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

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]Pick mode — building from '{Markup.Escape(ws.Name)}'[/]");
        AnsiConsole.MarkupLine("  [dim]Pick a neighbor by number. Commands: [bold]s[/]kip reshuffle, [bold]c[/]ommit as playlist, [bold]q[/]uit.[/]");
        AnsiConsole.WriteLine();

        PrintCurrent(currentTrack, step: 1);

        while (true)
        {
            var neighbors = NeighborsInWorkspace(graph, currentTrack, ws, pickedIds, NeighborLimit);
            if (neighbors.Count == 0)
            {
                AnsiConsole.MarkupLine("  [yellow]No more compatible neighbors in this workspace.[/]");
                break;
            }

            PrintNeighbors(neighbors);

            Console.Write("  > ");
            var input = Console.ReadLine()?.Trim();
            if (input is null) break;

            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("  [dim]Pick session ended.[/]");
                break;
            }

            if (string.Equals(input, "s", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(input, "c", StringComparison.OrdinalIgnoreCase))
            {
                CommitPicked(store, ws, picked);
                return;
            }

            if (!int.TryParse(input, out var choice) || choice < 1 || choice > neighbors.Count)
            {
                AnsiConsole.MarkupLine($"  [red]Enter 1–{neighbors.Count}, or s/c/q.[/]");
                continue;
            }

            var nextEdge = neighbors[choice - 1];
            currentTrack = nextEdge.Target;
            picked.Add(currentTrack);
            pickedIds.Add(currentTrack.TrackId);

            AnsiConsole.WriteLine();
            PrintCurrent(currentTrack, step: picked.Count);
        }

        if (picked.Count > 1)
            OfferCommit(store, ws, picked);
    }

    /// <summary>
    /// Routes a free-text search-style command (add / intersect / except) through
    /// <see cref="WorkspaceTools.WorkspaceFromSearch"/>. The user types <c>add bpm:140-160 zeds dead</c>
    /// loosely — for now we treat the whole rest as the query and rely on bpm/key/playlist-filter
    /// commands for filtered ops. Empty rest is allowed (falls through to a no-op search).
    /// </summary>
    private static void ApplySearch(WorkspaceStore store, string wsName, string mode, string query)
    {
        var result = WorkspaceTools.WorkspaceFromSearch(
            store, wsName, mode, query ?? string.Empty,
            playlistFilter: null, key: null, minBpm: null, maxBpm: null, limit: "5000");
        var ws = store.Get(wsName);
        if (ws is not null) PrintToolResult(result, ws);
    }

    private static void PrintToolResult(object result, TrackSet ws)
    {
        // Tools return small anonymous objects. We don't need full reflection — pull the count
        // off the workspace directly and surface any error string.
        var errorProp = result.GetType().GetProperty("error");
        if (errorProp?.GetValue(result) is string err)
        {
            ConsoleUi.PrintError(err);
            return;
        }
        AnsiConsole.MarkupLine($"  [dim]→ {ws.Count} tracks, {(ws.Ordered ? "ordered" : "unordered")}.[/]");
    }

    private static bool Require(string value, string usage)
    {
        if (!string.IsNullOrWhiteSpace(value)) return true;
        ConsoleUi.PrintError(usage);
        return false;
    }

    private static (string Cmd, string Args) SplitCommand(string line)
    {
        var space = line.IndexOf(' ');
        return space < 0
            ? (line.ToLowerInvariant(), string.Empty)
            : (line[..space].ToLowerInvariant(), line[(space + 1)..].Trim());
    }

    /// <summary>Prints the inline help table for the build / pick subcommands.</summary>
    private static void PrintInteractiveHelp()
    {
        AnsiConsole.WriteLine();
        var table = new Table { ShowHeaders = false, Border = TableBorder.None };
        table.AddColumn(new TableColumn(string.Empty) { Width = 28, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));

        table.AddRow("[bold]add <query>[/]", "Search library, union results into workspace");
        table.AddRow("[bold]intersect <query>[/]", "Keep only matches");
        table.AddRow("[bold]except <query>[/]", "Remove matches");
        table.AddRow("[bold]add-playlist <name>[/]", "Union a library playlist's tracks");
        table.AddRow("[bold]key <camelot>[/]", "Intersect by Camelot key (e.g. key 8A)");
        table.AddRow("[bold]bpm <min> <max>[/]", "Intersect by BPM range");
        table.AddRow("[bold]playlist-filter <substr>[/]", "Intersect with tracks in any playlist matching the substring");
        table.AddRow("[bold]trim <n> [[by]][/]", "Shrink to top N (by = rating | recent | playCount | random)");
        table.AddRow("[bold]show [[n]][/]", "Show the first N tracks (default 20)");
        table.AddRow("[bold]count[/]", "Print workspace size");
        table.AddRow("[bold]clear[/]", "Empty the workspace");
        table.AddRow("[bold]seed <trackId>[/]", "Set the seed track for ordering / pick");
        table.AddRow("[bold]order [[targetCount]][/]", "Order via the compatibility graph (optionally trim to N first)");
        table.AddRow("[bold]pick[/]", "Walk the graph one neighbor at a time");
        table.AddRow("[bold]commit [[name]][/]", "Commit to DJ_BUDDY and exit");
        table.AddRow("[bold]q | quit[/]", "Exit without committing");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
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

    private static List<CompatibilityEdge> NeighborsInWorkspace(
        BidirectionalGraph<Track, TrackEdge> graph,
        Track current,
        TrackSet workspace,
        HashSet<string> picked,
        int limit)
    {
        if (!graph.TryGetOutEdges(current, out var outEdges))
            return [];

        return outEdges.OfType<CompatibilityEdge>()
            .Where(e => workspace.Contains(e.Target.TrackId) && !picked.Contains(e.Target.TrackId))
            .OrderBy(e => e.Weight)
            .Take(limit)
            .ToList();
    }

    private static void PrintCurrent(Track t, int step)
    {
        AnsiConsole.MarkupLine(
            $"  [bold]#{step}[/]  🎵 [bold]{Markup.Escape(t.Artist)}[/] — {Markup.Escape(t.Name)}  " +
            $"[dim]{t.Bpm:0.#} BPM  {Markup.Escape(t.Key)}[/]");
        AnsiConsole.WriteLine();
    }

    private static void PrintNeighbors(List<CompatibilityEdge> neighbors)
    {
        AnsiConsole.MarkupLine("  [dim]Compatible next tracks:[/]");
        var table = new Table { ShowHeaders = false, Border = TableBorder.None };
        table.AddColumn(new TableColumn(string.Empty) { Width = 4, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty) { Alignment = Justify.Right });

        for (var i = 0; i < neighbors.Count; i++)
        {
            var e = neighbors[i];
            var label = $"[bold]{i + 1}.[/]";
            var title = $"{Markup.Escape(e.Target.Artist)} — {Markup.Escape(e.Target.Name)}";
            var meta = $"[dim]{e.Target.Bpm:0.#} BPM {Markup.Escape(e.Target.Key)} · {e.Relation}/{e.Tier}[/]";
            table.AddRow(label, title, meta);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static void OfferCommit(WorkspaceStore store, TrackSet ws, List<Track> picked)
    {
        AnsiConsole.MarkupLine($"  [dim]Picked {picked.Count} tracks. Commit as a new playlist? (y/N)[/]");
        Console.Write("  > ");
        var answer = Console.ReadLine()?.Trim();
        if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
            CommitPicked(store, ws, picked);
    }

    private static void CommitPicked(WorkspaceStore store, TrackSet ws, List<Track> picked)
    {
        var suggested = $"{ws.Name}-interactive";
        AnsiConsole.MarkupLine($"  [dim]Playlist name? (default: {Markup.Escape(suggested)})[/]");
        Console.Write("  > ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(name)) name = suggested;

        // Stage the picked sequence in its own workspace so the source workspace stays intact for
        // a follow-up session.
        var staging = store.GetOrCreate(name);
        staging.SetOrder(picked.Select(t => t.TrackId));

        var (ok, message, count) = store.Commit(staging.Name, name);
        if (ok)
            ConsoleUi.PrintStatus($"Committed '{message}' ({count} tracks) to DJ_BUDDY. Type /export to write rekordbox.xml.");
        else
            ConsoleUi.PrintError(message);
    }
}
