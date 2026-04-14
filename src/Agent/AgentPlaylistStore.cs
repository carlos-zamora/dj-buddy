using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Agent;

/// <summary>
/// Manages the agent's in-session playlist collection as an in-memory
/// <c>DJ_BUDDY</c> folder node. State exists only for the duration of the session —
/// users export via <c>/export</c> to persist playlists into rekordbox.xml.
/// </summary>
internal sealed class AgentPlaylistStore
{
    /// <summary>The root folder node that will be injected into rekordbox.xml on export.</summary>
    public PlaylistNode DjBuddyFolder { get; } = new()
    {
        Name = "DJ_BUDDY",
        IsFolder = true,
        Children = [],
        TrackKeys = [],
    };

    /// <summary>Returns true when at least one playlist contains at least one track.</summary>
    public bool HasAnyTracks => DjBuddyFolder.Children.Any(c => c.TrackKeys.Count > 0);

    /// <summary>
    /// Creates a new named playlist in the DJ_BUDDY folder.
    /// </summary>
    /// <returns>
    /// <c>(true, name)</c> on success; <c>(false, reason)</c> if the name is blank or already exists.
    /// </returns>
    public (bool Ok, string Message) CreatePlaylist(string name)
    {
        name = name?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(name))
            return (false, "Playlist name cannot be empty.");

        if (DjBuddyFolder.Children.Any(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"A playlist named '{name}' already exists.");

        DjBuddyFolder.Children.Add(new PlaylistNode
        {
            Name = name,
            IsFolder = false,
            Children = [],
            TrackKeys = [],
        });

        return (true, name);
    }

    /// <summary>
    /// Adds a track key to a named playlist.
    /// </summary>
    /// <returns>
    /// <c>(true, ...)</c> on success or if the track is already present (idempotent);
    /// <c>(false, reason)</c> if the playlist does not exist.
    /// </returns>
    public (bool Ok, string Message) AddTrack(string playlistName, string trackId)
    {
        var node = FindPlaylist(playlistName);
        if (node is null)
            return (false, $"Playlist '{playlistName}' not found. Use create_playlist first.");

        if (node.TrackKeys.Contains(trackId))
            return (true, "already_present");

        node.TrackKeys.Add(trackId);
        return (true, "added");
    }

    /// <summary>
    /// Removes a track key from a named playlist.
    /// </summary>
    /// <returns>
    /// <c>(true, ...)</c> on success; <c>(false, reason)</c> if the playlist or track is not found.
    /// </returns>
    public (bool Ok, string Message) RemoveTrack(string playlistName, string trackId)
    {
        var node = FindPlaylist(playlistName);
        if (node is null)
            return (false, $"Playlist '{playlistName}' not found.");

        if (!node.TrackKeys.Remove(trackId))
            return (false, $"Track '{trackId}' is not in playlist '{playlistName}'.");

        return (true, "removed");
    }

    /// <summary>All playlists in the DJ_BUDDY folder.</summary>
    public IReadOnlyList<PlaylistNode> GetPlaylists() => DjBuddyFolder.Children;

    private PlaylistNode? FindPlaylist(string name) =>
        DjBuddyFolder.Children.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
