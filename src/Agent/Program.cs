using GitHub.Copilot;
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
                ConsoleUi.PrintError($"rekordbox.xml not found: {xmlPath}");
                ConsoleUi.PrintError("Usage: Agent [path-to-rekordbox.xml]");
                return 1;
            }

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
        var graphTask = Task.Run(() => TrackGraphBuilder.Build(library));

        // ── Start Copilot session in background (non-blocking) ──────────────────────

        var store = new WorkspaceStore(library);
        var initialConnect = TryCreateSessionAsync(store, graphTask, playlistCount);
        var initialConnectPending = true;

        // ── REPL ────────────────────────────────────────────────────────────────────

        ConsoleUi.PrintWelcome();

        CancellationTokenSource? loopCts = null;
        bool loopStreamingStarted = false;
        TaskCompletionSource? loopSpinnerCleared = null;
        CopilotSession? handledSession = null;
        Connection? currentConnection = null;
        string? lastConnectError = null;
        const string spinnerSuffix = " Thinking...";

        void RegisterSessionHandler(CopilotSession s) => s.On<SessionEvent>(ev =>
        {
            if (ev is ToolExecutionStartEvent toolStart)
            {
                if (loopStreamingStarted)
                {
                    // Cursor is mid-line in streaming text — move to a fresh line first.
                    Console.WriteLine();
                }
                else
                {
                    // Spinner may still be running — stop it and wait for its clear.
                    loopCts?.Cancel();
                    loopSpinnerCleared?.Task.Wait();
                }
                Console.Write("\x1b[2K\r");
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
                    loopSpinnerCleared?.Task.Wait();
                    Console.WriteLine();
                }
                Console.Write(_mdRenderer.Process(delta.Data.DeltaContent));
            }
        });

        while (true)
        {
            ConsoleUi.PrintPrompt();
            var input = Console.ReadLine();

            if (input is null)
                break;

            var trimmed = input.Trim();
            if (trimmed.Length == 0)
                continue;

            if (string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "/exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "/quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

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

                    case "/workspaces":
                        ConsoleUi.PrintWorkspaces(store);
                        continue;

                    case "/interactive":
                    {
                        // Optional single positional arg: workspace name. No name → scratch workspace.
                        var wsName = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
                            ? parts[1].Trim()
                            : null;

                        try
                        {
                            await InteractiveSession.RunAsync(store, graphTask, wsName);
                        }
                        catch (Exception ex)
                        {
                            ConsoleUi.PrintError($"Interactive session failed: {ex.Message}");
                        }

                        continue;
                    }

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
                            store = new WorkspaceStore(library);
                            graphTask = Task.Run(() => TrackGraphBuilder.Build(library));
                            await DisposeClientAsync(currentConnection?.Client);
                            initialConnect = TryCreateSessionAsync(store, graphTask, playlistCount);
                            initialConnectPending = true;
                            currentConnection = null;
                            lastConnectError = null;
                            handledSession = null;
                            _mdRenderer.Flush();
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
                            ConsoleUi.PrintError("No committed playlists to export. Ask DJ Buddy to build a workspace and commit it first.");
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

                    case "/reconnect":
                    {
                        // Settle any still-pending startup attempt first so its client can be
                        // adopted (or later disposed) rather than leaked.
                        if (initialConnectPending)
                        {
                            (currentConnection, lastConnectError) = await initialConnect;
                            initialConnectPending = false;
                        }

                        var (conn, error) = await RunWithSpinnerAsync(
                            "Reconnecting to GitHub Copilot...",
                            TryCreateSessionAsync(store, graphTask, playlistCount));

                        if (conn is null)
                        {
                            // Keep the existing connection (it may still be alive); the failed
                            // attempt already disposed its own partial client.
                            lastConnectError = error;
                            ConsoleUi.PrintError($"Reconnect failed: {error}");
                        }
                        else
                        {
                            await DisposeClientAsync(currentConnection?.Client);
                            currentConnection = conn;
                            lastConnectError = null;
                            handledSession = null;
                            AnsiConsole.MarkupLine("[green]✔ Reconnected.[/]");
                        }

                        continue;
                    }

                    case "/search":
                    {
                        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                        {
                            ConsoleUi.PrintError("Usage: /search <query>");
                            continue;
                        }

                        var q = parts[1].Trim();
                        var hits = library.Tracks.Values
                            .Search(q, TrackSearchFields.All)
                            .OrderBy(TrackSortKey.Title)
                            .Take(50)
                            .ToList();
                        ConsoleUi.RenderTrackTable(hits, ["artist", "name", "bpm", "key"], $"Search: {q}");
                        continue;
                    }

                    case "/suggest":
                    {
                        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                        {
                            ConsoleUi.PrintError("Usage: /suggest <trackId>");
                            continue;
                        }

                        try { await HandleSuggestAsync(library, graphTask, parts[1].Trim()); }
                        catch (Exception ex) { ConsoleUi.PrintError($"Error: {ex.Message}"); }
                        continue;
                    }

                    case "/similar":
                    {
                        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                        {
                            ConsoleUi.PrintError("Usage: /similar <trackId>");
                            continue;
                        }

                        try { await HandleSimilarAsync(library, graphTask, parts[1].Trim()); }
                        catch (Exception ex) { ConsoleUi.PrintError($"Error: {ex.Message}"); }
                        continue;
                    }

                    default:
                        ConsoleUi.PrintError($"Unknown command: {cmd}. Type /help for available commands.");
                        continue;
                }
            }

            try
            {
                // Resolve the background startup connect the first time we need it.
                if (initialConnectPending)
                {
                    (currentConnection, lastConnectError) = await initialConnect;
                    initialConnectPending = false;
                }

                if (currentConnection is null)
                {
                    var reason = lastConnectError is null
                        ? " to GitHub Copilot."
                        : $": {Markup.Escape(lastConnectError)}";
                    AnsiConsole.MarkupLine($"[yellow]⚠ AI offline[/] [dim]— could not connect{reason}[/]");
                    AnsiConsole.MarkupLine("[dim]  Use /reconnect to retry, or /search, /suggest, /interactive to work offline.[/]");
                    Console.WriteLine();
                    continue;
                }

                var currentSession = currentConnection.Session;
                if (!ReferenceEquals(currentSession, handledSession))
                {
                    RegisterSessionHandler(currentSession);
                    handledSession = currentSession;
                }

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
                    Console.Write("\x1b[2K\r");
                    spinnerCleared.SetResult();
                });

                await currentSession.SendAndWaitAsync(new MessageOptions { Prompt = trimmed });

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

        // ── Graceful teardown: kill the Copilot runtime so it doesn't orphan ────────────
        if (currentConnection is null && initialConnectPending)
        {
            // Startup connect may still be in flight; wait briefly to grab its client.
            if (await Task.WhenAny(initialConnect, Task.Delay(TimeSpan.FromSeconds(2))) == initialConnect)
                currentConnection = initialConnect.Result.Conn;
        }

        await DisposeClientAsync(currentConnection?.Client);

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

    /// <summary>
    /// Pairs a live Copilot client with its session so both can be torn down together. The client
    /// owns the bundled runtime subprocess; keeping the reference lets us dispose it on
    /// reconnect/reload/exit instead of leaking an orphaned process.
    /// </summary>
    private sealed record Connection(CopilotClient Client, CopilotSession Session);

    /// <summary>How long to wait for a Copilot connect (start + session create) before giving up.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Attempts to connect to GitHub Copilot and create a session, bounded by
    /// <see cref="ConnectTimeout"/>. On success returns the <see cref="Connection"/> and a null
    /// error; on failure returns a null connection and a human-readable reason (the caller shows
    /// it and continues in offline mode). The partially started client is disposed on failure so
    /// its runtime subprocess doesn't linger.
    /// </summary>
    private static async Task<(Connection? Conn, string? Error)> TryCreateSessionAsync(
        WorkspaceStore store,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        int plCount)
    {
        CopilotClient? client = null;
        try
        {
            using var cts = new CancellationTokenSource(ConnectTimeout);
            client = new CopilotClient();
            await client.StartAsync(cts.Token);
            var session = await CreateSessionCoreAsync(client, store, graphTask, plCount, cts.Token);
            return (new Connection(client, session), null);
        }
        catch (Exception ex)
        {
            // Tear down the half-started runtime so a retry gets a clean process.
            await DisposeClientAsync(client);
            var reason = ex is OperationCanceledException
                ? $"connection timed out after {ConnectTimeout.TotalSeconds:0}s"
                : ex.Message;
            return (null, reason);
        }
    }

    /// <summary>
    /// Best-effort teardown of a Copilot client: graceful <see cref="CopilotClient.StopAsync"/>
    /// with a bounded wait, escalating to <see cref="CopilotClient.ForceStopAsync"/> if it stalls,
    /// then disposal. Safe to call with <c>null</c>. Never throws.
    /// </summary>
    private static async Task DisposeClientAsync(CopilotClient? client)
    {
        if (client is null)
            return;

        try
        {
            var stop = client.StopAsync();
            if (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(3))) == stop)
                await stop; // observe any exception from a completed stop
            else
                await client.ForceStopAsync();
        }
        catch
        {
            // Teardown is best-effort — an already-dead runtime may fault here.
        }
        finally
        {
            try { await client.DisposeAsync(); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Awaits <paramref name="work"/> while animating a single-line spinner labelled
    /// <paramref name="label"/>, clearing the line when it completes. Used by blocking commands
    /// (e.g. <c>/reconnect</c>) that need immediate visual feedback.
    /// </summary>
    private static async Task<T> RunWithSpinnerAsync<T>(string label, Task<T> work)
    {
        var frames = new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };
        using var cts = new CancellationTokenSource();
        var spinner = Task.Run(async () =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                Console.Write($"\r  \x1b[2m\x1b[33m{frames[i++ % frames.Length]} {label}\x1b[0m");
                try { await Task.Delay(80, cts.Token); }
                catch (OperationCanceledException) { break; }
            }
            Console.Write("\x1b[2K\r");
        });

        try
        {
            return await work;
        }
        finally
        {
            cts.Cancel();
            await spinner;
        }
    }

    /// <summary>
    /// Renders the top compatible next-track suggestions for a given source track ID.
    /// </summary>
    private static async Task HandleSuggestAsync(
        RekordboxLibrary library,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        string trackId)
    {
        if (!library.Tracks.TryGetValue(trackId, out var source))
        {
            ConsoleUi.PrintError($"Track not found: {trackId}");
            return;
        }

        var graph = await graphTask.ConfigureAwait(false);
        if (!graph.TryGetOutEdges(source, out var outEdges))
        {
            ConsoleUi.PrintStatus($"No compatible tracks found for {trackId}.");
            return;
        }

        var suggestions = outEdges.OfType<CompatibilityEdge>().OrderBy(e => e.Weight).Take(10).ToList();
        if (suggestions.Count == 0)
        {
            ConsoleUi.PrintStatus($"No compatible tracks found for {trackId}.");
            return;
        }

        var title = $"Suggestions after: {source.Artist} — {source.Name}";
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]{Markup.Escape(title)}[/]  [dim]({suggestions.Count} tracks)[/]");
        AnsiConsole.WriteLine();

        var table = new Table { Border = TableBorder.None, ShowHeaders = true };
        table.AddColumn(new TableColumn("[dim]#[/]") { Width = 4, NoWrap = true });
        table.AddColumn(new TableColumn("[dim]Artist[/]"));
        table.AddColumn(new TableColumn("[dim]Title[/]"));
        table.AddColumn(new TableColumn("[dim]BPM[/]") { Alignment = Justify.Right, NoWrap = true });
        table.AddColumn(new TableColumn("[dim]Key[/]") { NoWrap = true });
        table.AddColumn(new TableColumn("[dim]Relation[/]") { NoWrap = true });
        table.AddColumn(new TableColumn("[dim]BPM Δ%[/]") { Alignment = Justify.Right, NoWrap = true });

        for (var i = 0; i < suggestions.Count; i++)
        {
            var e = suggestions[i];
            var t = e.Target;
            table.AddRow(
                $"[dim]{i + 1,3}[/]",
                Markup.Escape(t.Artist),
                Markup.Escape(t.Name),
                $"[yellow]{t.Bpm:F1}[/]",
                $"[yellow]{Markup.Escape(t.Key)}[/]",
                $"[dim]{Markup.Escape(e.Relation.ToString())}[/]",
                $"[dim]{e.BpmDeltaPercent:F1}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Renders similar tracks (harmonic compat + playlist co-occurrence) for a given source track ID.
    /// </summary>
    private static async Task HandleSimilarAsync(
        RekordboxLibrary library,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        string trackId)
    {
        if (!library.Tracks.TryGetValue(trackId, out var source))
        {
            ConsoleUi.PrintError($"Track not found: {trackId}");
            return;
        }

        var graph = await graphTask.ConfigureAwait(false);
        if (!graph.TryGetOutEdges(source, out var outEdges))
        {
            ConsoleUi.PrintStatus($"No similar tracks found for {trackId}.");
            return;
        }

        // Group by target ID, keeping best compat edge + any co-occur edge.
        var grouped = new Dictionary<string, (CompatibilityEdge? Compat, CoOccurrenceEdge? CoOccur)>();
        foreach (var edge in outEdges)
        {
            var id = edge.Target.TrackId;
            grouped.TryGetValue(id, out var slot);
            if (edge is CompatibilityEdge ce)
            {
                if (slot.Compat is null || ce.Weight < slot.Compat.Weight)
                    slot.Compat = ce;
            }
            else if (edge is CoOccurrenceEdge coe)
            {
                slot.CoOccur = coe;
            }
            grouped[id] = slot;
        }

        var results = grouped
            .Select(kv => (
                Track: kv.Value.Compat?.Target ?? kv.Value.CoOccur!.Target,
                CompatWeight: kv.Value.Compat?.Weight,
                CoOccurCount: kv.Value.CoOccur?.PlaylistCount,
                Score: (kv.Value.Compat?.Weight ?? 0) + (kv.Value.CoOccur?.Weight ?? 0)))
            .OrderBy(x => x.Score)
            .Take(10)
            .ToList();

        if (results.Count == 0)
        {
            ConsoleUi.PrintStatus($"No similar tracks found for {trackId}.");
            return;
        }

        var title = $"Similar to: {source.Artist} — {source.Name}";
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]{Markup.Escape(title)}[/]  [dim]({results.Count} tracks)[/]");
        AnsiConsole.WriteLine();

        var table = new Table { Border = TableBorder.None, ShowHeaders = true };
        table.AddColumn(new TableColumn("[dim]#[/]") { Width = 4, NoWrap = true });
        table.AddColumn(new TableColumn("[dim]Artist[/]"));
        table.AddColumn(new TableColumn("[dim]Title[/]"));
        table.AddColumn(new TableColumn("[dim]BPM[/]") { Alignment = Justify.Right, NoWrap = true });
        table.AddColumn(new TableColumn("[dim]Key[/]") { NoWrap = true });
        table.AddColumn(new TableColumn("[dim]Compat[/]") { Alignment = Justify.Right, NoWrap = true });
        table.AddColumn(new TableColumn("[dim]Playlists[/]") { Alignment = Justify.Right, NoWrap = true });

        for (var i = 0; i < results.Count; i++)
        {
            var (t, compat, coOccur, _) = results[i];
            table.AddRow(
                $"[dim]{i + 1,3}[/]",
                Markup.Escape(t.Artist),
                Markup.Escape(t.Name),
                $"[yellow]{t.Bpm:F1}[/]",
                $"[yellow]{Markup.Escape(t.Key)}[/]",
                compat.HasValue ? $"[dim]{compat.Value:F3}[/]" : "[dim]—[/]",
                coOccur.HasValue ? $"[dim]{coOccur.Value}[/]" : "[dim]—[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Registers the full DJ Buddy tool surface with the Copilot SDK. Tools close over the
    /// workspace store and the shared graph task; every return is ID-first to keep the session
    /// transcript compact.
    /// </summary>
    private static async Task<CopilotSession> CreateSessionCoreAsync(
        CopilotClient copilotClient,
        WorkspaceStore store,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        int plCount,
        CancellationToken cancellationToken = default)
    {
        var lib = store.Library;

        // Declared as AIFunctionDeclaration (the base type SessionConfig.Tools expects). Each
        // AIFunctionFactory.Create returns an AIFunction, which derives from AIFunctionDeclaration,
        // so items add directly — a List<AIFunction> can't be cast to ICollection<AIFunctionDeclaration>
        // (generic collections are invariant), which previously threw at session creation.
        var tools = new List<AIFunctionDeclaration>
        {
            // ── Library search / projection ─────────────────────────────────────────

            AIFunctionFactory.Create(
                (string query, string? genre, string? key, string? minBpm, string? maxBpm, string? sortBy, string? limit) =>
                    LibraryTools.Search(lib, query, genre, key, minBpm, maxBpm, sortBy, limit),
                "search",
                "Search the library and return matching TrackIDs (IDs only). Follow up with get_tracks to see field values. Omit optional parameters you don't need — don't pass empty strings or 'null'. limit default 50, max 500."),

            AIFunctionFactory.Create(
                (string[] trackIds, string[] fields) =>
                    LibraryTools.GetTracks(lib, trackIds, fields),
                "get_tracks",
                "Project chosen fields for a set of TrackIDs. Supported fields: name, artist, album, genre, bpm, key, tonality, rating, playCount, totalTime, dateAdded, label, remixer, year, kind, bitRate, comments. Pick only the fields you'll display."),

            AIFunctionFactory.Create(
                (string playlistName) => LibraryTools.LibraryPlaylistIds(lib, playlistName),
                "library_playlist_ids",
                "Returns TrackIDs for a named library playlist. Use get_tracks afterward to see details."),

            AIFunctionFactory.Create(
                () => LibraryTools.ListLibraryPlaylists(lib),
                "list_library_playlists",
                "List all library playlists with name and count (no tracks)."),

            AIFunctionFactory.Create(
                () => LibraryTools.LibraryStats(lib),
                "library_stats",
                "Summary scalars about the library: track count, playlist count, BPM range, distinct artist/genre/key counts."),

            AIFunctionFactory.Create(
                (string dimension, string? top) => LibraryTools.LibraryDistribution(lib, dimension, top),
                "library_distribution",
                "Top-N distribution for one of: artist, genre, key. Use when the user asks for breakdowns. top default 10."),

            // ── Workspace CRUD and set operations ───────────────────────────────────

            AIFunctionFactory.Create(
                (string name) => WorkspaceTools.CreateWorkspace(store, name),
                "create_workspace",
                "Create a new empty named workspace (a TrackSet in app memory). Returns name, count, ordered."),

            AIFunctionFactory.Create(
                (string name) => WorkspaceTools.DeleteWorkspace(store, name),
                "delete_workspace",
                "Delete a workspace by name."),

            AIFunctionFactory.Create(
                (string oldName, string newName) => WorkspaceTools.RenameWorkspace(store, oldName, newName),
                "rename_workspace",
                "Rename a workspace."),

            AIFunctionFactory.Create(
                () => WorkspaceTools.ListWorkspaces(store),
                "list_workspaces",
                "List all workspaces with name, count, and ordered flag."),

            AIFunctionFactory.Create(
                (string name, string? topArtists, string? topKeys) =>
                    WorkspaceTools.DescribeWorkspace(store, name, topArtists, topKeys),
                "describe_workspace",
                "Workspace aggregates: count, ordered flag, BPM range, optional top-N artists/keys. Pass topArtists / topKeys as small integers to include breakdowns."),

            AIFunctionFactory.Create(
                (string name, string? offset, string? limit) =>
                    WorkspaceTools.WorkspaceIds(store, name, offset, limit),
                "workspace_ids",
                "Paged TrackIDs from a workspace. Use get_tracks after to see details. limit default 200."),

            AIFunctionFactory.Create(
                (string name, string mode, string query, string? playlistFilter, string? key, string? minBpm, string? maxBpm, string? limit) =>
                    WorkspaceTools.WorkspaceFromSearch(store, name, mode, query, playlistFilter, key, minBpm, maxBpm, limit),
                "workspace_from_search",
                "Run a library search and fold results into a workspace. mode: replace | union | intersect | except. Optional playlistFilter scopes by playlist name (substring, case-insensitive) — use as a quasi-genre when the rekordbox Genre tag is unreliable. Creates the workspace if missing."),

            AIFunctionFactory.Create(
                (string name, string mode, string[] trackIds) =>
                    WorkspaceTools.WorkspaceFromIds(store, name, mode, trackIds),
                "workspace_from_ids",
                "Fold an explicit list of TrackIDs into a workspace. mode: replace | union | intersect | except."),

            AIFunctionFactory.Create(
                (string name, string mode, string playlistName) =>
                    WorkspaceTools.WorkspaceFromLibraryPlaylist(store, name, mode, playlistName),
                "workspace_from_library_playlist",
                "Fold a named library playlist's TrackIDs into a workspace. mode: replace | union | intersect | except."),

            AIFunctionFactory.Create(
                (string target, string op, string source) =>
                    WorkspaceTools.WorkspaceOp(store, target, op, source),
                "workspace_op",
                "Combine two workspaces: target = target <op> source. op: union | intersect | except | replace."),

            // ── Ordering + export ───────────────────────────────────────────────────

            AIFunctionFactory.Create(
                (string name, string? seedTrackId, string? targetCount) =>
                    WorkspaceTools.OrderWorkspace(store, graphTask, name, seedTrackId, targetCount),
                "order_workspace",
                "Turn a workspace into an ordered DJ set using the compatibility graph. Picks strategy by size (exact for tiny, heuristic for larger). Optional targetCount caps the output (workspace shrinks in place to N tracks) — pass when the user asked for an explicit size, omit for 'all of X' requests. Returns the ordered TrackIDs and the strategy used."),

            AIFunctionFactory.Create(
                (string name, string count, string? by) =>
                    WorkspaceTools.TrimWorkspace(store, name, count, by),
                "trim_workspace",
                "Shrink a workspace to its top N tracks ranked by 'by': rating (default), recent (DateAdded desc), playCount, or random. Use as a pre-ordering cull when the user wants the 'best N' rather than a BPM-prefix slice."),

            AIFunctionFactory.Create(
                (string workspaceName, string? playlistName) =>
                    WorkspaceTools.CommitWorkspaceAsPlaylist(store, workspaceName, playlistName),
                "commit_workspace_as_playlist",
                "Commit a workspace into the DJ_BUDDY folder as a playlist. Tell the user to run /export after to write rekordbox.xml."),

            // ── Display ─────────────────────────────────────────────────────────────

            AIFunctionFactory.Create(
                (string[] trackIds, string[]? fields, string? title) =>
                    DisplayTools.DisplayTracks(lib, trackIds, fields, title),
                "display_tracks",
                "Render a list of TrackIDs as a Spectre table directly to the user's console. THIS is what the user sees — call it instead of pasting tracks into your reply. Default columns: artist, name, bpm, key. Pass fields=[\"artist\",\"name\",\"bpm\",\"key\",\"rating\"] to add columns. Pass a short title to caption the table. Returns { ok, displayed, missingIds } only — the rendered data is NOT echoed back."),

            // ── Graph traversal ─────────────────────────────────────────────────────

            AIFunctionFactory.Create(
                (string trackId, string? key, string? genre, string? minBpm, string? maxBpm, string? limit) =>
                    GraphTools.SuggestNextTrack(lib, graphTask, trackId, key, genre, minBpm, maxBpm, limit),
                "suggest_next_track",
                "Given a TrackID, return compatible next-track candidates (IDs + edge metadata only) sorted by transition quality. Use against the whole library."),

            AIFunctionFactory.Create(
                (string workspaceName, string trackId, string? limit) =>
                    GraphTools.SuggestNextInWorkspace(store, graphTask, workspaceName, trackId, limit),
                "suggest_next_in_workspace",
                "Next-track candidates restricted to a named workspace. Use when helping the user build a set from a curated subset."),

            AIFunctionFactory.Create(
                (string trackId, string? limit) =>
                    GraphTools.FindSimilarTracks(lib, graphTask, trackId, limit),
                "find_similar_tracks",
                "Tracks similar to a source by harmonic compatibility and/or playlist co-occurrence. Returns IDs plus evidence components."),
        };

        return await copilotClient.CreateSessionAsync(new SessionConfig
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
        }, cancellationToken);
    }

    /// <summary>
    /// Patches the source rekordbox.xml with the agent's DJ_BUDDY folder and writes the result
    /// to <paramref name="outputPath"/>. Creates a <c>.bak</c> backup first when overwriting
    /// the source file in-place.
    /// </summary>
    private static async Task HandleExportAsync(
        WorkspaceStore store, string sourceXmlPath, string outputPath)
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
    /// </summary>
    private static string FormatToolArgs(object? arguments)
    {
        if (arguments is null)
            return "";

        if (arguments is IDictionary<string, object?> dict)
        {
            return string.Join(", ", dict
                .Where(kv => kv.Value is not null)
                .Select(kv => $"{kv.Key}: {FormatArgValue(kv.Value!)}"));
        }

        if (arguments is System.Collections.IDictionary legacyDict)
        {
            var pairs = new List<string>();
            foreach (System.Collections.DictionaryEntry entry in legacyDict)
            {
                if (entry.Value is not null)
                    pairs.Add($"{entry.Key}: {FormatArgValue(entry.Value)}");
            }

            return string.Join(", ", pairs);
        }

        var json = System.Text.Json.JsonSerializer.Serialize(arguments);
        return json.Trim('{', '}').Trim();
    }

    /// <summary>
    /// Renders an argument value compactly. Large arrays (e.g. trackIds) are truncated to
    /// length only so the debug line stays readable.
    /// </summary>
    private static string FormatArgValue(object value)
    {
        if (value is string s) return s;
        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var items = enumerable.Cast<object?>().ToList();
            if (items.Count > 5)
                return $"[{items.Count} items]";
            return "[" + string.Join(",", items.Select(i => i?.ToString() ?? "")) + "]";
        }
        return value.ToString() ?? "";
    }
}
