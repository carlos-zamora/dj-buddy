using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using Xunit;

namespace DJBuddy.Rekordbox.Tests.Query;

/// <summary>
/// Invariants for <see cref="TrackSet"/>: set-op folding, union/intersect/except,
/// and the ordered-vs-unordered contract.
/// </summary>
public class TrackSetTests
{
    private static RekordboxLibrary MakeLibrary(params string[] ids)
    {
        var lib = new RekordboxLibrary();
        foreach (var id in ids)
            lib.Tracks[id] = new Track { TrackId = id, Name = $"T{id}" };
        return lib;
    }

    [Fact]
    public void Apply_Replace_discards_existing_membership()
    {
        var lib = MakeLibrary("1", "2", "3", "4");
        var ws = new TrackSet("w", lib, ["1", "2"]);

        ws.Apply(TrackSetMode.Replace, ["3", "4"]);

        Assert.Equal(2, ws.Count);
        Assert.True(ws.Contains("3"));
        Assert.True(ws.Contains("4"));
        Assert.False(ws.Contains("1"));
    }

    [Fact]
    public void Apply_Union_adds_without_removing()
    {
        var lib = MakeLibrary("1", "2", "3");
        var ws = new TrackSet("w", lib, ["1", "2"]);

        ws.Apply(TrackSetMode.Union, ["2", "3"]);

        Assert.Equal(3, ws.Count);
        Assert.True(ws.Contains("1"));
        Assert.True(ws.Contains("3"));
    }

    [Fact]
    public void Apply_Intersect_keeps_only_common_ids()
    {
        var lib = MakeLibrary("1", "2", "3");
        var ws = new TrackSet("w", lib, ["1", "2", "3"]);

        ws.Apply(TrackSetMode.Intersect, ["2", "3", "4"]);

        Assert.Equal(2, ws.Count);
        Assert.True(ws.Contains("2"));
        Assert.True(ws.Contains("3"));
        Assert.False(ws.Contains("1"));
    }

    [Fact]
    public void Apply_Except_removes_matching_ids()
    {
        var lib = MakeLibrary("1", "2", "3");
        var ws = new TrackSet("w", lib, ["1", "2", "3"]);

        ws.Apply(TrackSetMode.Except, ["2"]);

        Assert.Equal(2, ws.Count);
        Assert.False(ws.Contains("2"));
    }

    [Fact]
    public void Apply_clears_ordering()
    {
        var lib = MakeLibrary("1", "2", "3");
        var ws = new TrackSet("w", lib);
        ws.SetOrder(["1", "2"]);
        Assert.True(ws.Ordered);

        ws.Apply(TrackSetMode.Union, ["3"]);

        Assert.False(ws.Ordered);
        Assert.Null(ws.OrderedIds);
    }

    [Fact]
    public void SetOrder_deduplicates_and_preserves_first_occurrence()
    {
        var lib = MakeLibrary("1", "2", "3");
        var ws = new TrackSet("w", lib);

        ws.SetOrder(["2", "1", "2", "3", "1"]);

        Assert.True(ws.Ordered);
        Assert.Equal(new[] { "2", "1", "3" }, ws.OrderedIds!.ToArray());
        Assert.Equal(3, ws.Count);
    }

    [Fact]
    public void Ids_yields_ordered_sequence_when_ordered()
    {
        var lib = MakeLibrary("1", "2", "3");
        var ws = new TrackSet("w", lib, ["1", "2", "3"]);

        ws.SetOrder(["3", "1", "2"]);

        Assert.Equal(new[] { "3", "1", "2" }, ws.Ids.ToArray());
    }

    [Fact]
    public void ClearOrder_keeps_membership_but_drops_sequence()
    {
        var lib = MakeLibrary("1", "2");
        var ws = new TrackSet("w", lib);
        ws.SetOrder(["1", "2"]);

        ws.ClearOrder();

        Assert.False(ws.Ordered);
        Assert.Equal(2, ws.Count);
        Assert.True(ws.Contains("1"));
    }

    [Fact]
    public void Union_Intersect_Except_produce_new_unordered_set()
    {
        var lib = MakeLibrary("1", "2", "3", "4");
        var a = new TrackSet("a", lib, ["1", "2", "3"]);
        var b = new TrackSet("b", lib, ["2", "3", "4"]);

        var u = a.Union(b, "u");
        var i = a.Intersect(b, "i");
        var e = a.Except(b, "e");

        Assert.Equal(new[] { "1", "2", "3", "4" }.OrderBy(x => x), u.Ids.OrderBy(x => x));
        Assert.Equal(new[] { "2", "3" }.OrderBy(x => x), i.Ids.OrderBy(x => x));
        Assert.Equal(new[] { "1" }, e.Ids.ToArray());
        Assert.False(u.Ordered);
    }

    [Fact]
    public void Set_ops_reject_mismatched_libraries()
    {
        var libA = MakeLibrary("1");
        var libB = MakeLibrary("1");
        var a = new TrackSet("a", libA, ["1"]);
        var b = new TrackSet("b", libB, ["1"]);

        Assert.Throws<InvalidOperationException>(() => a.Union(b, "x"));
    }

    [Fact]
    public void Tracks_resolves_via_parent_library_and_skips_missing_ids()
    {
        var lib = MakeLibrary("1", "2");
        var ws = new TrackSet("w", lib, ["1", "ghost", "2"]);

        var names = ws.Tracks.Select(t => t.Name).ToList();

        Assert.Equal(2, names.Count);
        Assert.Contains("T1", names);
        Assert.Contains("T2", names);
    }
}
