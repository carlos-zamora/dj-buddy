namespace dj_buddy.Models;

/// <summary>
/// Represents a node in the rekordbox playlist tree. A node is either a folder
/// (Type="0", contains child nodes) or a playlist (Type="1", contains track references).
/// </summary>
public class PlaylistNode
{
    public string Name { get; set; } = "";

    /// <summary>
    /// True if this node is a folder (Type="0"), false if it is a playlist (Type="1").
    /// </summary>
    public bool IsFolder { get; set; }

    /// <summary>
    /// Child nodes (sub-folders or playlists). Only populated for folder nodes.
    /// </summary>
    public List<PlaylistNode> Children { get; set; } = [];

    /// <summary>
    /// Track IDs (Key attribute values) referencing tracks in the COLLECTION.
    /// Only populated for playlist nodes.
    /// </summary>
    public List<string> TrackKeys { get; set; } = [];
}
