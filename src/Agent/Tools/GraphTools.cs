using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using QuikGraph;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Graph-backed Copilot SDK tools. All returns are IDs + edge metadata only; the agent is
/// expected to call <see cref="LibraryTools.GetTracks"/> with the returned IDs when it needs
/// titles/artists/etc. The graph is built once at startup and awaited on each tool call.
/// </summary>
internal static class GraphTools
{
    /// <summary>
    /// Returns compatible next-track candidates from the library as a whole (not restricted to a
    /// workspace). Result entries contain only the target track ID plus edge metadata —
    /// the agent projects titles via <c>get_tracks</c>.
    /// </summary>
    /// <param name="library">The parent library (used only for the source-track lookup).</param>
    /// <param name="graphTask">Task resolving to the shared compatibility graph.</param>
    /// <param name="trackId">Source track ID.</param>
    /// <param name="key">Optional Camelot key filter on the target.</param>
    /// <param name="genre">Optional genre substring filter on the target.</param>
    /// <param name="minBpm">Optional inclusive lower BPM bound.</param>
    /// <param name="maxBpm">Optional inclusive upper BPM bound.</param>
    /// <param name="limit">Optional cap; default 10, clamped to [1, 50].</param>
    public static async Task<object> SuggestNextTrack(
        RekordboxLibrary library,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        string trackId,
        string? key = null,
        string? genre = null,
        string? minBpm = null,
        string? maxBpm = null,
        string? limit = null)
    {
        if (!library.Tracks.TryGetValue(trackId, out var source))
            return new { error = $"Track with ID '{trackId}' not found." };

        var graph = await graphTask.ConfigureAwait(false);

        if (!graph.TryGetOutEdges(source, out var outEdges))
            return new { sourceTrackId = trackId, count = 0, neighbors = Array.Empty<object>() };

        IEnumerable<CompatibilityEdge> candidates = outEdges.OfType<CompatibilityEdge>();

        if (HasValue(key))
            candidates = candidates.Where(e =>
                string.Equals(e.Target.Key, key, StringComparison.OrdinalIgnoreCase));

        if (HasValue(genre))
            candidates = candidates.Where(e =>
                e.Target.Genre.Contains(genre!, StringComparison.OrdinalIgnoreCase));

        var parsedMin = ParseDouble(minBpm);
        var parsedMax = ParseDouble(maxBpm);
        if (parsedMin.HasValue)
            candidates = candidates.Where(e => e.Target.Bpm >= parsedMin.Value);
        if (parsedMax.HasValue)
            candidates = candidates.Where(e => e.Target.Bpm <= parsedMax.Value);

        var cap = Math.Clamp(ParseInt(limit) ?? 10, 1, 50);
        var neighbors = candidates
            .OrderBy(e => e.Weight)
            .Take(cap)
            .Select(FormatCompat)
            .ToList();

        return new { sourceTrackId = trackId, count = neighbors.Count, neighbors };
    }

    /// <summary>
    /// Next-track suggestions restricted to tracks inside a named workspace. Supports the
    /// interactive / build-a-set-from-a-curated-subset flow.
    /// </summary>
    /// <param name="store">The workspace store holding the named <see cref="Rekordbox.Query.TrackSet"/>.</param>
    /// <param name="graphTask">Task resolving to the shared compatibility graph.</param>
    /// <param name="workspaceName">Name of the workspace to restrict candidates to.</param>
    /// <param name="trackId">Source track ID (must exist in the library; need not be in the workspace).</param>
    /// <param name="limit">Optional cap; default 10, clamped to [1, 50].</param>
    public static async Task<object> SuggestNextInWorkspace(
        WorkspaceStore store,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        string workspaceName,
        string trackId,
        string? limit = null)
    {
        var ws = store.Get(workspaceName);
        if (ws is null)
            return new { error = $"Workspace '{workspaceName}' not found." };

        if (!store.Library.Tracks.TryGetValue(trackId, out var source))
            return new { error = $"Track with ID '{trackId}' not found." };

        var graph = await graphTask.ConfigureAwait(false);
        if (!graph.TryGetOutEdges(source, out var outEdges))
            return new { sourceTrackId = trackId, workspace = workspaceName, count = 0, neighbors = Array.Empty<object>() };

        var cap = Math.Clamp(ParseInt(limit) ?? 10, 1, 50);

        var neighbors = outEdges
            .OfType<CompatibilityEdge>()
            .Where(e => ws.Contains(e.Target.TrackId))
            .OrderBy(e => e.Weight)
            .Take(cap)
            .Select(FormatCompat)
            .ToList();

        return new
        {
            sourceTrackId = trackId,
            workspace = workspaceName,
            count = neighbors.Count,
            neighbors,
        };
    }

    /// <summary>
    /// Returns neighbors blending harmonic compatibility and playlist co-occurrence. Each entry
    /// carries an ID plus whichever evidence components exist; the combined score sums both
    /// contributions (missing sides are treated as zero).
    /// </summary>
    public static async Task<object> FindSimilarTracks(
        RekordboxLibrary library,
        Task<BidirectionalGraph<Track, TrackEdge>> graphTask,
        string trackId,
        string? limit = null)
    {
        if (!library.Tracks.TryGetValue(trackId, out var source))
            return new { error = $"Track with ID '{trackId}' not found." };

        var graph = await graphTask.ConfigureAwait(false);
        if (!graph.TryGetOutEdges(source, out var outEdges))
            return new { sourceTrackId = trackId, count = 0, neighbors = Array.Empty<object>() };

        var grouped = new Dictionary<string, (CompatibilityEdge? Compat, CoOccurrenceEdge? CoOccur)>();
        foreach (var edge in outEdges)
        {
            var id = edge.Target.TrackId;
            grouped.TryGetValue(id, out var slot);

            if (edge is CompatibilityEdge ce)
            {
                if (slot.Compat is null || ce.Weight < slot.Compat.Weight)
                    slot.Compat = ce;
            }
            else if (edge is CoOccurrenceEdge coe)
            {
                slot.CoOccur = coe;
            }

            grouped[id] = slot;
        }

        var cap = Math.Clamp(ParseInt(limit) ?? 10, 1, 50);
        var neighbors = grouped
            .Where(kv => kv.Value.Compat is not null || kv.Value.CoOccur is not null)
            .Select(kv => new
            {
                trackId = kv.Key,
                compatWeight = kv.Value.Compat is null ? (double?)null : Math.Round(kv.Value.Compat.Weight, 4),
                coOccurCount = kv.Value.CoOccur?.PlaylistCount,
                combinedScore = Math.Round(
                    (kv.Value.Compat?.Weight ?? 0) + (kv.Value.CoOccur?.Weight ?? 0), 4),
            })
            .OrderBy(x => x.combinedScore)
            .Take(cap)
            .ToList();

        return new { sourceTrackId = trackId, count = neighbors.Count, neighbors };
    }

    private static object FormatCompat(CompatibilityEdge e) => new
    {
        trackId = e.Target.TrackId,
        relation = e.Relation.ToString(),
        tier = e.Tier.ToString(),
        bpmDeltaPercent = Math.Round(e.BpmDeltaPercent, 2),
        isHalfTimeMatch = e.IsHalfTimeMatch,
        weight = Math.Round(e.Weight, 4),
    };

    private static bool HasValue([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("null", StringComparison.OrdinalIgnoreCase)
        && value is not ("*" or "any" or "all");

    private static double? ParseDouble(string? value) =>
        HasValue(value) && double.TryParse(value, out var d) ? d : null;

    private static int? ParseInt(string? value) =>
        HasValue(value) && int.TryParse(value, out var i) ? i : null;
}
