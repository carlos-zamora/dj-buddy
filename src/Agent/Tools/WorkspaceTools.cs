using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using QuikGraph;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Copilot SDK tools that let the agent build, combine, order, and commit named
/// <see cref="TrackSet"/> workspaces. Workspaces live in process memory (<see cref="WorkspaceStore"/>),
/// not in the conversation — the agent references them by name and only ever sees IDs + counts
/// come back.
/// </summary>
/// <remarks>
/// <para>
/// Every method here returns a compact anonymous object designed to round-trip cheaply through the
/// Copilot tool-result channel. Track-level fields (name, artist, …) are never projected here —
/// the agent must call <c>get_tracks</c> afterward with the IDs it cares about. That split keeps
/// the LLM's visible transcript small even when a workspace contains thousands of tracks.
/// </para>
/// <para>
/// Optional parameters are typed as <c>string?</c> because the Copilot SDK surfaces nullable
/// numerics inconsistently; <see cref="ParseInt"/> / <see cref="ParseDouble"/> / <see cref="HasValue"/>
/// handle the "empty string / literal 'null' / wildcard" conventions the model sometimes emits.
/// </para>
/// </remarks>
internal static class WorkspaceTools
{
    /// <summary>Creates a new empty workspace.</summary>
    /// <param name="store">The workspace store that owns the new <see cref="TrackSet"/>.</param>
    /// <param name="name">Desired workspace name. Must be unique (case-insensitive) within the store.</param>
    /// <returns>
    /// <c>{ ok, name, count, ordered }</c> summary on success, or <c>{ ok=false, error }</c> when
    /// the name is blank or already taken.
    /// </returns>
    public static object CreateWorkspace(WorkspaceStore store, string name)
    {
        var (ok, message, ws) = store.Create(name);
        if (!ok || ws is null) return new { ok = false, error = message };
        return Summary(ws);
    }

    /// <summary>Deletes a workspace by name.</summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="name">Case-insensitive workspace name to remove.</param>
    /// <returns><c>{ ok=true, name }</c> when removed, <c>{ ok=false, error }</c> when not found.</returns>
    public static object DeleteWorkspace(WorkspaceStore store, string name) =>
        store.Delete(name)
            ? new { ok = true, name }
            : new { ok = false, error = $"Workspace '{name}' not found." };

    /// <summary>Renames a workspace.</summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="oldName">Existing workspace name.</param>
    /// <param name="newName">New name; must be non-empty and unique.</param>
    /// <returns><c>{ ok=true, name }</c> with the new name on success, otherwise <c>{ ok=false, error }</c>.</returns>
    public static object RenameWorkspace(WorkspaceStore store, string oldName, string newName)
    {
        var (ok, message) = store.Rename(oldName, newName);
        return ok
            ? new { ok = true, name = message }
            : new { ok = false, error = message };
    }

    /// <summary>Lists all workspaces with their counts and ordered status.</summary>
    /// <param name="store">The workspace store to enumerate.</param>
    /// <returns><c>{ count, workspaces: [{ name, count, ordered }] }</c>.</returns>
    public static object ListWorkspaces(WorkspaceStore store)
    {
        var items = store.Workspaces.Select(w => new { name = w.Name, count = w.Count, ordered = w.Ordered }).ToList();
        return new { count = items.Count, workspaces = items };
    }

