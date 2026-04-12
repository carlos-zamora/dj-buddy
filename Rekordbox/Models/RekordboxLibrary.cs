namespace Rekordbox.Models;

/// <summary>
/// The parsed contents of a rekordbox.xml file, containing the track collection
/// and the playlist/folder tree.
/// </summary>
public class RekordboxLibrary
{
    /// <summary>All tracks in the library, keyed by <see cref="Track.TrackId"/>.</summary>
    public Dictionary<string, Track> Tracks { get; set; } = [];

    /// <summary>The root node of the playlist tree. Its children are the top-level folders/playlists.</summary>
    public PlaylistNode Root { get; set; } = new() { Name = "ROOT", IsFolder = true };
}
