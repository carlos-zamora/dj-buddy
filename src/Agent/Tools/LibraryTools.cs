using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Static methods that implement the Copilot SDK tools for querying a rekordbox library.
/// Each method returns plain objects that <c>AIFunctionFactory</c> serializes to JSON.
/// </summary>
internal static class LibraryTools
{
    /// <summary>
    /// Searches tracks by name/artist with optional filters for genre, key, and BPM range.
    /// </summary>
    public static object SearchTracks(
        RekordboxLibrary library,
        string query,
        string? genre = null,
        string? key = null,
        double? minBpm = null,
        double? maxBpm = null,
        string? sortBy = null,
        int? limit = null)
    {
        IEnumerable<Track> results = library.Tracks.Values;

        if (!string.IsNullOrWhiteSpace(query))
            results = results.Search(query, TrackSearchFields.All);

        if (!string.IsNullOrWhiteSpace(genre))
            results = results.Where(t => t.Genre.Contains(genre, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(key))
            results = results.WhereKey(key);

        if (minBpm.HasValue || maxBpm.HasValue)
            results = results.WhereBpmBetween(minBpm ?? 0, maxBpm ?? 999);

        var sortKey = TrackSortKey.Title;
        if (!string.IsNullOrWhiteSpace(sortBy) &&
            Enum.TryParse<TrackSortKey>(sortBy, ignoreCase: true, out var parsed))
        {
            sortKey = parsed;
        }

        var cap = Math.Clamp(limit ?? 20, 1, 100);
        var list = results.OrderBy(sortKey).Take(cap).ToList();

        return new
        {
            count = list.Count,
            tracks = list.Select(ToSummary).ToList()
        };
    }

    /// <summary>
    /// Returns full metadata for a single track by its ID.
    /// </summary>
    public static object GetTrackDetails(RekordboxLibrary library, string trackId)
    {
        if (!library.Tracks.TryGetValue(trackId, out var t))
            return new { error = $"Track with ID '{trackId}' not found." };

        return new
        {
            trackId = t.TrackId,
            name = t.Name,
            artist = t.Artist,
            album = t.Album,
            genre = t.Genre,
            bpm = t.Bpm,
            key = t.Key,
            tonality = t.Tonality,
            rating = NormalizeRating(t.Rating),
            playCount = t.PlayCount,
            totalTime = FormatTime(t.TotalTime),
            dateAdded = t.DateAdded?.ToString("yyyy-MM-dd"),
            label = t.Label,
            remixer = t.Remixer,
            comments = t.Comments,
            year = t.Year,
            kind = t.Kind,
            bitRate = t.BitRate
        };
    }

    /// <summary>
    /// Lists all playlists in the library with their track counts.
    /// </summary>
    public static object ListPlaylists(RekordboxLibrary library)
    {
        var playlists = library.Root.EnumeratePlaylists()
            .Select(p => new { name = p.Name, trackCount = p.TrackKeys.Count })
            .ToList();

        return new { count = playlists.Count, playlists };
    }

    /// <summary>
    /// Returns all tracks in a specific playlist by name.
    /// </summary>
    public static object GetPlaylistTracks(RekordboxLibrary library, string playlistName)
    {
        var node = library.Root.FindByName(playlistName);
        if (node is null)
            return new { error = $"Playlist '{playlistName}' not found." };

        if (node.IsFolder)
            return new { error = $"'{playlistName}' is a folder, not a playlist. Use list_playlists to see its contents." };

        var tracks = node.GetTracks(library).Select(ToSummary).ToList();
        return new { playlistName = node.Name, count = tracks.Count, tracks };
    }

    /// <summary>
    /// Returns summary statistics about the entire library.
    /// </summary>
    public static object GetLibraryStats(RekordboxLibrary library)
    {
        var tracks = library.Tracks.Values;
        var withBpm = tracks.Where(t => t.Bpm > 0).ToList();

        var topGenres = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Genre))
            .GroupBy(t => t.Genre)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new { genre = g.Key, count = g.Count() })
            .ToList();

        var topArtists = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .GroupBy(t => t.Artist)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new { artist = g.Key, count = g.Count() })
            .ToList();

        var keyDistribution = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Key))
            .GroupBy(t => t.Key)
            .OrderBy(g => g.Key, CamelotKeyComparer.Instance)
            .Select(g => new { key = g.Key, count = g.Count() })
            .ToList();

        return new
        {
            totalTracks = library.Tracks.Count,
            totalPlaylists = library.Root.EnumeratePlaylists().Count(),
            bpmRange = withBpm.Count > 0
                ? new { min = withBpm.Min(t => t.Bpm), max = withBpm.Max(t => t.Bpm) }
                : null,
            topGenres,
            topArtists,
            keyDistribution
        };
    }

    private static object ToSummary(Track t) => new
    {
        trackId = t.TrackId,
        name = t.Name,
        artist = t.Artist,
        bpm = t.Bpm,
        key = t.Key,
        genre = t.Genre,
        rating = NormalizeRating(t.Rating),
        totalTime = FormatTime(t.TotalTime)
    };

    /// <summary>
    /// Converts the rekordbox 0-255 rating scale to 0-5 stars.
    /// </summary>
    private static int NormalizeRating(int raw) =>
        raw <= 0 ? 0 : Math.Clamp(raw / 51, 1, 5);

    /// <summary>
    /// Formats a duration in seconds as "m:ss" or "h:mm:ss".
    /// </summary>
    private static string FormatTime(int totalSeconds)
    {
        if (totalSeconds <= 0) return "0:00";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}
