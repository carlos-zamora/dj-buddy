using DJBuddy.Rekordbox.Models;
using QuikGraph;

namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// Name of the ordering strategy that was chosen for a given input size. Exposed on
/// <see cref="OrderingResult.Strategy"/> so callers (and the agent) can see why a long set
/// was ordered with a cheaper heuristic.
/// </summary>
public enum OrderingStrategy
{
    /// <summary>Zero or one track — nothing to order.</summary>
    Trivial,

    /// <summary>Exact TSP path via dynamic programming (Held-Karp). Used for small sets (≤ 15).</summary>
    HeldKarp,

    /// <summary>Greedy nearest-neighbor followed by 2-opt local search. Used for medium sets (16–80).</summary>
    NearestNeighborTwoOpt,

    /// <summary>Greedy nearest-neighbor only. Used for large sets (≥ 81) where polishing becomes too expensive.</summary>
    NearestNeighbor,
}

/// <summary>
/// Result of <see cref="TrackSetOrdering.Order"/>: the ordered track-ID sequence plus the
/// strategy that produced it.
/// </summary>
/// <param name="OrderedIds">The chosen order. Contains exactly one entry per input track.</param>
/// <param name="Strategy">Which algorithm produced <see cref="OrderedIds"/>.</param>
public readonly record struct OrderingResult(IReadOnlyList<string> OrderedIds, OrderingStrategy Strategy);

/// <summary>
/// Adaptive track-set ordering. Given a subset of tracks and the shared compatibility graph,
/// produces a sequence that minimizes total transition weight (lower = smoother DJ set).
/// Strategy is chosen automatically based on input size — small sets get an exact solver,
/// larger sets use progressively cheaper heuristics.
/// </summary>
public static class TrackSetOrdering
{
    /// <summary>Input-size cutoff below which Held-Karp's exact DP runs in under a second.</summary>
    public const int HeldKarpLimit = 15;

    /// <summary>Input-size cutoff below which greedy nearest-neighbor plus 2-opt polish runs in well under a second.</summary>
    public const int TwoOptLimit = 80;

    /// <summary>
    /// Penalty weight applied to vertex pairs that have no direct <see cref="CompatibilityEdge"/>.
    /// Chosen large enough that algorithms strongly prefer real edges, but finite so that
    /// fully-disconnected subsets still produce <em>some</em> ordering instead of failing.
    /// </summary>
    public const double MissingEdgePenalty = 1_000_000.0;

    /// <summary>
    /// Orders <paramref name="tracks"/> into a sequence minimizing cumulative transition weight.
    /// </summary>
    /// <param name="graph">The shared compatibility graph (vertices must cover <paramref name="tracks"/>).</param>
    /// <param name="tracks">Tracks to order. Duplicates are collapsed.</param>
    /// <param name="seed">
    /// Optional starting track. When supplied, the result begins with <paramref name="seed"/>.
    /// When null, the lowest-BPM track is used so sets open at their coolest tempo.
    /// </param>
    /// <returns>The ordered sequence of TrackIds plus the strategy that produced it.</returns>
    public static OrderingResult Order(
        BidirectionalGraph<Track, TrackEdge> graph,
        IReadOnlyCollection<Track> tracks,
        Track? seed = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(tracks);

        // Collapse duplicates while preserving insertion order, in case callers pass an
        // iterator that yields a track twice.
        var uniqueTracks = new List<Track>(tracks.Count);
        var seenIds = new HashSet<string>(tracks.Count);
        foreach (var t in tracks)
        {
            if (seenIds.Add(t.TrackId))
                uniqueTracks.Add(t);
        }

        var n = uniqueTracks.Count;
        if (n == 0)
            return new OrderingResult([], OrderingStrategy.Trivial);
        if (n == 1)
            return new OrderingResult([uniqueTracks[0].TrackId], OrderingStrategy.Trivial);

        var seedIndex = ResolveSeedIndex(uniqueTracks, seed);
        var weights = BuildWeightMatrix(graph, uniqueTracks);

        if (n <= HeldKarpLimit)
        {
            var path = HeldKarpPath(weights, seedIndex);
            return new OrderingResult(PathToIds(path, uniqueTracks), OrderingStrategy.HeldKarp);
        }

        // Greedy NN is the backbone for both medium and large cases; 2-opt polishes it.
        var greedy = NearestNeighbor(weights, seedIndex);
        if (n <= TwoOptLimit)
        {
            TwoOptPolish(greedy, weights);
            return new OrderingResult(PathToIds(greedy, uniqueTracks), OrderingStrategy.NearestNeighborTwoOpt);
        }

        return new OrderingResult(PathToIds(greedy, uniqueTracks), OrderingStrategy.NearestNeighbor);
    }

