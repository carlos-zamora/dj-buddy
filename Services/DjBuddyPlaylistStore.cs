using System.Text.Json;
using dj_buddy.Models;

namespace dj_buddy.Services;

/// <summary>
/// Static store for DJ Buddy playlists (favorites and doubles).
/// Manages a <c>DJ_BUDDY</c> folder node persisted as JSON in app data.
/// </summary>
public static class DjBuddyPlaylistStore
{
    private static readonly string FilePath =
        Path.Combine(FileSystem.AppDataDirectory, "dj_buddy_playlists.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// The root DJ_BUDDY folder containing Favorites and user-created playlists.
    /// </summary>
    public static PlaylistNode DjBuddyFolder { get; private set; } = CreateDefault();

    /// <summary>
    /// Shortcut to the Favorites playlist (always the first child).
    /// </summary>
    public static PlaylistNode Favorites => DjBuddyFolder.Children[0];

    /// <summary>
    /// Loads playlists from the JSON file, or creates defaults if none exist.
    /// </summary>
    public static async Task LoadAsync()
    {
        if (!File.Exists(FilePath))
        {
            DjBuddyFolder = CreateDefault();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            var loaded = JsonSerializer.Deserialize<PlaylistNode>(json, JsonOptions);
            if (loaded != null && loaded.Children.Count > 0)
            {
                DjBuddyFolder = loaded;
                return;
            }
        }
        catch
        {
            // Corrupt file — fall through to defaults
        }

        DjBuddyFolder = CreateDefault();
    }

    /// <summary>
    /// Persists the current DJ_BUDDY folder to JSON.
    /// </summary>
    public static async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(DjBuddyFolder, JsonOptions);
        await File.WriteAllTextAsync(FilePath, json);
    }

    /// <summary>
    /// Adds a track to the Favorites playlist if not already present.
    /// </summary>
    /// <param name="trackId">The track's <c>TrackID</c> from the rekordbox collection.</param>
    public static void AddTrackToFavorites(string trackId)
    {
        AddTrackToPlaylist("Favorites", trackId);
    }

    /// <summary>
    /// Adds a track to the named playlist if not already present.
    /// </summary>
    /// <param name="playlistName">Name of the target playlist.</param>
    /// <param name="trackId">The track's <c>TrackID</c>.</param>
    public static void AddTrackToPlaylist(string playlistName, string trackId)
    {
        var playlist = DjBuddyFolder.Children
            .FirstOrDefault(c => c.Name == playlistName);
        if (playlist == null) return;

        if (!playlist.TrackKeys.Contains(trackId))
            playlist.TrackKeys.Add(trackId);
    }

    /// <summary>
    /// Creates a new playlist under the DJ_BUDDY folder.
    /// </summary>
    /// <param name="name">Playlist name.</param>
    /// <returns>The newly created playlist node.</returns>
    public static PlaylistNode CreatePlaylist(string name)
    {
        var playlist = new PlaylistNode { Name = name, IsFolder = false };
        DjBuddyFolder.Children.Add(playlist);
        return playlist;
    }

    /// <summary>
    /// Renames an existing playlist. Cannot rename Favorites.
    /// </summary>
    /// <param name="oldName">Current playlist name.</param>
    /// <param name="newName">New playlist name.</param>
    /// <returns><c>true</c> if the playlist was found and renamed.</returns>
    public static bool RenamePlaylist(string oldName, string newName)
    {
        if (oldName == "Favorites") return false;
        var playlist = DjBuddyFolder.Children.FirstOrDefault(c => c.Name == oldName);
        if (playlist == null) return false;
        playlist.Name = newName;
        return true;
    }

    /// <summary>
    /// Removes a playlist from the DJ_BUDDY folder. Cannot remove Favorites.
    /// </summary>
    /// <param name="name">Playlist name to remove.</param>
    /// <returns><c>true</c> if the playlist was found and removed.</returns>
    public static bool RemovePlaylist(string name)
    {
        if (name == "Favorites") return false;
        var playlist = DjBuddyFolder.Children.FirstOrDefault(c => c.Name == name);
        if (playlist == null) return false;
        DjBuddyFolder.Children.Remove(playlist);
        return true;
    }

    /// <summary>
    /// Returns the names of all user-created playlists (excludes Favorites).
    /// </summary>
    public static List<string> GetPlaylistNames()
    {
        return DjBuddyFolder.Children
            .Where(c => c.Name != "Favorites")
            .Select(c => c.Name)
            .ToList();
    }

    private static PlaylistNode CreateDefault()
    {
        return new PlaylistNode
        {
            Name = "DJ_BUDDY",
            IsFolder = true,
            Children =
            [
                new PlaylistNode { Name = "Favorites", IsFolder = false },
            ],
        };
    }
}
