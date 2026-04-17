using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using QuikGraph;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Agent tools that walk the <see cref="BidirectionalGraph{Track, TrackEdge}"/> built by
/// <see cref="TrackGraphBuilder"/>. The graph is expensive to construct and is built exactly
/// once at startup; each tool awaits the shared build task then performs lookups only.
/// </summary>
internal static class GraphTools
{
    /// <summary>
    /// Returns compatible next-track candidates for a given source track, sorted by
    /// <see cref="CompatibilityEdge"/> weight (lower is better). Optional filters narrow the
    /// candidate set by Camelot key, genre substring, or BPM range.
    /// </summary>
    /// <param name="library">Parsed rekordbox library (used for the track lookup).</param>
    /// <param name="graphTask">Task resolving to the shared, process-wide graph.</param>
    /// <param name="trackId">TrackID of the currently playing track.</param>
    /// <param name="key">Optional Camelot key filter (e.g. "8A"); matched case-insensitively.</param>
    /// <param name="genre">Optional genre substring filter (case-insensitive contains).</param>
    /// <param name="minBpm">Optional inclusive minimum BPM; parsed lazily to tolerate LLM junk values.</param>
    /// <param name="maxBpm">Optional inclusive maximum BPM; parsed lazily to tolerate LLM junk values.</param>
    /// <param name="limit">Optional result cap (default 10, clamped to 1..50).</param>
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
            return new { sourceTrackId = trackId, count = 0, suggestions = Array.Empty<object>() };

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
        var suggestions = candidates
            .OrderBy(e => e.Weight)
            .Take(cap)
            .Select(e => new
            {
                track = TrackSummary.Of(e.Target),
                relation = e.Relation.ToString(),
                tier = e.Tier.ToString(),
                bpmDeltaPercent = Math.Round(e.BpmDeltaPercent, 2),
                isHalfTimeMatch = e.IsHalfTimeMatch,
                weight = Math.Round(e.Weight, 4),
            })
            .ToList();

        return new
        {
            sourceTrackId = trackId,
            count = suggestions.Count,
            suggestions,
        };
    }

    /// <summary>
    /// Returns tracks similar to the given source, combining both edge families: harmonic
    /// compatibility (<see cref="CompatibilityEdge"/>) and playlist co-occurrence
    /// (<see cref="CoOccurrenceEdge"/>). Only tracks reachable by at least one edge kind are
    /// considered; the combined score sums both contributions (missing contributions are
    /// treated as zero penalty so the metric degrades gracefully).
    /// </summary>
    /// <param name="library">Parsed rekordbox library (used for the track lookup).</param>
    /// <param name="graphTask">Task resolving to the shared, process-wide graph.</param>
    /// <param name="trackId">TrackID to find neighbors of.</param>
    /// <param name="limit">Optional result cap (default 10, clamped to 1..50).</param>
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
            return new { sourceTrackId = trackId, count = 0, similar = Array.Empty<object>() };

        var grouped = new Dictionary<string, (Track Target, CompatibilityEdge? Compat, CoOccurrenceEdge? CoOccur)>();

        foreach (var edge in outEdges)
        {
            var id = edge.Target.TrackId;
            grouped.TryGetValue(id, out var slot);
            slot.Target = edge.Target;

            if (edge is CompatibilityEdge ce)
            {
                // Keep the best (lowest-weight) compatibility edge per neighbor.
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
        var similar = grouped.Values
            .Where(s => s.Compat is not null || s.CoOccur is not null)
            .Select(s => new
            {
                track = TrackSummary.Of(s.Target),
                compatibility = s.Compat is null ? null : (object)new
                {
                    relation = s.Compat.Relation.ToString(),
                    tier = s.Compat.Tier.ToString(),
                    bpmDeltaPercent = Math.Round(s.Compat.BpmDeltaPercent, 2),
                    weight = Math.Round(s.Compat.Weight, 4),
                },
                coOccurrence = s.CoOccur is null ? null : (object)new
                {
                    playlistCount = s.CoOccur.PlaylistCount,
                    weight = Math.Round(s.CoOccur.Weight, 4),
                },
                combinedScore = Math.Round((s.Compat?.Weight ?? 0) + (s.CoOccur?.Weight ?? 0), 4),
            })
            .OrderBy(x => x.combinedScore)
            .Take(cap)
            .ToList();

        return new
        {
            sourceTrackId = trackId,
            count = similar.Count,
            similar,
        };
    }

    /// <summary>
    /// Returns true when a string parameter carries a meaningful value — mirrors the same guard
    /// used in <see cref="LibraryTools"/> so LLM-supplied junk ("null", "any", "*") is ignored.
    /// </summary>
    private static bool HasValue([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("null", StringComparison.OrdinalIgnoreCase)
        && value is not ("*" or "any" or "all");

    /// <summary>Parses a string to <see cref="double"/>; returns null for junk values.</summary>
    private static double? ParseDouble(string? value) =>
        HasValue(value) && double.TryParse(value, out var d) ? d : null;

    /// <summary>Parses a string to <see cref="int"/>; returns null for junk values.</summary>
    private static int? ParseInt(string? value) =>
        HasValue(value) && int.TryParse(value, out var i) ? i : null;
}
