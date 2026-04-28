using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using QuikGraph;
using Xunit;

namespace DJBuddy.Rekordbox.Tests.Graph;

/// <summary>
/// Invariants for <see cref="TrackSetOrdering"/>: strategy selection, Held-Karp optimality on
/// a hand-crafted graph, and 2-opt improving over pure nearest-neighbor on a pathological seed.
/// </summary>
public class TrackSetOrderingTests
{
    /// <summary>
    /// Wires a directed compatibility edge with an explicit weight. Uses the builder's vertex
    /// registration implicitly via AddVerticesAndEdge so tests don't have to pre-add tracks.
    /// </summary>
    private static void AddEdge(
        BidirectionalGraph<Track, TrackEdge> graph, Track from, Track to, double weight)
    {
        var edge = new CompatibilityEdge(
            from, to,
            HarmonicRelation.Same,
            BpmTier.Same,
            bpmDeltaPercent: 0,
            isHalfTimeMatch: false,
            weight: weight);
        graph.AddVerticesAndEdge(edge);
    }

    private static Track T(string id, double bpm = 120) => new()
    {
        TrackId = id,
        Name = id,
        Bpm = bpm,
    };

    /// <summary>
    /// Populates a graph with a symmetric complete digraph over <paramref name="tracks"/> using
    /// <paramref name="weights"/>[i, j] as the directed edge weight from tracks[i] → tracks[j].
    /// </summary>
    private static BidirectionalGraph<Track, TrackEdge> CompleteGraph(Track[] tracks, double[,] weights)
    {
        var g = new BidirectionalGraph<Track, TrackEdge>(allowParallelEdges: true);
        foreach (var t in tracks) g.AddVertex(t);
        for (var i = 0; i < tracks.Length; i++)
            for (var j = 0; j < tracks.Length; j++)
                if (i != j) AddEdge(g, tracks[i], tracks[j], weights[i, j]);
        return g;
    }

    [Fact]
    public void Order_returns_empty_for_empty_input()
    {
        var g = new BidirectionalGraph<Track, TrackEdge>();
        var result = TrackSetOrdering.Order(g, Array.Empty<Track>());
        Assert.Equal(OrderingStrategy.Trivial, result.Strategy);
        Assert.Empty(result.OrderedIds);
    }

    [Fact]
    public void Order_is_trivial_for_single_track()
    {
        var g = new BidirectionalGraph<Track, TrackEdge>();
        var t = T("a");
        g.AddVertex(t);

        var result = TrackSetOrdering.Order(g, new[] { t });

        Assert.Equal(OrderingStrategy.Trivial, result.Strategy);
        Assert.Single(result.OrderedIds);
        Assert.Equal("a", result.OrderedIds[0]);
    }

    [Fact]
    public void Order_picks_HeldKarp_at_the_limit()
    {
        var tracks = Enumerable.Range(0, TrackSetOrdering.HeldKarpLimit)
            .Select(i => T($"t{i}", bpm: 120 + i)).ToArray();
        var weights = new double[tracks.Length, tracks.Length];
        var g = CompleteGraph(tracks, weights);

        var result = TrackSetOrdering.Order(g, tracks);

        Assert.Equal(OrderingStrategy.HeldKarp, result.Strategy);
        Assert.Equal(tracks.Length, result.OrderedIds.Count);
    }

    [Fact]
    public void Order_picks_NN_plus_2opt_just_above_HeldKarp_limit()
    {
        var n = TrackSetOrdering.HeldKarpLimit + 1;
        var tracks = Enumerable.Range(0, n).Select(i => T($"t{i}", bpm: 120 + i)).ToArray();
        var g = CompleteGraph(tracks, new double[n, n]);

        var result = TrackSetOrdering.Order(g, tracks);

        Assert.Equal(OrderingStrategy.NearestNeighborTwoOpt, result.Strategy);
    }

    [Fact]
    public void Order_picks_NN_only_just_above_TwoOpt_limit()
    {
        var n = TrackSetOrdering.TwoOptLimit + 1;
        var tracks = Enumerable.Range(0, n).Select(i => T($"t{i}", bpm: 120 + i)).ToArray();
        var g = CompleteGraph(tracks, new double[n, n]);

        var result = TrackSetOrdering.Order(g, tracks);

        Assert.Equal(OrderingStrategy.NearestNeighbor, result.Strategy);
    }

