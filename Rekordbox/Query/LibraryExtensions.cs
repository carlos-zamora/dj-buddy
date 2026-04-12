using Rekordbox.Models;

namespace Rekordbox.Query;

/// <summary>
/// Library-level lookup and traversal helpers.
/// </summary>
public static class LibraryExtensions
{
    /// <summary>
    /// Resolves the <see cref="PlaylistNode.TrackKeys"/> on a playlist node to <see cref="Track"/>
    /// objects via the given library. Track IDs that the library does not contain are silently
    /// skipped — matching the <c>Dictionary.GetValueOrDefault</c> pattern used throughout the
    /// MAUI app.
    /// </summary>
    /// <param name="playlist">The playlist node whose tracks should be resolved.</param>
    /// <param name="library">The library holding the track collection.</param>
    /// <returns>Tracks in playlist order, with missing IDs skipped.</returns>
    public static IEnumerable<Track> GetTracks(this PlaylistNode playlist, RekordboxLibrary library)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(library);

        foreach (var id in playlist.TrackKeys)
        {
            if (library.Tracks.TryGetValue(id, out var track))
                yield return track;
        }
    }

    /// <summary>
    /// Depth-first walk returning every non-folder (playlist) node under <paramref name="root"/>,
    /// excluding <paramref name="root"/> itself. Useful for flattening a library into a list of
    /// all playlists.
    /// </summary>
    public static IEnumerable<PlaylistNode> EnumeratePlaylists(this PlaylistNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var stack = new Stack<PlaylistNode>();
        // Push children in reverse so the first child is visited first (pre-order DFS).
        for (var i = root.Children.Count - 1; i >= 0; i--)
            stack.Push(root.Children[i]);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!node.IsFolder)
            {
                yield return node;
            }
            else
            {
                for (var i = node.Children.Count - 1; i >= 0; i--)
                    stack.Push(node.Children[i]);
            }
        }
    }

    /// <summary>
    /// Depth-first search for a node (folder or playlist) with the given <paramref name="name"/>
    /// under <paramref name="root"/>. Returns the first match, or <c>null</c> if none is found.
    /// </summary>
    public static PlaylistNode? FindByName(this PlaylistNode root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(name);

        foreach (var child in root.Children)
        {
            if (child.Name == name)
                return child;

            if (child.IsFolder)
            {
                var found = child.FindByName(name);
                if (found != null)
                    return found;
            }
        }

        return null;
    }
}
