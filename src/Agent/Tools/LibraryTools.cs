using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Library-level Copilot SDK tools. All returns are IDs-only or scalar aggregates —
/// field-level data is fetched on demand via <see cref="GetTracks"/> so token usage
/// stays low. Every method is stateless apart from the library it closes over.
/// </summary>
internal static class LibraryTools
{
    /// <summary>
    /// Searches the library and returns matching <see cref="Track.TrackId"/>s only. Callers
    /// should follow up with <see cref="GetTracks"/> to project only the fields they need.
    /// </summary>
    /// <param name="library">The parent library.</param>
    /// <param name="query">Free-text query; matches name/artist/album/genre/comments/label.</param>
    /// <param name="genre">Optional genre substring filter.</param>
    /// <param name="key">Optional Camelot key filter (e.g. "8A").</param>
    /// <param name="minBpm">Optional inclusive lower BPM bound.</param>
    /// <param name="maxBpm">Optional inclusive upper BPM bound.</param>
    /// <param name="sortBy">Optional sort key (Title, Artist, Album, Bpm, Key, DateAdded, Rating, PlayCount).</param>
    /// <param name="limit">Optional cap; default 50, clamped to [1, 500] since this is an ID list.</param>
    public static object Search(
        RekordboxLibrary library,
        string query,
        string? genre = null,
        string? key = null,
        string? minBpm = null,
        string? maxBpm = null,
        string? sortBy = null,
        string? limit = null)
    {
        IEnumerable<Track> results = library.Tracks.Values;

        if (HasValue(query))
            results = results.Search(query, TrackSearchFields.All);

        if (HasValue(genre))
            results = results.Where(t => t.Genre.Contains(genre!, StringComparison.OrdinalIgnoreCase));

        if (HasValue(key))
            results = results.WhereKey(key!);

        var parsedMin = ParseDouble(minBpm);
        var parsedMax = ParseDouble(maxBpm);
        if (parsedMin.HasValue || parsedMax.HasValue)
            results = results.WhereBpmBetween(parsedMin ?? 0, parsedMax ?? 9999);

        var sortKey = TrackSortKey.Title;
        if (HasValue(sortBy) &&
            Enum.TryParse<TrackSortKey>(sortBy, ignoreCase: true, out var parsed))
        {
            sortKey = parsed;
        }

        // ID lists are cheap — give the agent room to work without immediately paging.
        var cap = Math.Clamp(ParseInt(limit) ?? 50, 1, 500);
        var ids = results.OrderBy(sortKey).Take(cap).Select(t => t.TrackId).ToList();

        return new { count = ids.Count, trackIds = ids };
    }

    /// <summary>
    /// Projects chosen fields for a set of track IDs. This is the only tool that exposes
    /// per-track metadata — the agent picks <paramref name="fields"/> per the task at hand.
    /// </summary>
    /// <param name="library">The parent library.</param>
    /// <param name="trackIds">Track IDs to project. Missing IDs are reported under <c>missingIds</c>.</param>
    /// <param name="fields">
    /// Field names to include, from the supported set: <c>name, artist, album, genre, bpm, key,
    /// tonality, rating, playCount, totalTime, dateAdded, label, remixer, year, kind, bitRate,
    /// comments</c>. Unknown field names are silently dropped. Case-insensitive.
    /// </param>
    public static object GetTracks(
        RekordboxLibrary library,
        IReadOnlyList<string> trackIds,
        IReadOnlyList<string> fields)
    {
        if (trackIds is null || trackIds.Count == 0)
            return new { tracks = Array.Empty<object>(), missingIds = Array.Empty<string>() };

        // Normalize and dedupe field names. Unknown ones are dropped silently — easier for the
        // agent than a hard error on a minor typo.
        var requested = TrackFieldProjector.Normalize(fields);

        var rows = new List<Dictionary<string, object?>>(trackIds.Count);
        var missing = new List<string>();

        foreach (var id in trackIds)
        {
            if (!library.Tracks.TryGetValue(id, out var t))
            {
                missing.Add(id);
                continue;
            }

            var row = new Dictionary<string, object?>(capacity: requested.Count + 1)
            {
                ["id"] = t.TrackId,
            };

            foreach (var f in requested)
                row[f] = TrackFieldProjector.Project(t, f);

            rows.Add(row);
        }

        return new { tracks = rows, missingIds = missing };
    }

