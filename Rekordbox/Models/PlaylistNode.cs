namespace Rekordbox.Models;

/// <summary>
/// A node in the rekordbox playlist tree. A node is either a folder
/// (<c>Type="0"</c>, contains child nodes) or a playlist (<c>Type="1"</c>, contains track references).
/// </summary>
public class PlaylistNode
{
    public string Name { get; set; } = "";

    /// <summary>True if this node is a folder (<c>Type="0"</c>), false if it is a playlist (<c>Type="1"</c>).</summary>
    public bool IsFolder { get; set; }

    /// <summary>Child nodes (sub-folders or playlists). Only populated for folder nodes.</summary>
    public List<PlaylistNode> Children { get; set; } = [];

    /// <summary>Track IDs (<c>Key</c> attribute values) referencing tracks in the <c>COLLECTION</c>.
    /// Only populated for playlist nodes.</summary>
    public List<string> TrackKeys { get; set; } = [];
}
