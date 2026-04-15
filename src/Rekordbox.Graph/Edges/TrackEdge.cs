using DJBuddy.Rekordbox.Models;
using QuikGraph;

namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// Base class for all directed edges in a track graph. <see cref="Source"/> → <see cref="Target"/>
/// is meaningful — asymmetric relationships (e.g. energy boosts) are expressed by emitting only
/// the forward edge; symmetric ones by emitting two opposing edges.
/// </summary>
public abstract class TrackEdge : IEdge<Track>
{
    /// <inheritdoc/>
    public Track Source { get; }

    /// <inheritdoc/>
    public Track Target { get; }

    /// <summary>
    /// Composite edge weight. Lower values indicate stronger / more preferred transitions so that
    /// shortest-path algorithms naturally select better sequences. Concrete subclasses compute
    /// this from relation-specific factors plus vertex-quality adjustments.
    /// </summary>
    public double Weight { get; }

    /// <summary>Initializes the base edge fields.</summary>
    protected TrackEdge(Track source, Track target, double weight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Source = source;
        Target = target;
        Weight = weight;
    }
}
