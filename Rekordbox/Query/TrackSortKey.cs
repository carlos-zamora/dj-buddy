namespace Rekordbox.Query;

/// <summary>
/// Fields that <see cref="TrackQuery.OrderBy"/> can sort by.
/// </summary>
public enum TrackSortKey
{
    /// <summary>Sort by <c>Track.Name</c>, culture-aware, case-insensitive.</summary>
    Title,

    /// <summary>Sort by <c>Track.Artist</c>, culture-aware, case-insensitive.</summary>
    Artist,

    /// <summary>Sort by <c>Track.Album</c>, culture-aware, case-insensitive.</summary>
    Album,

    /// <summary>Sort by <c>Track.Bpm</c>.</summary>
    Bpm,

    /// <summary>
    /// Sort by <c>Track.Key</c> in Camelot order (<c>1A, 1B, 2A, 2B, …, 12A, 12B</c>),
    /// not lexicographic. Tracks without a parseable key sort to the end.
    /// </summary>
    Key,

    /// <summary>Sort by <c>Track.DateAdded</c>. Nulls sort as <see cref="System.DateTime.MinValue"/>.</summary>
    DateAdded,

    /// <summary>Sort by <c>Track.Rating</c>.</summary>
    Rating,

    /// <summary>Sort by <c>Track.PlayCount</c>.</summary>
    PlayCount,
}
