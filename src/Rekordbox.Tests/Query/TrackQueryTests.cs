using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using Xunit;

namespace DJBuddy.Rekordbox.Tests.Query;

// Usage:
//   var filtered = library.Tracks.Values
//       .Search("deadmau5 strobe")
//       .WhereKey("6A")
//       .OrderBy(TrackSortKey.Bpm);
public class TrackQueryTests
{
    private static List<Track> Sample() =>
    [
        new() { TrackId = "1", Name = "Strobe",           Artist = "deadmau5", Album = "For Lack Of A Better Name", Genre = "Progressive House", Bpm = 128.0, Key = "3B", DateAdded = new DateTime(2023, 1, 1), Rating = 204, PlayCount = 50 },
        new() { TrackId = "2", Name = "Ghosts n Stuff",   Artist = "deadmau5", Album = "For Lack Of A Better Name", Genre = "Electro",           Bpm = 128.0, Key = "11B",DateAdded = new DateTime(2023, 2, 1), Rating = 153, PlayCount = 10 },
        new() { TrackId = "3", Name = "One More Time",    Artist = "Daft Punk",Album = "Discovery",                 Genre = "House",             Bpm = 123.0, Key = "6A", DateAdded = new DateTime(2022, 6, 1), Rating = 255, PlayCount = 100 },
        new() { TrackId = "4", Name = "Harder Better",    Artist = "Daft Punk",Album = "Discovery",                 Genre = "House",             Bpm = 124.0, Key = "2A", DateAdded = null,                     Rating = 102, PlayCount = 5  },
        new() { TrackId = "5", Name = "Opus",             Artist = "Eric Prydz",Album = "Opus",                      Genre = "Progressive",       Bpm = 126.0, Key = "1A", DateAdded = new DateTime(2024, 1, 1), Rating = 0,   PlayCount = 0  },
    ];

    [Fact]
    public void Search_matches_name_and_artist_by_default()
    {
        var results = Sample().Search("deadmau5").ToList();
        Assert.Equal(2, results.Count);
        Assert.All(results, t => Assert.Equal("deadmau5", t.Artist));
    }

    [Fact]
    public void Search_is_case_insensitive()
    {
        var results = Sample().Search("STROBE").ToList();
        var single = Assert.Single(results);
        Assert.Equal("Strobe", single.Name);
    }

    [Fact]
    public void Search_splits_on_whitespace_with_AND_semantics()
    {
        // Both terms must match across Name+Artist — "deadmau5 strobe" hits track 1 only.
        var results = Sample().Search("deadmau5 strobe").ToList();
        var single = Assert.Single(results);
        Assert.Equal("1", single.TrackId);
    }

    [Fact]
    public void Search_with_multi_term_AND_can_return_nothing()
    {
        // "deadmau5" matches two tracks but "discovery" matches none of them — intersection is empty.
        Assert.Empty(Sample().Search("deadmau5 discovery"));
    }

    [Fact]
    public void Search_with_empty_query_returns_all()
    {
        Assert.Equal(5, Sample().Search("").Count());
        Assert.Equal(5, Sample().Search("   ").Count());
        Assert.Equal(5, Sample().Search(null).Count());
    }

    [Fact]
    public void Search_respects_field_flags_Album()
    {
        var results = Sample().Search("Discovery", TrackSearchFields.Album).ToList();
        Assert.Equal(2, results.Count);
        Assert.All(results, t => Assert.Equal("Discovery", t.Album));
    }

    [Fact]
    public void Search_All_flag_covers_name_artist_album_genre_comments_label()
    {
        var electro = Sample().Search("Electro", TrackSearchFields.All).ToList();
        // "Electro" is a Genre on track 2 only.
        var single = Assert.Single(electro);
        Assert.Equal("2", single.TrackId);
    }

    [Fact]
    public void WhereKey_filters_case_insensitively()
    {
        Assert.Single(Sample().WhereKey("6a"));
        Assert.Single(Sample().WhereKey("6A"));
    }

    [Fact]
    public void WhereKey_with_empty_key_returns_all()
    {
        Assert.Equal(5, Sample().WhereKey(null).Count());
        Assert.Equal(5, Sample().WhereKey("").Count());
    }

    [Fact]
    public void WhereBpmBetween_is_inclusive()
    {
        var results = Sample().WhereBpmBetween(124, 128).ToList();
        // Sample BPMs: 128, 128, 123, 124, 126 -> range [124,128] keeps all except 123.
        Assert.Equal(4, results.Count);
        Assert.DoesNotContain(results, t => t.Bpm < 124 || t.Bpm > 128);
    }

    [Fact]
    public void WhereBpmBetween_bounds_are_inclusive()
    {
        // Pin exactly on the edges — both 124 and 128 must be included.
        var edge = Sample().WhereBpmBetween(124, 124).ToList();
        Assert.Single(edge);
        Assert.Equal(124.0, edge[0].Bpm);
    }

    [Fact]
    public void OrderBy_Bpm_ascending()
    {
        var ordered = Sample().OrderBy(TrackSortKey.Bpm).ToList();
        Assert.Equal(new[] { "3", "4", "5", "1", "2" }, ordered.Select(t => t.TrackId));
    }

    [Fact]
    public void OrderBy_Bpm_descending()
    {
        var ordered = Sample().OrderBy(TrackSortKey.Bpm, descending: true).ToList();
        Assert.Equal(128.0, ordered[0].Bpm);
        Assert.Equal(123.0, ordered[^1].Bpm);
    }

    [Fact]
    public void OrderBy_Key_uses_Camelot_numeric_order_not_lexicographic()
    {
        var ordered = Sample().OrderBy(TrackSortKey.Key).ToList();
        // Expected Camelot order: 1A, 2A, 3B, 6A, 11B
        Assert.Equal(new[] { "1A", "2A", "3B", "6A", "11B" }, ordered.Select(t => t.Key));
        // Naive lexicographic would put "11B" between "1A" and "2A" — this test catches that regression.
    }

    [Fact]
    public void OrderBy_DateAdded_sorts_nulls_as_MinValue()
    {
        var asc = Sample().OrderBy(TrackSortKey.DateAdded).ToList();
        // Track 4 has null DateAdded -> treated as MinValue -> sorts first.
        Assert.Equal("4", asc[0].TrackId);
    }

    [Fact]
    public void OrderBy_Rating_and_PlayCount()
    {
        var byRating = Sample().OrderBy(TrackSortKey.Rating, descending: true).First();
        Assert.Equal("3", byRating.TrackId); // rating 255

        var byPlays = Sample().OrderBy(TrackSortKey.PlayCount, descending: true).First();
        Assert.Equal("3", byPlays.TrackId); // plays 100
    }

    [Fact]
    public void Chain_composes_as_LINQ()
    {
        var results = Sample()
            .Search("deadmau5")
            .WhereBpmBetween(127, 129)
            .OrderBy(TrackSortKey.Title)
            .ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("Ghosts n Stuff", results[0].Name);
        Assert.Equal("Strobe", results[1].Name);
    }
}
