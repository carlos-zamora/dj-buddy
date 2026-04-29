using System.Text;
using DJBuddy.Agent.Tools;
using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using Spectre.Console;

namespace DJBuddy.Agent;

/// <summary>
/// Formatted console output helpers for the DJ Buddy Agent.
/// All styling uses Spectre.Console markup and styles.
/// </summary>
internal static class ConsoleUi
{
    /// <summary>
    /// Enables UTF-8 output so emojis and box-drawing characters render correctly.
    /// Call once at the very start of the program.
    /// </summary>
    public static void EnableUtf8()
    {
        Console.OutputEncoding = Encoding.UTF8;
    }

    /// <summary>
    /// Prints the startup banner with library path and stats inside a box.
    /// </summary>
    public static void PrintBanner(string xmlPath, int trackCount, int playlistCount)
    {
        var content = new StringBuilder();
        content.AppendLine("[bold]DJ Buddy Agent[/]");
        content.AppendLine();
        content.AppendLine($"📁  {Markup.Escape(xmlPath)}");
        content.AppendLine($"🎵  {trackCount:N0} tracks");
        content.AppendLine($"📂  {playlistCount:N0} playlists");

        var panel = new Panel(content.ToString())
        {
            Border = BoxBorder.Double,
            BorderStyle = Style.Parse("dim"),
            Expand = true,
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prints the welcome message shown after the banner.
    /// </summary>
    public static void PrintWelcome()
    {
        AnsiConsole.MarkupLine("  Type [bold]/help[/] for commands, or ask me anything about your library.");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prints the list of available REPL commands.
    /// </summary>
    public static void PrintHelp()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Commands[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
        {
            ShowHeaders = false,
            Border = TableBorder.None,
        };
        table.AddColumn(new TableColumn(string.Empty) { Width = 25, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));

        table.AddRow("[bold]/help[/]", "Show this help message");
        table.AddRow("[bold]/tools[/]", "Show available AI tools");
        table.AddRow("[bold]/load <path>[/]", "Load a different rekordbox.xml (resets conversation)");
        table.AddRow("[bold]/stats[/]", "Show library statistics");
        table.AddRow("[bold]/workspaces[/]", "List active workspaces (named TrackSets) in this session");
        table.AddRow("[bold]/interactive [[ws]][/]", "Offline workspace builder + graph picker (no LLM calls). Type ? inside for subcommands.");
        table.AddRow("[bold]/search <query>[/]", "Search the library and display results (works offline)");
        table.AddRow("[bold]/suggest <trackId>[/]", "Show compatible next-track candidates from the graph (works offline)");
        table.AddRow("[bold]/similar <trackId>[/]", "Show similar tracks by harmonic compat + co-occurrence (works offline)");
        table.AddRow("[bold]/reconnect[/]", "Retry connecting to GitHub Copilot after a network failure");
        table.AddRow("[bold]/export[/]", "Export DJ_BUDDY playlists into rekordbox.xml (backs up original as .bak)");
        table.AddRow("[bold]/export <path>[/]", "Export DJ_BUDDY playlists to a specific output file");
        table.AddRow("[bold]/clear[/]", "Clear the screen");
        table.AddRow("[bold]/exit[/]", "Exit DJ Buddy");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]Or just type a question to chat with DJ Buddy (requires GitHub Copilot).[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Writes the input prompt glyph.
    /// </summary>
    public static void PrintPrompt()
    {
        AnsiConsole.Markup("[bold]🎧 > [/]");
    }

    /// <summary>
    /// Prints an informational status message in dim text.
    /// </summary>
    public static void PrintStatus(string message)
    {
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Prints an error message in red.
    /// </summary>
    public static void PrintError(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Prints the list of available AI tools exposed to the Copilot SDK.
    /// </summary>
    public static void PrintTools()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Available AI Tools[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
        {
            ShowHeaders = false,
            Border = TableBorder.None,
        };
        table.AddColumn(new TableColumn(string.Empty) { Width = 32, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));

        table.AddRow("[bold]search[/]", "Search library and return matching TrackIDs (IDs only). Follow up with get_tracks to see fields.");
        table.AddRow("[bold]get_tracks[/]", "Project chosen fields for a list of TrackIDs (SQL-style). The only tool that returns field values.");
        table.AddRow("[bold]library_playlist_ids[/]", "Return TrackIDs for a named library playlist.");
        table.AddRow("[bold]list_library_playlists[/]", "List all library playlists with name and count.");
        table.AddRow("[bold]library_stats[/]", "Summary scalars (count, BPM range, distinct artist/genre/key counts).");
        table.AddRow("[bold]library_distribution[/]", "Top-N distribution by artist / genre / key.");
        table.AddRow("[bold]create_workspace[/]", "Create an empty named workspace (TrackSet in app memory).");
        table.AddRow("[bold]delete_workspace[/]", "Delete a workspace by name.");
        table.AddRow("[bold]rename_workspace[/]", "Rename a workspace.");
        table.AddRow("[bold]list_workspaces[/]", "List all workspaces with count and ordered flag.");
        table.AddRow("[bold]describe_workspace[/]", "Workspace aggregates: count, BPM range, optional top artists/keys.");
        table.AddRow("[bold]workspace_ids[/]", "Paged TrackIDs from a workspace.");
        table.AddRow("[bold]workspace_from_search[/]", "Search + fold results into a workspace (mode: replace/union/intersect/except).");
        table.AddRow("[bold]workspace_from_ids[/]", "Fold explicit TrackIDs into a workspace.");
        table.AddRow("[bold]workspace_from_library_playlist[/]", "Fold a library playlist into a workspace.");
        table.AddRow("[bold]workspace_op[/]", "Combine two workspaces (target = target <op> source).");
        table.AddRow("[bold]order_workspace[/]", "Order a workspace into a DJ set via the compatibility graph (optional targetCount caps size).");
        table.AddRow("[bold]trim_workspace[/]", "Shrink to top N tracks by rating / recent / playCount / random.");
        table.AddRow("[bold]commit_workspace_as_playlist[/]", "Commit a workspace into the DJ_BUDDY folder as a playlist.");
        table.AddRow("[bold]display_tracks[/]", "Render TrackIDs as a Spectre table on the user's console (preferred over pasting in chat).");
        table.AddRow("[bold]suggest_next_track[/]", "Next-track candidates from the whole library (IDs + edge metadata).");
        table.AddRow("[bold]suggest_next_in_workspace[/]", "Next-track candidates restricted to a named workspace.");
        table.AddRow("[bold]find_similar_tracks[/]", "Similar tracks by harmonic compat and/or playlist co-occurrence.");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prints the current set of in-memory workspaces with their counts and ordered flag.
    /// </summary>
    public static void PrintWorkspaces(WorkspaceStore store)
    {
        var workspaces = store.Workspaces.ToList();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Workspaces[/]");
        AnsiConsole.WriteLine();

        if (workspaces.Count == 0)
        {
            AnsiConsole.MarkupLine("  [dim]No workspaces yet. Ask DJ Buddy to build one, e.g. \"give me a playlist of Zeds Dead and Subtronics\".[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var table = new Table()
        {
            ShowHeaders = false,
            Border = TableBorder.None,
        };
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty) { Alignment = Justify.Right });
        table.AddColumn(new TableColumn(string.Empty) { NoWrap = true });

        foreach (var ws in workspaces)
        {
            var orderedTag = ws.Ordered ? "[green]ordered[/]" : "[dim]unordered[/]";
            table.AddRow(Markup.Escape(ws.Name), $"[dim]{ws.Count,5:N0}[/]", orderedTag);
        }

        AnsiConsole.Write(table);

        var committed = store.DjBuddyFolder.Children.Count;
        if (committed > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [dim]{committed} committed playlist(s) in DJ_BUDDY folder — run /export to write rekordbox.xml.[/]");
        }
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Renders <paramref name="tracks"/> as a Spectre table with one column per requested field.
    /// Owns all formatting (column alignment, BPM right-justified, dim numerics) so callers —
    /// the agent via <c>display_tracks</c>, the offline build phase's <c>show</c> command —
    /// produce identical output.
    /// </summary>
    /// <param name="tracks">Resolved tracks to render. Caller filters out missing IDs.</param>
    /// <param name="fields">
    /// Canonical field names from <see cref="TrackFieldProjector.SupportedFields"/>. Order is
    /// preserved as column order. <c>name</c> is rendered as the title column even when listed
    /// alongside <c>artist</c>; both are routinely the first two columns.
    /// </param>
    /// <param name="title">Optional caption printed above the table in dim text.</param>
    public static void RenderTrackTable(
        IReadOnlyList<Track> tracks,
        IReadOnlyList<string> fields,
        string? title = null)
    {
        if (tracks.Count == 0)
        {
            AnsiConsole.MarkupLine("  [dim]No tracks to display.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [bold]{Markup.Escape(title)}[/]  [dim]({tracks.Count} tracks)[/]");
            AnsiConsole.WriteLine();
        }

        var table = new Table
        {
            Border = TableBorder.None,
            ShowHeaders = true,
        };

        // Index column so users can refer to picks by number from the build/pick loops.
        table.AddColumn(new TableColumn("[dim]#[/]") { Width = 4, NoWrap = true });

        foreach (var f in fields)
        {
            var header = f switch
            {
                "name" => "Title",
                "artist" => "Artist",
                "bpm" => "BPM",
                "key" => "Key",
                "totalTime" => "Time",
                "playCount" => "Plays",
                "dateAdded" => "Added",
                _ => char.ToUpperInvariant(f[0]) + f[1..],
            };

            var col = new TableColumn($"[dim]{Markup.Escape(header)}[/]") { NoWrap = f is "bpm" or "key" or "rating" };
            if (f is "bpm" or "playCount" or "year" or "bitRate")
                col.Alignment = Justify.Right;
            table.AddColumn(col);
        }

        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            var row = new List<string>(capacity: fields.Count + 1)
            {
                $"[dim]{i + 1,3}[/]",
            };

            foreach (var f in fields)
            {
                var raw = TrackFieldProjector.FormatForDisplay(t, f);
                var escaped = Markup.Escape(raw);
                row.Add(f switch
                {
                    "bpm" or "key" => $"[yellow]{escaped}[/]",
                    "rating" => $"[yellow]{escaped}[/]",
                    "playCount" or "year" or "bitRate" or "totalTime" or "dateAdded" => $"[dim]{escaped}[/]",
                    _ => escaped,
                });
            }

            table.AddRow([.. row]);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prints a formatted summary of library statistics (for the /stats command).
    /// </summary>
    public static void PrintStats(RekordboxLibrary library)
    {
        var tracks = library.Tracks.Values;
        var playlists = library.Root.EnumeratePlaylists().ToList();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]Library Statistics[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  🎵  {library.Tracks.Count:N0} tracks");
        AnsiConsole.MarkupLine($"  📂  {playlists.Count:N0} playlists");

        // BPM range
        var withBpm = tracks.Where(t => t.Bpm > 0).ToList();
        if (withBpm.Count > 0)
        {
            var minBpm = withBpm.Min(t => t.Bpm);
            var maxBpm = withBpm.Max(t => t.Bpm);
            AnsiConsole.MarkupLine($"  🥁  BPM range: {minBpm:F1} – {maxBpm:F1}");
        }

        // Top genres
        var topGenres = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Genre))
            .GroupBy(t => t.Genre)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToList();

        if (topGenres.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Top Genres[/]");

            var table = new Table()
            {
                ShowHeaders = false,
                Border = TableBorder.None,
            };
            table.AddColumn(new TableColumn(string.Empty));
            table.AddColumn(new TableColumn(string.Empty) { Alignment = Justify.Right });

            foreach (var g in topGenres)
                table.AddRow(g.Key, $"[dim]{g.Count(),5:N0}[/]");

            AnsiConsole.Write(table);
        }

        // Top artists
        var topArtists = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .GroupBy(t => t.Artist)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToList();

        if (topArtists.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Top Artists[/]");

            var table = new Table()
            {
                ShowHeaders = false,
                Border = TableBorder.None,
            };
            table.AddColumn(new TableColumn(string.Empty));
            table.AddColumn(new TableColumn(string.Empty) { Alignment = Justify.Right });

            foreach (var a in topArtists)
                table.AddRow(a.Key, $"[dim]{a.Count(),5:N0}[/]");

            AnsiConsole.Write(table);
        }

        // Key distribution
        var keys = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Key))
            .GroupBy(t => t.Key)
            .OrderBy(g => g.Key, CamelotKeyComparer.Instance)
            .ToList();

        if (keys.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold]Key Distribution[/]");

            var table = new Table()
            {
                ShowHeaders = false,
                Border = TableBorder.None,
            };
            table.AddColumn(new TableColumn(string.Empty) { Width = 6, NoWrap = true });
            table.AddColumn(new TableColumn(string.Empty) { Alignment = Justify.Right });

            foreach (var k in keys)
                table.AddRow(k.Key, $"[dim]{k.Count(),5:N0}[/]");

            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
    }
}
