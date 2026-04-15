using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// A directed harmonic/BPM compatibility edge. Carries the classified relation plus tempo
/// metadata so callers can reason about the nature of the transition (or filter edges by type).
/// </summary>
public sealed class CompatibilityEdge : TrackEdge
{
    /// <summary>The harmonic relationship from <see cref="TrackEdge.Source"/> to <see cref="TrackEdge.Target"/>.</summary>
    public HarmonicRelation Relation { get; }

    /// <summary>Coarse tempo-proximity bucket.</summary>
    public BpmTier Tier { get; }

    /// <summary>Absolute percentage BPM delta used for classification. Already half/double-time aware.</summary>
    public double BpmDeltaPercent { get; }

    /// <summary>
    /// True when the best tempo match was found by comparing one track's BPM against the other's
    /// doubled or halved value (e.g. 96 vs 192). Callers may want to style or weight these
    /// transitions differently since they're situationally usable.
    /// </summary>
    public bool IsHalfTimeMatch { get; }

    /// <summary>Constructs a compatibility edge with a pre-computed weight.</summary>
    public CompatibilityEdge(
        Track source,
        Track target,
        HarmonicRelation relation,
        BpmTier tier,
        double bpmDeltaPercent,
        bool isHalfTimeMatch,
        double weight)
        : base(source, target, weight)
    {
        Relation = relation;
        Tier = tier;
        BpmDeltaPercent = bpmDeltaPercent;
        IsHalfTimeMatch = isHalfTimeMatch;
    }
}
