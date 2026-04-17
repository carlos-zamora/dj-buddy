using System.Text;
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
        table.AddColumn(new TableColumn(string.Empty) { Width = 20, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));

        table.AddRow("[bold]/help[/]", "Show this help message");
        table.AddRow("[bold]/tools[/]", "Show available AI tools");
        table.AddRow("[bold]/load <path>[/]", "Load a different rekordbox.xml (resets conversation)");
        table.AddRow("[bold]/stats[/]", "Show library statistics");
        table.AddRow("[bold]/export[/]", "Export DJ_BUDDY playlists into rekordbox.xml (backs up original as .bak)");
        table.AddRow("[bold]/export <path>[/]", "Export DJ_BUDDY playlists to a specific output file");
        table.AddRow("[bold]/clear[/]", "Clear the screen");
        table.AddRow("[bold]/exit[/]", "Exit DJ Buddy");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]Or just type a question to chat with DJ Buddy.[/]");
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
        table.AddColumn(new TableColumn(string.Empty) { Width = 26, NoWrap = true });
        table.AddColumn(new TableColumn(string.Empty));

        table.AddRow("[bold]search_tracks[/]", "Search tracks by name/artist with optional filters for genre, BPM range, and musical key (Camelot). Returns up to limit results (default 20).");
        table.AddRow("[bold]get_track_details[/]", "Get full metadata for a specific track by its track ID.");
        table.AddRow("[bold]list_playlists[/]", "List all playlists in the library with their track counts.");
        table.AddRow("[bold]get_playlist_tracks[/]", "Get all tracks in a specific playlist by name.");
        table.AddRow("[bold]get_library_stats[/]", "Get summary statistics about the library: total tracks, artist/key distribution, BPM range.");
        table.AddRow("[bold]create_playlist[/]", "Create a new named playlist in the agent's DJ_BUDDY folder. The playlist name must be unique and non-empty.");
        table.AddRow("[bold]add_track_to_playlist[/]", "Add a track to a named agent playlist by its track ID.");
        table.AddRow("[bold]remove_track_from_playlist[/]", "Remove a track from a named agent playlist.");
        table.AddRow("[bold]list_agent_playlists[/]", "List all agent-created playlists with their track counts and full track details.");
        table.AddRow("[bold]suggest_next_track[/]", "Given a current track ID, return compatible next-track candidates sorted by transition quality. Optional filters: key, genre, minBpm, maxBpm, limit (default 10).");
        table.AddRow("[bold]find_similar_tracks[/]", "Given a track ID, return tracks that are harmonically compatible and/or frequently co-occur in playlists. Optional limit (default 10).");

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
