using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// Configuration for <see cref="TrackGraphBuilder"/>. Thresholds and weight knobs are
/// intentionally exposed so callers — and future tuning experiments — can adjust behavior
/// without touching builder code.
/// </summary>
/// <remarks>
/// TODO(weight-tuning): every default constant here is provisional. The intent is to tune these
/// against real-world DJ set feedback; do not treat the numbers as load-bearing.
/// </remarks>
public sealed record TrackGraphOptions
{
    /// <summary>
    /// Absolute cap on BPM delta (percent). Pairs further apart than this produce no
    /// compatibility edge. Default 16.0, matching the <see cref="BpmFarThreshold"/>.
    /// </summary>
    public double BpmTolerancePercent { get; init; } = 16.0;

    /// <summary>Upper bound (inclusive) for <see cref="BpmTier.Close"/>.</summary>
    public double BpmCloseThreshold { get; init; } = 6.0;

    /// <summary>Upper bound (inclusive) for <see cref="BpmTier.Medium"/>.</summary>
    public double BpmMediumThreshold { get; init; } = 10.0;

    /// <summary>Upper bound (inclusive) for <see cref="BpmTier.Far"/> and the overall edge cutoff.</summary>
    public double BpmFarThreshold { get; init; } = 16.0;

    /// <summary>Delta below this percent is treated as <see cref="BpmTier.Same"/>.</summary>
    public double BpmSameEpsilon { get; init; } = 0.25;

    /// <summary>Whether to treat same-number different-letter pairs (e.g. 6A↔6B) as <see cref="HarmonicRelation.Adjacent"/>.</summary>
    public bool AllowRelativeMajorMinor { get; init; } = true;

    /// <summary>Whether <see cref="TrackGraphBuilder.Build"/> should add harmonic/BPM edges.</summary>
    public bool IncludeCompatibilityEdges { get; init; } = true;

    /// <summary>Whether <see cref="TrackGraphBuilder.Build"/> should add playlist co-occurrence edges.</summary>
    public bool IncludeCoOccurrenceEdges { get; init; } = true;

    /// <summary>Whether compatibility edges may be emitted when one BPM is roughly double the other (e.g. 96↔192).</summary>
    public bool AllowHalfTimeMatch { get; init; } = true;

    /// <summary>Scaling factor applied to the rating component of vertex-quality weight adjustments.</summary>
    public double RatingWeight { get; init; } = 1.0;

    /// <summary>Scaling factor applied to the freshness (date-added) component of vertex-quality weight adjustments.</summary>
    public double FreshnessWeight { get; init; } = 1.0;

    /// <summary>
    /// Optional custom freshness score for a track (higher = fresher / more preferred). When
    /// <c>null</c>, <see cref="TrackQualityWeight"/> falls back to a linear-by-days-since-added
    /// function anchored at <see cref="Track.DateAdded"/>.
    /// </summary>
    /// <remarks>
    /// TODO: replace the default linear freshness with a more nuanced model (half-life decay,
    /// last-played integration, novelty vs. staple differentiation). The pluggable function is
    /// the long-term extension point — consumers can supply any scoring curve they like.
    /// </remarks>
    public Func<Track, double>? FreshnessScore { get; init; }

    /// <summary>Base weight contribution per harmonic relation. Lower = preferred.</summary>
    /// <remarks>TODO(weight-tuning): the Same &lt; Adjacent &lt; Energy* ordering is correct in spirit but the magnitudes are guesses.</remarks>
    public double RelationWeightSame { get; init; } = 0.0;

    /// <inheritdoc cref="RelationWeightSame"/>
    public double RelationWeightAdjacent { get; init; } = 1.0;

    /// <inheritdoc cref="RelationWeightSame"/>
    public double RelationWeightEnergyBoost { get; init; } = 2.0;

    /// <inheritdoc cref="RelationWeightSame"/>
    public double RelationWeightEnergyDrop { get; init; } = 2.0;

    /// <summary>Base weight contribution per BPM tier.</summary>
    /// <remarks>TODO(weight-tuning): the step values here are provisional.</remarks>
    public double TierWeightSame { get; init; } = 0.0;

    /// <inheritdoc cref="TierWeightSame"/>
    public double TierWeightClose { get; init; } = 0.5;

    /// <inheritdoc cref="TierWeightSame"/>
    public double TierWeightMedium { get; init; } = 1.5;

    /// <inheritdoc cref="TierWeightSame"/>
    public double TierWeightFar { get; init; } = 3.0;

    /// <summary>
    /// Extra weight penalty added to compatibility edges whose tempo match relied on half/double
    /// time. These transitions are usable but more situational than a direct BPM match.
    /// </summary>
    /// <remarks>TODO(weight-tuning): zero by default; consider a small positive penalty once tested with real sets.</remarks>
    public double HalfTimePenalty { get; init; } = 0.0;
}
