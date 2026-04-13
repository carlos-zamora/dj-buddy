using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Rekordbox.Query;

/// <summary>
/// LINQ-style extension methods for searching, filtering, and sorting <see cref="Track"/> sequences.
/// All methods are lazy — nothing is materialized until the caller enumerates the result.
/// </summary>
/// <example>
/// <code>
/// var results = library.Tracks.Values
///     .Search("deadmau5 strobe")
///     .WhereKey("8A")
///     .OrderBy(TrackSortKey.Bpm);
/// </code>
/// </example>
public static class TrackQuery
{
    /// <summary>
    /// Case-insensitive substring search. <paramref name="query"/> is split on whitespace; every
    /// resulting term must match at least one of the selected <paramref name="fields"/> (AND
    /// semantics across terms, OR across fields).
    /// </summary>
    /// <param name="tracks">Source sequence.</param>
    /// <param name="query">Search query. A null or whitespace-only query returns the source unchanged.</param>
    /// <param name="fields">Which track fields to match against. Defaults to Name + Artist.</param>
    public static IEnumerable<Track> Search(
        this IEnumerable<Track> tracks,
        string? query,
        TrackSearchFields fields = TrackSearchFields.NameAndArtist)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        if (string.IsNullOrWhiteSpace(query) || fields == TrackSearchFields.None)
            return tracks;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !t.All(char.IsPunctuation))
            .ToArray();
        if (terms.Length == 0)
            return tracks;

        return tracks.Where(t => terms.All(term => MatchesAnyField(t, term, fields)));
    }

    private static bool MatchesAnyField(Track track, string term, TrackSearchFields fields)
    {
        if ((fields & TrackSearchFields.Name)     != 0 && track.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if ((fields & TrackSearchFields.Artist)   != 0 && track.Artist.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if ((fields & TrackSearchFields.Album)    != 0 && track.Album.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if ((fields & TrackSearchFields.Genre)    != 0 && track.Genre.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if ((fields & TrackSearchFields.Comments) != 0 && track.Comments.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if ((fields & TrackSearchFields.Label)    != 0 && track.Label.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Filters to tracks whose <see cref="Track.Key"/> matches <paramref name="camelotKey"/>
    /// case-insensitively. A null or empty key returns the source unchanged.
    /// </summary>
    public static IEnumerable<Track> WhereKey(this IEnumerable<Track> tracks, string? camelotKey)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (string.IsNullOrEmpty(camelotKey))
            return tracks;

        return tracks.Where(t => string.Equals(t.Key, camelotKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Filters to tracks whose BPM is in the inclusive <c>[min, max]</c> range.</summary>
    public static IEnumerable<Track> WhereBpmBetween(this IEnumerable<Track> tracks, double min, double max)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        return tracks.Where(t => t.Bpm >= min && t.Bpm <= max);
    }

    /// <summary>
    /// Orders a track sequence by the chosen <paramref name="sortKey"/>.
    /// </summary>
    /// <param name="tracks">Source sequence.</param>
    /// <param name="sortKey">Which field to sort by.</param>
    /// <param name="descending">When true, sorts in descending order.</param>
    public static IOrderedEnumerable<Track> OrderBy(
        this IEnumerable<Track> tracks,
        TrackSortKey sortKey,
        bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        return sortKey switch
        {
            TrackSortKey.Title => descending
                ? tracks.OrderByDescending(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                : tracks.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase),
            TrackSortKey.Artist => descending
                ? tracks.OrderByDescending(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
                : tracks.OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase),
            TrackSortKey.Album => descending
                ? tracks.OrderByDescending(t => t.Album, StringComparer.CurrentCultureIgnoreCase)
                : tracks.OrderBy(t => t.Album, StringComparer.CurrentCultureIgnoreCase),
            TrackSortKey.Bpm => descending
                ? tracks.OrderByDescending(t => t.Bpm)
                : tracks.OrderBy(t => t.Bpm),
            TrackSortKey.Key => descending
                ? tracks.OrderByDescending(t => t.Key, CamelotKeyComparer.Instance)
                : tracks.OrderBy(t => t.Key, CamelotKeyComparer.Instance),
            TrackSortKey.DateAdded => descending
                ? tracks.OrderByDescending(t => t.DateAdded ?? DateTime.MinValue)
                : tracks.OrderBy(t => t.DateAdded ?? DateTime.MinValue),
            TrackSortKey.Rating => descending
                ? tracks.OrderByDescending(t => t.Rating)
                : tracks.OrderBy(t => t.Rating),
            TrackSortKey.PlayCount => descending
                ? tracks.OrderByDescending(t => t.PlayCount)
                : tracks.OrderBy(t => t.PlayCount),
            _ => throw new ArgumentOutOfRangeException(nameof(sortKey), sortKey, "Unknown sort key."),
        };
    }
}
