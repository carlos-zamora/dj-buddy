using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using QuikGraph;

namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// Builds a <see cref="BidirectionalGraph{Track, TrackEdge}"/> view of a
/// <see cref="RekordboxLibrary"/>. Callers use QuikGraph's own algorithms directly against the
/// returned graph; this class is responsible only for populating vertices and edges, not for
/// exposing an opinionated algorithm surface.
/// </summary>
/// <remarks>
/// TODO: a future Agent tool should navigate this graph to assemble ordered DJ sets — the
/// directedness of <see cref="CompatibilityEdge"/> (EnergyBoost / EnergyDrop) is designed to
/// support that use case. Implementing the navigation belongs in <c>src/Agent/Tools/</c> and is
/// out of scope for the initial builder.
/// </remarks>
public static class TrackGraphBuilder
{
    /// <summary>
    /// Builds a graph populated with every track in <paramref name="library"/> as a vertex, plus
    /// compatibility and/or co-occurrence edges per <paramref name="options"/>.
    /// </summary>
    /// <param name="library">Parsed rekordbox library.</param>
    /// <param name="options">Build options; <c>null</c> uses defaults.</param>
    /// <returns>A bidirectional graph with parallel edges allowed so multiple edge kinds may share a vertex pair.</returns>
    public static BidirectionalGraph<Track, TrackEdge> Build(RekordboxLibrary library, TrackGraphOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        options ??= new TrackGraphOptions();

        var graph = new BidirectionalGraph<Track, TrackEdge>(allowParallelEdges: true);
        graph.AddVertexRange(library.Tracks.Values);

        if (options.IncludeCompatibilityEdges)
            AddCompatibilityEdges(graph, library, options);

        if (options.IncludeCoOccurrenceEdges)
            AddCoOccurrenceEdges(graph, library, options);

        return graph;
    }

    /// <summary>
    /// Adds directed <see cref="CompatibilityEdge"/>s between every track pair whose Camelot keys
    /// classify under <see cref="CamelotWheel.Classify"/> and whose BPMs fall within the
    /// configured tolerance (with optional half/double-time matching).
    /// </summary>
    /// <remarks>
    /// Iterates ordered pairs so each direction is classified independently — a 6A→8A pair
    /// naturally produces an <see cref="HarmonicRelation.EnergyBoost"/> edge while the reverse
    /// 8A→6A produces <see cref="HarmonicRelation.EnergyDrop"/>. Tracks with missing or
    /// unparseable keys are skipped. Self-loops are not emitted.
    /// </remarks>
    public static void AddCompatibilityEdges(
        BidirectionalGraph<Track, TrackEdge> graph,
        RekordboxLibrary library,
        TrackGraphOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(library);
        options ??= new TrackGraphOptions();

        // Precompute parsed keys once so we don't re-parse on every pair comparison.
        var keyed = new List<(Track track, int number, char letter)>();
        foreach (var track in library.Tracks.Values)
        {
            if (CamelotWheel.TryParse(track.Key, out var num, out var letter))
                keyed.Add((track, num, letter));
        }

        for (var i = 0; i < keyed.Count; i++)
        {
            var (a, _, _) = keyed[i];
            for (var j = 0; j < keyed.Count; j++)
            {
                if (i == j) continue;
                var (b, _, _) = keyed[j];

                var relation = CamelotWheel.Classify(a.Key, b.Key, options.AllowRelativeMajorMinor);
                if (relation is null) continue;

                if (!TryComputeBpmMatch(a.Bpm, b.Bpm, options, out var deltaPercent, out var isHalfTime))
                    continue;

                var tier = ClassifyTier(deltaPercent, options);
                var weight = ComputeCompatibilityWeight(a, b, relation.Value, tier, isHalfTime, options);
                graph.AddEdge(new CompatibilityEdge(a, b, relation.Value, tier, deltaPercent, isHalfTime, weight));
            }
        }
    }

