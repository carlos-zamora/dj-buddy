using DJBuddy.Agent;
using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using Xunit;

namespace DJBuddy.Agent.Tests;

/// <summary>
/// Tests for <see cref="InteractiveSession.ParseTokens"/> and key workspace dispatch helpers.
/// </summary>
public class InteractiveParserTests
{
    // ── ParseTokens ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseTokens_splits_bare_words()
    {
        var tokens = InteractiveSession.ParseTokens("add zeds dead");
        Assert.Equal(["add", "zeds", "dead"], tokens);
    }

    [Fact]
    public void ParseTokens_empty_input_returns_empty_array()
    {
        Assert.Empty(InteractiveSession.ParseTokens(""));
        Assert.Empty(InteractiveSession.ParseTokens("   "));
    }

    [Fact]
    public void ParseTokens_quoted_string_is_single_token()
    {
        var tokens = InteractiveSession.ParseTokens("add playlist \"Dubstep Favorites\"");
        Assert.Equal(["add", "playlist", "Dubstep Favorites"], tokens);
    }

    [Fact]
    public void ParseTokens_strips_quotes_from_token()
    {
        var tokens = InteractiveSession.ParseTokens("seed \"One Who Lost\"");
        Assert.Equal(2, tokens.Length);
        Assert.Equal("One Who Lost", tokens[1]);
    }

    [Fact]
    public void ParseTokens_multiple_quoted_segments()
    {
        var tokens = InteractiveSession.ParseTokens("filter \"hello world\" and \"foo bar\"");
        Assert.Equal(["filter", "hello world", "and", "foo bar"], tokens);
    }

    [Fact]
    public void ParseTokens_unterminated_quote_includes_rest_of_line()
    {
        var tokens = InteractiveSession.ParseTokens("add \"open ended");
        Assert.Equal(["add", "open ended"], tokens);
    }

    [Fact]
    public void ParseTokens_single_token_no_spaces()
    {
        var tokens = InteractiveSession.ParseTokens("help");
        Assert.Equal(["help"], tokens);
    }

    [Fact]
    public void ParseTokens_collapses_extra_spaces()
    {
        var tokens = InteractiveSession.ParseTokens("filter  key  8A");
        Assert.Equal(["filter", "key", "8A"], tokens);
    }

    // ── Workspace dispatch ────────────────────────────────────────────────────────────────────

    private static (WorkspaceStore Store, TrackSet Ws) MakeStore(params (string id, string name, string artist)[] tracks)
    {
        var lib = new RekordboxLibrary();
        foreach (var (id, name, artist) in tracks)
            lib.Tracks[id] = new Track { TrackId = id, Name = name, Artist = artist };

        var store = new WorkspaceStore(lib);
        var ws = store.GetOrCreate("test");
        return (store, ws);
    }

    [Fact]
    public void Add_query_unions_results_into_workspace()
    {
        var (store, ws) = MakeStore(("1", "Clarity", "Zedd"), ("2", "Animals", "Martin Garrix"));
        ws.Apply(TrackSetMode.Replace, []);

        // Simulate the "add zedd" dispatch
        var result = DJBuddy.Agent.Tools.WorkspaceTools.WorkspaceFromSearch(
            store, "test", "union", "zedd",
            playlistFilter: null, key: null, minBpm: null, maxBpm: null, limit: "5000");

        Assert.Equal(1, ws.Count);
        Assert.True(ws.Contains("1"));
    }

    [Fact]
    public void Filter_query_intersects_workspace()
    {
        var (store, ws) = MakeStore(("1", "Clarity", "Zedd"), ("2", "Animals", "Martin Garrix"));
        ws.Apply(TrackSetMode.Union, ["1", "2"]);

        // Simulate "filter zedd"
        DJBuddy.Agent.Tools.WorkspaceTools.WorkspaceFromSearch(
            store, "test", "intersect", "zedd",
            playlistFilter: null, key: null, minBpm: null, maxBpm: null, limit: "5000");

        Assert.Equal(1, ws.Count);
        Assert.True(ws.Contains("1"));
        Assert.False(ws.Contains("2"));
    }

    [Fact]
    public void Remove_query_excepts_matching_tracks()
    {
        var (store, ws) = MakeStore(("1", "Clarity", "Zedd"), ("2", "Animals", "Martin Garrix"));
        ws.Apply(TrackSetMode.Union, ["1", "2"]);

        // Simulate "remove zedd"
        DJBuddy.Agent.Tools.WorkspaceTools.WorkspaceFromSearch(
            store, "test", "except", "zedd",
            playlistFilter: null, key: null, minBpm: null, maxBpm: null, limit: "5000");

        Assert.Equal(1, ws.Count);
        Assert.False(ws.Contains("1"));
        Assert.True(ws.Contains("2"));
    }

    [Fact]
    public void Take_shrinks_workspace_to_n()
    {
        var (store, ws) = MakeStore(
            ("1", "A", "X"), ("2", "B", "X"), ("3", "C", "X"), ("4", "D", "X"), ("5", "E", "X"));
        ws.Apply(TrackSetMode.Union, ["1", "2", "3", "4", "5"]);

        DJBuddy.Agent.Tools.WorkspaceTools.TrimWorkspace(store, "test", "3", by: null);

        Assert.Equal(3, ws.Count);
    }

    [Fact]
    public void Undo_restores_previous_workspace_state()
    {
        var (store, ws) = MakeStore(("1", "A", "X"), ("2", "B", "X"), ("3", "C", "X"));
        ws.Apply(TrackSetMode.Union, ["1", "2"]);

        // Simulate undo: snapshot before mutation, then mutate, then restore
        var snapshot = ws.Ids.ToArray();
        ws.Apply(TrackSetMode.Union, ["3"]);
        Assert.Equal(3, ws.Count);

        ws.Apply(TrackSetMode.Replace, snapshot);
        Assert.Equal(2, ws.Count);
        Assert.False(ws.Contains("3"));
    }
}
