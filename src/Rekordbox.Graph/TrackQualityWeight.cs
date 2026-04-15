using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// Vertex-quality term added to every edge's weight so graph algorithms naturally prefer
/// transitions that touch higher-rated and fresher tracks. Centralized so tuning changes
/// affect every edge type uniformly and can be unit-tested in isolation.
/// </summary>
public static class TrackQualityWeight
{
    /// <summary>
    /// Computes the quality weight contribution for an edge going <paramref name="source"/> →
    /// <paramref name="target"/>. Lower is better; the result is intended to be added directly
    /// to an edge's relation/tier base weight.
    /// </summary>
    /// <remarks>
    /// TODO(weight-tuning): currently averages rating and freshness across both endpoints.
    /// Consider weighting the target more heavily, since in a DJ set the "next" track carries
    /// more significance than the one you're leaving behind.
    /// </remarks>
    public static double Compute(Track source, Track target, TrackGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        // TODO(weight-tuning): rating in rekordbox XML is encoded 0/51/102/153/204/255 — we
        // normalize to 0..1 via division by 255 and invert so higher ratings reduce weight.
        var ratingScore = ((source.Rating + target.Rating) / 2.0) / 255.0;
        var ratingTerm = options.RatingWeight * (1.0 - Math.Clamp(ratingScore, 0.0, 1.0));

        // TODO(weight-tuning): freshness currently uses a linear days-since-added fallback when
        // no custom score is provided. Swap for a pluggable half-life / last-played model.
        var freshness = (Freshness(source, options) + Freshness(target, options)) / 2.0;
        var freshnessTerm = options.FreshnessWeight * (1.0 - Math.Clamp(freshness, 0.0, 1.0));

        return ratingTerm + freshnessTerm;
    }

    /// <summary>Returns a 0..1 freshness score for <paramref name="track"/>; 1 = freshest.</summary>
    private static double Freshness(Track track, TrackGraphOptions options)
    {
        if (options.FreshnessScore is { } custom)
            return custom(track);

        if (track.DateAdded is not { } added)
            return 0.0;

        // TODO(weight-tuning): a flat 365-day linear decay is arbitrary — anything older than a
        // year collapses to zero freshness. Real libraries should use a half-life or usage signal.
        var days = (DateTime.UtcNow - added.ToUniversalTime()).TotalDays;
        if (days < 0) days = 0;
        const double windowDays = 365.0;
        return Math.Max(0.0, 1.0 - (days / windowDays));
    }
}
