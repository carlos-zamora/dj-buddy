using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Xml;
using Xunit;

namespace DJBuddy.Rekordbox.Tests.Graph;

public class TrackGraphBuilderTests
{
    private static Track MakeTrack(string id, string key, double bpm, int rating = 0, DateTime? dateAdded = null)
        => new()
        {
            TrackId = id,
            Name = $"Track {id}",
            Key = key,
            Bpm = bpm,
            Rating = rating,
            DateAdded = dateAdded,
        };

    private static RekordboxLibrary LibraryOf(params Track[] tracks)
    {
        var lib = new RekordboxLibrary();
        foreach (var t in tracks) lib.Tracks[t.TrackId] = t;
        return lib;
    }

    [Fact]
    public void Same_key_and_bpm_emits_symmetric_compatibility_edges()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "6A", 128);
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b), new TrackGraphOptions { IncludeCoOccurrenceEdges = false });

        var ab = graph.OutEdges(a).OfType<CompatibilityEdge>().ToList();
        var ba = graph.OutEdges(b).OfType<CompatibilityEdge>().ToList();
        Assert.Single(ab);
        Assert.Single(ba);
        Assert.Equal(HarmonicRelation.Same, ab[0].Relation);
        Assert.Equal(HarmonicRelation.Same, ba[0].Relation);
        Assert.Equal(BpmTier.Same, ab[0].Tier);
    }

    [Fact]
    public void Adjacent_key_emits_symmetric_adjacent_edges()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "7A", 128);
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b), new TrackGraphOptions { IncludeCoOccurrenceEdges = false });

        var ab = Assert.Single(graph.OutEdges(a).OfType<CompatibilityEdge>());
        var ba = Assert.Single(graph.OutEdges(b).OfType<CompatibilityEdge>());
        Assert.Equal(HarmonicRelation.Adjacent, ab.Relation);
        Assert.Equal(HarmonicRelation.Adjacent, ba.Relation);
    }

    [Fact]
    public void Relative_major_minor_is_adjacent_when_enabled()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "6B", 128);
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b), new TrackGraphOptions { IncludeCoOccurrenceEdges = false });

        var ab = Assert.Single(graph.OutEdges(a).OfType<CompatibilityEdge>());
        Assert.Equal(HarmonicRelation.Adjacent, ab.Relation);
    }

    [Fact]
    public void Relative_major_minor_is_omitted_when_disabled()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "6B", 128);
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b),
            new TrackGraphOptions { IncludeCoOccurrenceEdges = false, AllowRelativeMajorMinor = false });

        Assert.Empty(graph.OutEdges(a).OfType<CompatibilityEdge>());
    }

    [Fact]
    public void Energy_boost_forward_pairs_with_drop_reverse()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "8A", 128);
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b), new TrackGraphOptions { IncludeCoOccurrenceEdges = false });

        var ab = Assert.Single(graph.OutEdges(a).OfType<CompatibilityEdge>());
        var ba = Assert.Single(graph.OutEdges(b).OfType<CompatibilityEdge>());
        Assert.Equal(HarmonicRelation.EnergyBoost, ab.Relation);
        Assert.Equal(HarmonicRelation.EnergyDrop, ba.Relation);
    }

    [Fact]
    public void Far_keys_produce_no_edge()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "12A", 128); // 6 steps away — not Same/Adjacent/±2
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b), new TrackGraphOptions { IncludeCoOccurrenceEdges = false });

        Assert.Empty(graph.OutEdges(a).OfType<CompatibilityEdge>());
    }

    [Fact]
    public void Bpm_outside_tolerance_disconnects()
    {
        var a = MakeTrack("a", "6A", 100);
        var b = MakeTrack("b", "6A", 130); // ~23% apart, beyond Far threshold — but also outside half-time range
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b),
            new TrackGraphOptions { IncludeCoOccurrenceEdges = false, AllowHalfTimeMatch = false });

        Assert.Empty(graph.OutEdges(a).OfType<CompatibilityEdge>());
    }

    [Fact]
    public void Half_time_match_connects_doubled_bpm()
    {
        var a = MakeTrack("a", "6A", 96);
        var b = MakeTrack("b", "6A", 192);
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b), new TrackGraphOptions { IncludeCoOccurrenceEdges = false });

        var ab = Assert.Single(graph.OutEdges(a).OfType<CompatibilityEdge>());
        Assert.True(ab.IsHalfTimeMatch);
        Assert.Equal(BpmTier.Same, ab.Tier);
    }

    [Fact]
    public void Half_time_match_omitted_when_disabled()
    {
        var a = MakeTrack("a", "6A", 96);
        var b = MakeTrack("b", "6A", 192);
        var graph = TrackGraphBuilder.Build(LibraryOf(a, b),
            new TrackGraphOptions { IncludeCoOccurrenceEdges = false, AllowHalfTimeMatch = false });

        Assert.Empty(graph.OutEdges(a).OfType<CompatibilityEdge>());
    }

    [Fact]
    public void Co_occurrence_counts_shared_playlists()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "6A", 128);
        var lib = LibraryOf(a, b);
        lib.Root.Children.Add(new PlaylistNode { Name = "P1", IsFolder = false, TrackKeys = ["a", "b"] });
        lib.Root.Children.Add(new PlaylistNode { Name = "P2", IsFolder = false, TrackKeys = ["a", "b"] });

        var graph = TrackGraphBuilder.Build(lib, new TrackGraphOptions { IncludeCompatibilityEdges = false });

        var ab = Assert.Single(graph.OutEdges(a).OfType<CoOccurrenceEdge>());
        var ba = Assert.Single(graph.OutEdges(b).OfType<CoOccurrenceEdge>());
        Assert.Equal(2, ab.PlaylistCount);
        Assert.Equal(2, ba.PlaylistCount);
    }

    [Fact]
    public void Co_occurrence_ignored_when_tracks_never_share_playlist()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "6A", 128);
        var lib = LibraryOf(a, b);
        lib.Root.Children.Add(new PlaylistNode { Name = "P1", IsFolder = false, TrackKeys = ["a"] });
        lib.Root.Children.Add(new PlaylistNode { Name = "P2", IsFolder = false, TrackKeys = ["b"] });

        var graph = TrackGraphBuilder.Build(lib, new TrackGraphOptions { IncludeCompatibilityEdges = false });

        Assert.Empty(graph.OutEdges(a).OfType<CoOccurrenceEdge>());
    }

    [Fact]
    public void Parallel_edges_coexist_for_compatibility_and_cooccurrence()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "6A", 128);
        var lib = LibraryOf(a, b);
        lib.Root.Children.Add(new PlaylistNode { Name = "P1", IsFolder = false, TrackKeys = ["a", "b"] });

        var graph = TrackGraphBuilder.Build(lib);

        var forward = graph.OutEdges(a).ToList();
        Assert.Contains(forward, e => e is CompatibilityEdge);
        Assert.Contains(forward, e => e is CoOccurrenceEdge);
    }

    [Fact]
    public void Option_flag_disables_compatibility_edges()
    {
        var a = MakeTrack("a", "6A", 128);
        var b = MakeTrack("b", "6A", 128);
        var lib = LibraryOf(a, b);
        lib.Root.Children.Add(new PlaylistNode { Name = "P1", IsFolder = false, TrackKeys = ["a", "b"] });

        var graph = TrackGraphBuilder.Build(lib, new TrackGraphOptions { IncludeCompatibilityEdges = false });

        Assert.DoesNotContain(graph.Edges, e => e is CompatibilityEdge);
        Assert.Contains(graph.Edges, e => e is CoOccurrenceEdge);
    }

    [Fact]
    public async Task Smoke_build_against_minimal_fixture()
    {
        using var stream = TestData.OpenFixture("minimal.xml");
        var library = await RekordboxParser.ParseAsync(stream);

        var graph = TrackGraphBuilder.Build(library);

        Assert.NotEmpty(graph.Vertices);
        Assert.True(graph.VertexCount == library.Tracks.Count);
    }
}