    /// <summary>
    /// Returns aggregates about a workspace: size, ordered flag, BPM range, and (optionally)
    /// top-N artists and keys. No track-level metadata is returned.
    /// </summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="name">Workspace to describe.</param>
    /// <param name="topArtists">
    /// Optional integer (as string) requesting the top-N most frequent artists. Omit / null to
    /// skip the artist breakdown entirely — keeps the response small when the agent only needs
    /// scalar aggregates. Clamped to [1, 100].
    /// </param>
    /// <param name="topKeys">
    /// Optional integer (as string) requesting the top-N most frequent Camelot keys. Same
    /// opt-in semantics as <paramref name="topArtists"/>. Clamped to [1, 100].
    /// </param>
    /// <returns>
    /// <c>{ name, count, ordered, minBpm, maxBpm, topArtists?, topKeys? }</c>, or <c>{ error }</c>
    /// when the workspace doesn't exist.
    /// </returns>
    public static object DescribeWorkspace(
        WorkspaceStore store,
        string name,
        string? topArtists = null,
        string? topKeys = null)
    {
        var ws = store.Get(name);
        if (ws is null) return new { error = $"Workspace '{name}' not found." };

        var tracks = ws.Tracks.ToList();

        // Guard against tracks with missing BPM so min/max aren't dragged to 0 by unanalyzed files.
        var withBpm = tracks.Where(t => t.Bpm > 0).ToList();

        // topArtists/topKeys are opt-in: both default to null and only materialize when the agent
        // explicitly asks, so describe_workspace stays cheap when used as a "count + range" probe.
        List<object>? artists = null;
        if (ParseInt(topArtists) is int ac && ac > 0)
        {
            // SelectMany through ArtistSplitter so collabs ("Zeds Dead, Subtronics") and
            // bracketed flips ("[NEOTEK Flip]") are credited to each contributor.
            artists = tracks
                .SelectMany(ArtistSplitter.EnumerateContributors)
                .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(Math.Clamp(ac, 1, 100))
                .Select(g => (object)new { artist = g.Key, count = g.Count() })
                .ToList();
        }

        List<object>? keys = null;
        if (ParseInt(topKeys) is int kc && kc > 0)
        {
            keys = tracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Key))
                .GroupBy(t => t.Key)
                .OrderByDescending(g => g.Count())
                .Take(Math.Clamp(kc, 1, 100))
                .Select(g => (object)new { key = g.Key, count = g.Count() })
                .ToList();
        }

        return new
        {
            name = ws.Name,
            count = ws.Count,
            ordered = ws.Ordered,
            minBpm = withBpm.Count > 0 ? withBpm.Min(t => t.Bpm) : (double?)null,
            maxBpm = withBpm.Count > 0 ? withBpm.Max(t => t.Bpm) : (double?)null,
            topArtists = artists,
            topKeys = keys,
        };
    }

    /// <summary>Returns a paged slice of the workspace's track IDs.</summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="name">Workspace to page.</param>
    /// <param name="offset">Optional zero-based start index; defaults to 0 when null / non-numeric.</param>
    /// <param name="limit">
    /// Optional page size; default 200, clamped to [1, 1000]. The cap is deliberately tight —
    /// the agent should rarely need a full page dump since tools return IDs, not fields.
    /// </param>
    /// <returns><c>{ name, count, offset, trackIds }</c>, or <c>{ error }</c> when the workspace is missing.</returns>
    public static object WorkspaceIds(
        WorkspaceStore store,
        string name,
        string? offset = null,
        string? limit = null)
    {
        var ws = store.Get(name);
        if (ws is null) return new { error = $"Workspace '{name}' not found." };

        var skip = Math.Max(0, ParseInt(offset) ?? 0);
        var take = Math.Clamp(ParseInt(limit) ?? 200, 1, 1000);

        // ws.Ids respects the workspace's ordered/unordered state — callers get the canonical
        // sequence when the workspace has been ordered, and arbitrary hash-set order otherwise.
        var ids = ws.Ids.Skip(skip).Take(take).ToList();
        return new { name = ws.Name, count = ws.Count, offset = skip, trackIds = ids };
    }

    /// <summary>
    /// Runs a library search with the same parameter shape as <c>search</c> and folds the results
    /// into a workspace under the given <paramref name="mode"/>. Creates the workspace if it
    /// doesn't exist (except in Intersect/Except modes, where an empty source yields an empty
    /// result).
    /// </summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="name">Target workspace; created on demand.</param>
    /// <param name="mode">Fold strategy: <c>replace</c> | <c>union</c> | <c>intersect</c> | <c>except</c>.</param>
    /// <param name="query">Free-text search across name/artist/album/genre. Empty string ⇒ no text filter.</param>
    /// <param name="playlistFilter">
    /// Optional playlist-name substring (case-insensitive). When supplied, only tracks present in
    /// at least one library playlist whose name contains the substring are eligible. Used as a
    /// quasi-genre filter since the rekordbox <c>Genre</c> tag is unreliable in this library.
    /// Substring matching is intentionally narrow — "Dubstep" matches "Dubstep" / "Chill Dubstep",
    /// but "Bass" will not pull in "140". For broader genre intent, the agent should call
    /// <c>list_library_playlists</c> first and union the relevant ones via
    /// <c>workspace_from_library_playlist</c>.
    /// </param>
    /// <param name="key">Optional Camelot key filter (e.g. <c>8A</c>).</param>
    /// <param name="minBpm">Optional inclusive lower BPM bound.</param>
    /// <param name="maxBpm">Optional inclusive upper BPM bound.</param>
    /// <param name="limit">
    /// Optional cap on the number of IDs folded in; default 500, clamped to [1, 5000]. Protects
    /// the workspace from accidental "select all" with a broad query.
    /// </param>
    /// <returns>
    /// <c>{ ok, name, count, ordered }</c> on success, or <c>{ error }</c> when <paramref name="mode"/>
    /// is bad or <paramref name="playlistFilter"/> matches no playlists.
    /// </returns>
    public static object WorkspaceFromSearch(
        WorkspaceStore store,
        string name,
        string mode,
        string query,
        string? playlistFilter = null,
        string? key = null,
        string? minBpm = null,
        string? maxBpm = null,
        string? limit = null)
    {
        if (!TryParseMode(mode, out var parsedMode, out var error))
            return new { error };

        // Resolve playlist filter up front so we can short-circuit with suggestions when the
        // agent passes a name that doesn't match anything.
        HashSet<string>? allowedIds = null;
        if (HasValue(playlistFilter))
        {
            var matching = store.Library.Root.EnumeratePlaylists()
                .Where(p => p.Name.Contains(playlistFilter!, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matching.Count == 0)
            {
                var suggestions = store.Library.Root.EnumeratePlaylists()
                    .Select(p => p.Name)
                    .Take(10)
                    .ToList();
                return new
                {
                    error = $"No playlists matched '{playlistFilter}'.",
                    suggestions,
                };
            }
            allowedIds = new HashSet<string>(matching.SelectMany(p => p.TrackKeys), StringComparer.Ordinal);
        }

        // Compose filters lazily so we only walk the library once via the final Take(cap).
        IEnumerable<Track> results = store.Library.Tracks.Values;
        if (HasValue(query)) results = results.Search(query, TrackSearchFields.All);
        if (allowedIds is not null) results = results.Where(t => allowedIds.Contains(t.TrackId));
        if (HasValue(key)) results = results.WhereKey(key!);

        var min = ParseDouble(minBpm);
        var max = ParseDouble(maxBpm);
        if (min.HasValue || max.HasValue)
            results = results.WhereBpmBetween(min ?? 0, max ?? 9999);

        var cap = Math.Clamp(ParseInt(limit) ?? 500, 1, 5000);
        var ids = results.Take(cap).Select(t => t.TrackId);

        var ws = store.GetOrCreate(name);
        ws.Apply(parsedMode, ids);
        return Summary(ws);
    }

    /// <summary>Folds an explicit list of track IDs into a workspace under the given mode.</summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="name">Target workspace; created on demand.</param>
    /// <param name="mode">Fold strategy: <c>replace</c> | <c>union</c> | <c>intersect</c> | <c>except</c>.</param>
    /// <param name="trackIds">IDs to fold in. IDs not present in the library are silently dropped.</param>
    /// <returns><c>{ ok, name, count, ordered }</c> on success, or <c>{ error }</c> when <paramref name="mode"/> is bad.</returns>
    public static object WorkspaceFromIds(
        WorkspaceStore store,
        string name,
        string mode,
        IReadOnlyList<string> trackIds)
    {
        if (!TryParseMode(mode, out var parsedMode, out var error))
            return new { error };

        // Filter to valid IDs so hallucinated or stale IDs from the model can't pollute the
        // workspace. ContainsKey is O(1) on the underlying dictionary.
        var valid = trackIds is null
            ? Enumerable.Empty<string>()
            : trackIds.Where(store.Library.Tracks.ContainsKey);

        var ws = store.GetOrCreate(name);
        ws.Apply(parsedMode, valid);
        return Summary(ws);
    }

    /// <summary>Folds a library playlist's TrackIDs into a workspace under the given mode.</summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="name">Target workspace; created on demand.</param>
    /// <param name="mode">Fold strategy: <c>replace</c> | <c>union</c> | <c>intersect</c> | <c>except</c>.</param>
    /// <param name="playlistName">Name of a library playlist (not a folder) under the root node.</param>
    /// <returns>
    /// <c>{ ok, name, count, ordered }</c> on success, or <c>{ error }</c> when the mode is bad,
    /// the playlist doesn't exist, or the named node is a folder rather than a playlist.
    /// </returns>
    public static object WorkspaceFromLibraryPlaylist(
        WorkspaceStore store,
        string name,
        string mode,
        string playlistName)
    {
        if (!TryParseMode(mode, out var parsedMode, out var error))
            return new { error };

        var node = store.Library.Root.EnumeratePlaylists()
            .FirstOrDefault(p => p.Name == playlistName);
        if (node is null) return new { error = $"Playlist '{playlistName}' not found." };

        var ws = store.GetOrCreate(name);
        ws.Apply(parsedMode, node.TrackKeys);
        return Summary(ws);
    }

    /// <summary>
    /// Combines two existing workspaces: folds <paramref name="source"/> into
    /// <paramref name="target"/> under <paramref name="op"/>. Both workspaces must already exist.
    /// </summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="target">Workspace that is mutated in place.</param>
    /// <param name="op">Fold strategy: <c>replace</c> | <c>union</c> | <c>intersect</c> | <c>except</c>.</param>
    /// <param name="source">Workspace whose IDs supply the right-hand operand.</param>
    /// <returns><c>{ ok, name, count, ordered }</c> on success, or <c>{ error }</c> when either workspace is missing or the op is unknown.</returns>
    public static object WorkspaceOp(
        WorkspaceStore store,
        string target,
        string op,
        string source)
    {
        if (!TryParseMode(op, out var parsedMode, out var error))
            return new { error };

        var tws = store.Get(target);
        var sws = store.Get(source);
        // Require both sides to exist explicitly — auto-creating an empty source would silently
        // clear the target under Intersect, which is almost never what the agent intends.
        if (tws is null) return new { error = $"Workspace '{target}' not found." };
        if (sws is null) return new { error = $"Workspace '{source}' not found." };

        tws.Apply(parsedMode, sws.Ids);
        return Summary(tws);
    }

    /// <summary>
    /// Orders the workspace's tracks using the adaptive algorithm in
    /// <see cref="TrackSetOrdering"/>. Returns the strategy chosen plus the ordered ID list
    /// so the agent can confirm the set without a separate call.
    /// </summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="graphTask">
    /// Task resolving to the shared compatibility graph. Awaited here rather than requiring the
    /// caller to block — the graph is built once on a background task at startup.
    /// </param>
    /// <param name="name">Workspace to order.</param>
    /// <param name="seedTrackId">
    /// Optional starting track. When supplied, the ordered sequence begins with this ID; when
    /// null, <see cref="TrackSetOrdering"/> picks the lowest-BPM track so sets open at their
    /// coolest tempo. An ID not present in the library is silently ignored.
    /// </param>
    /// <param name="targetCount">
    /// Optional cap on the output size. When set, the workspace is truncated in place to the
    /// first N tracks of the ordered sequence after ordering runs. Use when the user asked for
    /// an explicit size (e.g. "a 20-track set"); omit for "all of X" requests where the entire
    /// workspace should be ordered. Clamped to <c>[1, ws.Count]</c>.
    /// </param>
    /// <returns>
    /// <c>{ name, count, ordered=true, strategy, trackIds }</c> on success, or <c>{ error }</c> when the
    /// workspace is missing or empty.
    /// </returns>
    public static async Task<object> OrderWorkspace(
        WorkspaceStore store,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        string name,
        string? seedTrackId = null,
        string? targetCount = null)
    {
        var ws = store.Get(name);
        if (ws is null) return new { error = $"Workspace '{name}' not found." };
        if (ws.Count == 0) return new { error = $"Workspace '{name}' is empty." };

        var graph = await graphTask.ConfigureAwait(false);

        // Resolve the optional seed ID against the library; if the agent passed a bogus ID we
        // fall through to TrackSetOrdering's default (lowest-BPM) rather than erroring out.
        Track? seed = null;
        if (HasValue(seedTrackId) && store.Library.Tracks.TryGetValue(seedTrackId!, out var s))
            seed = s;

        var tracks = ws.Tracks.ToList();
        var result = TrackSetOrdering.Order(graph, tracks, seed);

        // Truncate to the user-requested size before committing the order. The seed-first
        // prefix preserves the "open at coolest tempo" intent of the ordering algorithm.
        IEnumerable<string> finalIds = result.OrderedIds;
        if (ParseInt(targetCount) is int target && target > 0 && target < result.OrderedIds.Count)
            finalIds = result.OrderedIds.Take(target);

        ws.SetOrder(finalIds);

        return new
        {
            name = ws.Name,
            count = ws.Count,
            ordered = true,
            strategy = result.Strategy.ToString(),
            trackIds = ws.Ids.ToList(),
        };
    }

    /// <summary>
    /// Shrinks a workspace to its top <paramref name="count"/> tracks ranked by
    /// <paramref name="by"/>. Use as a pre-ordering cull when the user asks for a sized set
    /// and wants the "best" tracks rather than the BPM-prefix the ordering algorithm picks.
    /// </summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="name">Workspace to trim.</param>
    /// <param name="count">Target size (string-encoded). Clamped to <c>[1, ws.Count]</c>.</param>
    /// <param name="by">
    /// Ranking dimension: <c>rating</c> (default), <c>recent</c> (DateAdded desc),
    /// <c>playCount</c> desc, or <c>random</c>. Tracks missing the relevant field sort last.
    /// </param>
    /// <returns><c>{ ok, name, count, ordered }</c> on success, or <c>{ error }</c>.</returns>
    public static object TrimWorkspace(
        WorkspaceStore store,
        string name,
        string count,
        string? by = null)
    {
        var ws = store.Get(name);
        if (ws is null) return new { error = $"Workspace '{name}' not found." };

        var n = ParseInt(count);
        if (n is null || n <= 0) return new { error = "count must be a positive integer." };

        var dimension = HasValue(by) ? by!.ToLowerInvariant() : "rating";
        var tracks = ws.Tracks.ToList();

        IEnumerable<Track> ranked = dimension switch
        {
            "rating" => tracks.OrderByDescending(t => t.Rating).ThenByDescending(t => t.PlayCount),
            "recent" => tracks.OrderByDescending(t => t.DateAdded ?? DateTime.MinValue),
            "playcount" => tracks.OrderByDescending(t => t.PlayCount),
            "random" => tracks.OrderBy(_ => Random.Shared.Next()),
            _ => null!,
        };

        if (ranked is null)
            return new { error = $"Unknown 'by' value '{by}'. Expected: rating, recent, playCount, random." };

        var keep = ranked.Take(Math.Min(n.Value, tracks.Count)).Select(t => t.TrackId).ToList();
        ws.Apply(TrackSetMode.Replace, keep);
        return Summary(ws);
    }

    /// <summary>
    /// Commits a workspace into the <c>DJ_BUDDY</c> folder as a playlist. The <c>/export</c>
    /// slash command then writes the folder into the rekordbox.xml on disk.
    /// </summary>
    /// <param name="store">The workspace store.</param>
    /// <param name="workspaceName">Workspace to materialize as a <see cref="PlaylistNode"/>.</param>
    /// <param name="playlistName">
    /// Optional playlist name; when null or blank the workspace name is reused. If a playlist with
    /// the resulting name already exists under <c>DJ_BUDDY</c>, it is replaced in place.
    /// </param>
    /// <returns><c>{ ok=true, playlistName, count }</c> on success, or <c>{ ok=false, error }</c>.</returns>
    public static object CommitWorkspaceAsPlaylist(
        WorkspaceStore store,
        string workspaceName,
        string? playlistName = null)
    {
        var (ok, message, count) = store.Commit(workspaceName, playlistName);
        return ok
            ? new { ok = true, playlistName = message, count }
            : new { ok = false, error = message };
    }

    /// <summary>Compact success shape shared by all mutation methods — keeps the agent's transcript uniform.</summary>
    private static object Summary(TrackSet ws) => new
    {
        ok = true,
        name = ws.Name,
        count = ws.Count,
        ordered = ws.Ordered,
    };

    /// <summary>
    /// Parses a mode string into <see cref="TrackSetMode"/>. Accepts any casing to match the
    /// loose conventions the LLM tends to emit ("Union", "union", "UNION"). Returns an explicit
    /// error message on failure so the agent can self-correct.
    /// </summary>
    private static bool TryParseMode(string mode, out TrackSetMode parsed, out string error)
    {
        if (!HasValue(mode))
        {
            parsed = default;
            error = "mode is required (replace, union, intersect, except).";
            return false;
        }

        if (Enum.TryParse(mode, ignoreCase: true, out parsed))
        {
            error = "";
            return true;
        }

        error = $"Unknown mode '{mode}'. Expected: replace, union, intersect, except.";
        return false;
    }

    /// <summary>
    /// True when <paramref name="value"/> is a real user-supplied string. Filters out the
    /// placeholders the Copilot SDK sometimes passes through when the model tries to "null out"
    /// an optional argument — empty string, the literal word "null", and the wildcards "*" /
    /// "any" / "all". Keeps downstream filters from matching those as substrings.
    /// </summary>
    private static bool HasValue([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("null", StringComparison.OrdinalIgnoreCase)
        && value is not ("*" or "any" or "all");

    /// <summary>Parses an optional numeric argument, returning <c>null</c> for blanks/non-numerics.</summary>
    private static double? ParseDouble(string? value) =>
        HasValue(value) && double.TryParse(value, out var d) ? d : null;

    /// <summary>Parses an optional integer argument, returning <c>null</c> for blanks/non-numerics.</summary>
    private static int? ParseInt(string? value) =>
        HasValue(value) && int.TryParse(value, out var i) ? i : null;
}