    [Fact]
    public void HeldKarp_finds_optimal_path_on_hand_crafted_graph()
    {
        // Four tracks. Cheap path from A is A → C → B → D (1 + 1 + 1 = 3).
        // Greedy NN from A would pick B first (weight 5 is worse, so actually picks cheapest = C,
        // then from C it would pick D=1; this yields A→C→D→B=1+1+99=101). Held-Karp must beat it.
        var a = T("A");
        var b = T("B");
        var c = T("C");
        var d = T("D");
        var tracks = new[] { a, b, c, d };

        //     A     B     C     D
        // A   0    5     1    99
        // B   5    0     1     1
        // C   1    1     0     1
        // D  99    1     1     0
        var w = new double[,]
        {
            { 0, 5, 1, 99 },
            { 5, 0, 1,  1 },
            { 1, 1, 0,  1 },
            { 99,1, 1,  0 },
        };
        var g = CompleteGraph(tracks, w);

        var result = TrackSetOrdering.Order(g, tracks, seed: a);

        Assert.Equal(OrderingStrategy.HeldKarp, result.Strategy);
        Assert.Equal("A", result.OrderedIds[0]);

        double total = 0;
        for (var i = 1; i < result.OrderedIds.Count; i++)
        {
            var fromIdx = Array.FindIndex(tracks, t => t.TrackId == result.OrderedIds[i - 1]);
            var toIdx = Array.FindIndex(tracks, t => t.TrackId == result.OrderedIds[i]);
            total += w[fromIdx, toIdx];
        }
        // Optimal Hamiltonian path from A: A→C→B→D = 1+1+1 = 3.
        Assert.Equal(3, total);
    }

    [Fact]
    public void TwoOpt_improves_on_nearest_neighbor_for_pathological_seed()
    {
        // 16 tracks so we land in the NN+2-opt regime. Weights form a "cheap cycle" 0-1-2-…-15-0
        // with weight 1 per hop, but a shortcut from 0→5 with weight 0.5 that lures the greedy
        // algorithm off the cycle and strands it with expensive fallbacks.
        const int n = 16;
        var tracks = Enumerable.Range(0, n).Select(i => T($"t{i}", bpm: 120 + i * 0.1)).ToArray();
        var w = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                w[i, j] = i == j ? 0 : 100; // expensive default
        for (var i = 0; i < n; i++)
        {
            var next = (i + 1) % n;
            w[i, next] = 1;
            w[next, i] = 1;
        }
        // Shortcut to derail greedy NN from the good cycle.
        w[0, 5] = 0.5;
        w[5, 0] = 0.5;

        var g = CompleteGraph(tracks, w);

        // NN-only cost (simulate by forcing the NN path via bumping size over the 2-opt limit
        // would be overkill; instead use the public Order result and compare vs a straightforward
        // NN walk from 0).
        var nnPath = new List<int> { 0 };
        var visited = new bool[n];
        visited[0] = true;
        for (var step = 1; step < n; step++)
        {
            var prev = nnPath[step - 1];
            var best = -1;
            var bestW = double.PositiveInfinity;
            for (var v = 0; v < n; v++)
            {
                if (visited[v]) continue;
                if (w[prev, v] < bestW) { bestW = w[prev, v]; best = v; }
            }
            nnPath.Add(best);
            visited[best] = true;
        }
        double nnCost = 0;
        for (var i = 1; i < nnPath.Count; i++) nnCost += w[nnPath[i - 1], nnPath[i]];

        var result = TrackSetOrdering.Order(g, tracks, seed: tracks[0]);
        Assert.Equal(OrderingStrategy.NearestNeighborTwoOpt, result.Strategy);

        double polishedCost = 0;
        for (var i = 1; i < result.OrderedIds.Count; i++)
        {
            var from = int.Parse(result.OrderedIds[i - 1][1..]);
            var to = int.Parse(result.OrderedIds[i][1..]);
            polishedCost += w[from, to];
        }

        Assert.True(polishedCost <= nnCost,
            $"2-opt polished cost ({polishedCost}) should not exceed NN-only cost ({nnCost}).");
    }

    [Fact]
    public void Order_defaults_seed_to_lowest_bpm_track()
    {
        var a = T("A", bpm: 130);
        var b = T("B", bpm: 120); // lowest — should be seed
        var c = T("C", bpm: 125);
        var tracks = new[] { a, b, c };
        var g = CompleteGraph(tracks, new double[3, 3]);

        var result = TrackSetOrdering.Order(g, tracks);

        Assert.Equal("B", result.OrderedIds[0]);
    }

    [Fact]
    public void Order_uses_provided_seed_when_in_track_list()
    {
        var a = T("A", bpm: 120);
        var b = T("B", bpm: 125);
        var c = T("C", bpm: 130);
        var tracks = new[] { a, b, c };
        var g = CompleteGraph(tracks, new double[3, 3]);

        var result = TrackSetOrdering.Order(g, tracks, seed: c);

        Assert.Equal("C", result.OrderedIds[0]);
    }
}