    /// <summary>Maps an optional seed track to an index into <paramref name="tracks"/>, falling back to the lowest-BPM track.</summary>
    private static int ResolveSeedIndex(List<Track> tracks, Track? seed)
    {
        if (seed is not null)
        {
            for (var i = 0; i < tracks.Count; i++)
            {
                if (ReferenceEquals(tracks[i], seed) || tracks[i].TrackId == seed.TrackId)
                    return i;
            }
            // Seed provided but not in the workspace — fall through to BPM-based default.
        }

        var bestIdx = 0;
        var bestBpm = double.PositiveInfinity;
        for (var i = 0; i < tracks.Count; i++)
        {
            // Treat missing BPM as infinite so it sorts last — a known BPM is always preferred as seed.
            var bpm = tracks[i].Bpm > 0 ? tracks[i].Bpm : double.PositiveInfinity;
            if (bpm < bestBpm)
            {
                bestBpm = bpm;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    /// <summary>
    /// Builds the N×N pairwise weight matrix using the lowest-weight <see cref="CompatibilityEdge"/>
    /// between each ordered pair. Pairs with no edge fall back to <see cref="MissingEdgePenalty"/>.
    /// </summary>
    private static double[,] BuildWeightMatrix(
        BidirectionalGraph<Track, TrackEdge> graph,
        List<Track> tracks)
    {
        var n = tracks.Count;
        var indexById = new Dictionary<string, int>(n);
        for (var i = 0; i < n; i++) indexById[tracks[i].TrackId] = i;

        var weights = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                weights[i, j] = i == j ? 0.0 : MissingEdgePenalty;

        // Pull compatibility edges from each source's out-edges — the graph stores them once per
        // ordered pair, so walking source sides is sufficient.
        foreach (var track in tracks)
        {
            if (!graph.TryGetOutEdges(track, out var outEdges)) continue;
            if (!indexById.TryGetValue(track.TrackId, out var i)) continue;

            foreach (var edge in outEdges)
            {
                if (edge is not CompatibilityEdge compat) continue;
                if (!indexById.TryGetValue(compat.Target.TrackId, out var j)) continue;

                // Keep the minimum-weight edge when duplicates exist (defensive — the builder
                // currently emits at most one per ordered pair, but parallel edges are allowed
                // by the graph configuration).
                if (compat.Weight < weights[i, j])
                    weights[i, j] = compat.Weight;
            }
        }

        return weights;
    }

    /// <summary>
    /// Held-Karp dynamic programming for the Hamiltonian-path TSP variant (no return to start).
    /// <c>dp[mask, v]</c> = minimum cost of a path that visits exactly the vertices in <c>mask</c>
    /// and ends at <c>v</c>. The path is reconstructed by following <c>parent</c>.
    /// </summary>
    private static int[] HeldKarpPath(double[,] weights, int seed)
    {
        var n = weights.GetLength(0);
        var full = (1 << n) - 1;

        // dp and parent are flat arrays indexed by [mask * n + v] for cache friendliness.
        var dp = new double[(1 << n) * n];
        var parent = new int[(1 << n) * n];
        Array.Fill(dp, double.PositiveInfinity);
        Array.Fill(parent, -1);

        dp[(1 << seed) * n + seed] = 0.0;

        for (var mask = 1; mask <= full; mask++)
        {
            // Require the seed to be in every mask — we only care about paths that start at seed.
            if ((mask & (1 << seed)) == 0) continue;

            for (var v = 0; v < n; v++)
            {
                if ((mask & (1 << v)) == 0) continue;
                var cur = dp[mask * n + v];
                if (double.IsPositiveInfinity(cur)) continue;

                for (var u = 0; u < n; u++)
                {
                    if ((mask & (1 << u)) != 0) continue; // already visited
                    var newMask = mask | (1 << u);
                    var candidate = cur + weights[v, u];
                    var slot = newMask * n + u;
                    if (candidate < dp[slot])
                    {
                        dp[slot] = candidate;
                        parent[slot] = v;
                    }
                }
            }
        }

        // Pick the endpoint that yields the minimum total cost over the full visit.
        var bestEnd = -1;
        var bestCost = double.PositiveInfinity;
        for (var v = 0; v < n; v++)
        {
            var cost = dp[full * n + v];
            if (cost < bestCost)
            {
                bestCost = cost;
                bestEnd = v;
            }
        }

        if (bestEnd < 0)
        {
            // No reachable full path — every step paid the missing-edge penalty but the seed alone
            // is fine. Fall back to a straight seed-first sequence.
            var fallback = new int[n];
            fallback[0] = seed;
            var k = 1;
            for (var v = 0; v < n; v++) if (v != seed) fallback[k++] = v;
            return fallback;
        }

        var path = new int[n];
        var cursor = bestEnd;
        var remMask = full;
        for (var i = n - 1; i >= 0; i--)
        {
            path[i] = cursor;
            var slot = remMask * n + cursor;
            var prev = parent[slot];
            remMask ^= 1 << cursor;
            cursor = prev;
            if (cursor < 0) break;
        }
        return path;
    }

    /// <summary>
    /// Greedy nearest-neighbor tour starting from <paramref name="seed"/>. At each step picks the
    /// unvisited vertex with the lowest edge weight from the current tail.
    /// </summary>
    private static int[] NearestNeighbor(double[,] weights, int seed)
    {
        var n = weights.GetLength(0);
        var path = new int[n];
        var visited = new bool[n];

        path[0] = seed;
        visited[seed] = true;

        for (var step = 1; step < n; step++)
        {
            var prev = path[step - 1];
            var bestNext = -1;
            var bestW = double.PositiveInfinity;
            for (var v = 0; v < n; v++)
            {
                if (visited[v]) continue;
                var w = weights[prev, v];
                if (w < bestW)
                {
                    bestW = w;
                    bestNext = v;
                }
            }

            // Safety net: if every remaining weight is infinity (shouldn't happen because the
            // missing-edge penalty is finite), pick an arbitrary unvisited vertex.
            if (bestNext < 0)
            {
                for (var v = 0; v < n; v++)
                {
                    if (!visited[v]) { bestNext = v; break; }
                }
            }

            path[step] = bestNext;
            visited[bestNext] = true;
        }

        return path;
    }

    /// <summary>
    /// 2-opt local search for the path variant (no wrap-around). Reverses every subsegment
    /// <c>[i..j]</c> that reduces total weight and restarts the scan on improvement. Terminates
    /// when a full pass finds no improving swap.
    /// </summary>
    private static void TwoOptPolish(int[] path, double[,] weights)
    {
        var n = path.Length;
        if (n < 4) return;

        var improved = true;
        while (improved)
        {
            improved = false;
            for (var i = 1; i < n - 1; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    // Edges broken: (path[i-1], path[i]) and (path[j], path[j+1]?)
                    // Edges formed: (path[i-1], path[j]) and (path[i], path[j+1]?)
                    var a = path[i - 1];
                    var b = path[i];
                    var c = path[j];
                    var removed = weights[a, b];
                    var added = weights[a, c];

                    if (j + 1 < n)
                    {
                        var d = path[j + 1];
                        removed += weights[c, d];
                        added += weights[b, d];
                    }

                    if (added + 1e-12 < removed)
                    {
                        Array.Reverse(path, i, j - i + 1);
                        improved = true;
                    }
                }
            }
        }
    }

    private static string[] PathToIds(int[] path, List<Track> tracks)
    {
        var ids = new string[path.Length];
        for (var i = 0; i < path.Length; i++)
            ids[i] = tracks[path[i]].TrackId;
        return ids;
    }
}