    /// <summary>
    /// Adds directed <see cref="CoOccurrenceEdge"/>s for every pair of tracks that share at
    /// least one playlist, with <see cref="CoOccurrenceEdge.PlaylistCount"/> set to the number
    /// of shared playlists. Emitted in both directions with the same count.
    /// </summary>
    public static void AddCoOccurrenceEdges(
        BidirectionalGraph<Track, TrackEdge> graph,
        RekordboxLibrary library,
        TrackGraphOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(library);
        options ??= new TrackGraphOptions();

        // Accumulate pairwise counts keyed by the ordered TrackId pair (lo, hi) so each
        // unordered pair maps to a single slot regardless of the order we encountered the
        // tracks within a playlist.
        var counts = new Dictionary<(string, string), int>();

        foreach (var playlist in library.Root.EnumeratePlaylists())
        {
            // Distinct so a playlist that (legally, but rarely) lists a track twice doesn't
            // double-count its contribution.
            var tracks = playlist.GetTracks(library).Distinct().ToList();
            for (var i = 0; i < tracks.Count; i++)
            {
                for (var j = i + 1; j < tracks.Count; j++)
                {
                    var key = OrderedPair(tracks[i].TrackId, tracks[j].TrackId);
                    counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
                }
            }
        }

        foreach (var ((loId, hiId), count) in counts)
        {
            if (!library.Tracks.TryGetValue(loId, out var lo) ||
                !library.Tracks.TryGetValue(hiId, out var hi))
            {
                continue;
            }

            var weight = ComputeCoOccurrenceWeight(lo, hi, count, options);
            graph.AddEdge(new CoOccurrenceEdge(lo, hi, count, weight));
            graph.AddEdge(new CoOccurrenceEdge(hi, lo, count, weight));
        }
    }

    /// <summary>
    /// Computes the effective BPM delta for <paramref name="a"/> and <paramref name="b"/>,
    /// considering direct, double-time, and half-time matches. Returns <c>false</c> when the
    /// best match is beyond <see cref="TrackGraphOptions.BpmFarThreshold"/>.
    /// </summary>
    private static bool TryComputeBpmMatch(
        double a,
        double b,
        TrackGraphOptions options,
        out double deltaPercent,
        out bool isHalfTimeMatch)
    {
        deltaPercent = double.PositiveInfinity;
        isHalfTimeMatch = false;

        if (a <= 0 || b <= 0)
            return false;

        var direct = PercentDelta(a, b);
        var best = direct;

        if (options.AllowHalfTimeMatch)
        {
            var doubled = PercentDelta(a, 2.0 * b);
            var halved = PercentDelta(2.0 * a, b);
            if (doubled < best)
            {
                best = doubled;
                isHalfTimeMatch = true;
            }
            if (halved < best)
            {
                best = halved;
                isHalfTimeMatch = true;
            }
        }

        if (best > options.BpmFarThreshold)
            return false;

        deltaPercent = best;
        return true;

        static double PercentDelta(double x, double y)
        {
            var reference = Math.Max(x, y);
            return reference <= 0 ? double.PositiveInfinity : Math.Abs(x - y) / reference * 100.0;
        }
    }

    private static BpmTier ClassifyTier(double deltaPercent, TrackGraphOptions options)
    {
        if (deltaPercent <= options.BpmSameEpsilon) return BpmTier.Same;
        if (deltaPercent <= options.BpmCloseThreshold) return BpmTier.Close;
        if (deltaPercent <= options.BpmMediumThreshold) return BpmTier.Medium;
        return BpmTier.Far;
    }

    /// <remarks>TODO(weight-tuning): simple additive composition; consider multiplicative or non-linear terms.</remarks>
    private static double ComputeCompatibilityWeight(
        Track source,
        Track target,
        HarmonicRelation relation,
        BpmTier tier,
        bool isHalfTimeMatch,
        TrackGraphOptions options)
    {
        // TODO(weight-tuning): base weights live in TrackGraphOptions so callers can override
        // without touching this method. Keep this function pure additive for now.
        var relationWeight = relation switch
        {
            HarmonicRelation.Same => options.RelationWeightSame,
            HarmonicRelation.Adjacent => options.RelationWeightAdjacent,
            HarmonicRelation.EnergyBoost => options.RelationWeightEnergyBoost,
            HarmonicRelation.EnergyDrop => options.RelationWeightEnergyDrop,
            _ => 0.0,
        };

        var tierWeight = tier switch
        {
            BpmTier.Same => options.TierWeightSame,
            BpmTier.Close => options.TierWeightClose,
            BpmTier.Medium => options.TierWeightMedium,
            BpmTier.Far => options.TierWeightFar,
            _ => 0.0,
        };

        var halfTimeTerm = isHalfTimeMatch ? options.HalfTimePenalty : 0.0;
        var qualityTerm = TrackQualityWeight.Compute(source, target, options);

        return relationWeight + tierWeight + halfTimeTerm + qualityTerm;
    }

    /// <remarks>TODO(weight-tuning): 1.0 / PlaylistCount is crude; a log-scale or IDF-style damping may be more meaningful once library sizes grow.</remarks>
    private static double ComputeCoOccurrenceWeight(Track source, Track target, int playlistCount, TrackGraphOptions options)
    {
        var baseWeight = playlistCount <= 0 ? 1.0 : 1.0 / playlistCount;
        var qualityTerm = TrackQualityWeight.Compute(source, target, options);
        return baseWeight + qualityTerm;
    }

    private static (string lo, string hi) OrderedPair(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
}