    /// <summary>
    /// Returns the TrackIds of a named library playlist. IDs-only so a playlist dump costs
    /// almost no tokens.
    /// </summary>
    public static object LibraryPlaylistIds(RekordboxLibrary library, string playlistName)
    {
        var node = library.Root.FindByName(playlistName);
        if (node is null)
            return new { error = $"Playlist '{playlistName}' not found." };

        if (node.IsFolder)
            return new { error = $"'{playlistName}' is a folder, not a playlist." };

        return new { playlistName = node.Name, count = node.TrackKeys.Count, trackIds = node.TrackKeys };
    }

    /// <summary>Lists all library playlists with their names and track counts.</summary>
    public static object ListLibraryPlaylists(RekordboxLibrary library)
    {
        var playlists = library.Root.EnumeratePlaylists()
            .Select(p => new { name = p.Name, count = p.TrackKeys.Count })
            .ToList();
        return new { count = playlists.Count, playlists };
    }

    /// <summary>
    /// Returns summary scalars about the library. Does not include top-N breakdowns — callers
    /// that want those should call <see cref="LibraryDistribution"/> with an explicit dimension.
    /// </summary>
    public static object LibraryStats(RekordboxLibrary library)
    {
        var tracks = library.Tracks.Values;
        var withBpm = tracks.Where(t => t.Bpm > 0).ToList();
        var playlistCount = library.Root.EnumeratePlaylists().Count();

        return new
        {
            totalTracks = library.Tracks.Count,
            totalPlaylists = playlistCount,
            minBpm = withBpm.Count > 0 ? withBpm.Min(t => t.Bpm) : (double?)null,
            maxBpm = withBpm.Count > 0 ? withBpm.Max(t => t.Bpm) : (double?)null,
            distinctArtists = tracks
                .SelectMany(ArtistSplitter.EnumerateContributors)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            distinctGenres = tracks.Where(t => !string.IsNullOrWhiteSpace(t.Genre)).Select(t => t.Genre).Distinct().Count(),
            distinctKeys = tracks.Where(t => !string.IsNullOrWhiteSpace(t.Key)).Select(t => t.Key).Distinct().Count(),
        };
    }

    /// <summary>
    /// Returns a top-N distribution for a chosen dimension ("artist", "genre", or "key").
    /// Separated from <see cref="LibraryStats"/> so breakdowns are paid for only when asked.
    /// </summary>
    public static object LibraryDistribution(
        RekordboxLibrary library,
        string dimension,
        string? top = null)
    {
        if (!HasValue(dimension))
            return new { error = "dimension is required (one of: artist, genre, key)." };

        var cap = Math.Clamp(ParseInt(top) ?? 10, 1, 100);
        var tracks = library.Tracks.Values;

        // Artist uses ArtistSplitter so collabs get credited to each contributor; the grouping
        // is on a flat sequence of strings, not Track, so it lives in its own branch.
        if (string.Equals(dimension, "artist", StringComparison.OrdinalIgnoreCase))
        {
            var artistItems = tracks
                .SelectMany(ArtistSplitter.EnumerateContributors)
                .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(cap)
                .Select(g => new { value = g.Key, count = g.Count() })
                .ToList();
            return new { dimension, count = artistItems.Count, items = artistItems };
        }

        IEnumerable<IGrouping<string, Track>> groups = dimension.ToLowerInvariant() switch
        {
            "genre" => tracks.Where(t => !string.IsNullOrWhiteSpace(t.Genre)).GroupBy(t => t.Genre),
            "key" => tracks.Where(t => !string.IsNullOrWhiteSpace(t.Key)).GroupBy(t => t.Key),
            _ => null!,
        };

        if (groups is null)
            return new { error = $"Unknown dimension '{dimension}'. Supported: artist, genre, key." };

        // For "key", order follows the Camelot wheel when all groups are returned; top-N still
        // ranks by count since that's what users usually want for a distribution table.
        var items = groups
            .OrderByDescending(g => g.Count())
            .Take(cap)
            .Select(g => new { value = g.Key, count = g.Count() })
            .ToList();

        return new { dimension, count = items.Count, items };
    }

    /// <summary>Returns true when a string parameter carries a meaningful value.</summary>
    private static bool HasValue([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("null", StringComparison.OrdinalIgnoreCase)
        && value is not ("*" or "any" or "all");

    private static double? ParseDouble(string? value) =>
        HasValue(value) && double.TryParse(value, out var d) ? d : null;

    private static int? ParseInt(string? value) =>
        HasValue(value) && int.TryParse(value, out var i) ? i : null;
}
