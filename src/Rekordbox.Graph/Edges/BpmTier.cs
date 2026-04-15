namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// Coarse bucket for BPM proximity between two tracks, classified against configurable
/// thresholds on <see cref="TrackGraphOptions"/>.
/// </summary>
public enum BpmTier
{
    /// <summary>BPM delta is effectively zero (below a small epsilon).</summary>
    Same,

    /// <summary>Within <see cref="TrackGraphOptions.BpmCloseThreshold"/> percent.</summary>
    Close,

    /// <summary>Within <see cref="TrackGraphOptions.BpmMediumThreshold"/> percent.</summary>
    Medium,

    /// <summary>Within <see cref="TrackGraphOptions.BpmFarThreshold"/> percent. Pairs beyond this tier do not produce an edge.</summary>
    Far,
}
