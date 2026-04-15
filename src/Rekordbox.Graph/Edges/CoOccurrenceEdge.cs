using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// A directed playlist co-occurrence edge. Symmetric by nature — the builder emits one instance
/// in each direction (A→B and B→A) with the same <see cref="PlaylistCount"/>.
/// </summary>
public sealed class CoOccurrenceEdge : TrackEdge
{
    /// <summary>Number of playlists in which both the source and target tracks appear.</summary>
    public int PlaylistCount { get; }

    /// <summary>Constructs a co-occurrence edge with a pre-computed weight.</summary>
    public CoOccurrenceEdge(Track source, Track target, int playlistCount, double weight)
        : base(source, target, weight)
    {
        PlaylistCount = playlistCount;
    }
}
