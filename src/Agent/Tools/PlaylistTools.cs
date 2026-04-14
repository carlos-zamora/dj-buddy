using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Static methods that implement Copilot SDK tools for managing the agent's
/// in-session playlist collection. Each method is registered via
/// <c>AIFunctionFactory.Create</c> with a closure capturing the store and library.
/// </summary>
internal static class PlaylistTools
{
    /// <summary>
    /// Creates a new named playlist in the agent's DJ_BUDDY folder.
    /// </summary>
    /// <returns>
    /// <c>{ ok, playlistName }</c> on success; <c>{ error }</c> on failure.
    /// </returns>
    public static object CreatePlaylist(AgentPlaylistStore store, string name)
    {
        var (ok, message) = store.CreatePlaylist(name);
        return ok
            ? new { ok = true, playlistName = message }
            : new { ok = false, error = message };
    }

    /// <summary>
    /// Adds a track to a named agent playlist by its track ID. Validates that the
    /// track ID exists in the library before adding.
    /// </summary>
    /// <returns>
    /// <c>{ ok, playlistName, trackId, totalTracks }</c> on success;
    /// <c>{ ok, message }</c> if already present; <c>{ error }</c> on failure.
    /// </returns>
    public static object AddTrackToPlaylist(
        AgentPlaylistStore store,
        RekordboxLibrary library,
        string playlistName,
        string trackId)
    {
        if (!library.Tracks.ContainsKey(trackId))
            return new { error = $"Track ID '{trackId}' not found in library." };

        var (ok, message) = store.AddTrack(playlistName, trackId);
        if (!ok)
            return new { error = message };

        if (message == "already_present")
            return new { ok = true, message = $"Track '{trackId}' is already in playlist '{playlistName}'." };

        var playlist = store.GetPlaylists()
            .FirstOrDefault(p => string.Equals(p.Name, playlistName, StringComparison.OrdinalIgnoreCase));

        return new
        {
            ok = true,
            playlistName,
            trackId,
            totalTracks = playlist?.TrackKeys.Count ?? 0,
        };
    }

    /// <summary>
    /// Removes a track from a named agent playlist.
    /// </summary>
    /// <returns>
    /// <c>{ ok, playlistName, trackId }</c> on success; <c>{ error }</c> on failure.
    /// </returns>
    public static object RemoveTrackFromPlaylist(
        AgentPlaylistStore store,
        string playlistName,
        string trackId)
    {
        var (ok, message) = store.RemoveTrack(playlistName, trackId);
        return ok
            ? new { ok = true, playlistName, trackId }
            : new { ok = false, error = message };
    }

    /// <summary>
    /// Lists all agent-created playlists with their track counts and full track details.
    /// </summary>
    /// <returns>
    /// <c>{ playlistCount, playlists: [{ name, trackCount, tracks: [...] }] }</c>
    /// </returns>
    public static object ListAgentPlaylists(AgentPlaylistStore store, RekordboxLibrary library)
    {
        var playlists = store.GetPlaylists()
            .Select(p => new
            {
                name = p.Name,
                trackCount = p.TrackKeys.Count,
                tracks = p.GetTracks(library).Select(TrackSummary.Of).ToList(),
            })
            .ToList();

        return new { playlistCount = playlists.Count, playlists };
    }
}
