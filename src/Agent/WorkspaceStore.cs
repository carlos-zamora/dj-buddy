using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;

namespace DJBuddy.Agent;

/// <summary>
/// Per-session state for the DJ Buddy agent. Holds the parsed <see cref="RekordboxLibrary"/>
/// plus a dictionary of named <see cref="TrackSet"/> workspaces that the agent builds, filters,
/// orders, and eventually commits into the <c>DJ_BUDDY</c> folder for export.
/// </summary>
/// <remarks>
/// Workspaces live in process memory only — they are not part of the Copilot conversation
/// context, so large filter/union operations stay cheap in tokens. The agent refers to them by
/// name; tools return IDs + counts, not full track lists.
/// </remarks>
internal sealed class WorkspaceStore
{
    private readonly Dictionary<string, TrackSet> _workspaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The parent library; set operations and projections all resolve against it.</summary>
    public RekordboxLibrary Library { get; }

    /// <summary>
    /// Folder that export injects into rekordbox.xml. Committed workspaces are materialized as
    /// <see cref="PlaylistNode"/> children here.
    /// </summary>
    public PlaylistNode DjBuddyFolder { get; } = new()
    {
        Name = "DJ_BUDDY",
        IsFolder = true,
        Children = [],
        TrackKeys = [],
    };

    /// <summary>Creates a new workspace store bound to <paramref name="library"/>.</summary>
    public WorkspaceStore(RekordboxLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        Library = library;
    }

    /// <summary>True when at least one committed playlist contains at least one track.</summary>
    public bool HasAnyTracks => DjBuddyFolder.Children.Any(c => c.TrackKeys.Count > 0);

    /// <summary>All workspaces in creation order.</summary>
    public IEnumerable<TrackSet> Workspaces => _workspaces.Values;

    /// <summary>
    /// Creates a new empty workspace with the given name (case-insensitive uniqueness).
    /// </summary>
    /// <returns><c>(true, workspace)</c> on success; <c>(false, error)</c> otherwise.</returns>
    public (bool Ok, string Message, TrackSet? Workspace) Create(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
            return (false, "Workspace name cannot be empty.", null);

        if (_workspaces.ContainsKey(name))
            return (false, $"A workspace named '{name}' already exists.", null);

        var ws = new TrackSet(name, Library);
        _workspaces[name] = ws;
        return (true, name, ws);
    }

    /// <summary>
    /// Retrieves an existing workspace by case-insensitive name.
    /// </summary>
    public TrackSet? Get(string name) =>
        name is null ? null : _workspaces.GetValueOrDefault(name);

    /// <summary>
    /// Retrieves an existing workspace, or creates an empty one if none exists with the given
    /// name. Used by tools that fold search results into a workspace under "union"/"replace"
    /// semantics where the caller shouldn't need a separate create step.
    /// </summary>
    public TrackSet GetOrCreate(string name)
    {
        var existing = Get(name);
        if (existing is not null) return existing;

        var ws = new TrackSet(name, Library);
        _workspaces[name] = ws;
        return ws;
    }

    /// <summary>Removes a workspace. Returns <c>true</c> if the workspace existed and was removed.</summary>
    public bool Delete(string name) => _workspaces.Remove(name);

    /// <summary>
    /// Renames a workspace. Returns <c>(false, reason)</c> when the source doesn't exist or the
    /// target name is blank / already taken.
    /// </summary>
    public (bool Ok, string Message) Rename(string oldName, string newName)
    {
        newName = newName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(newName))
            return (false, "New workspace name cannot be empty.");

        if (!_workspaces.TryGetValue(oldName, out var ws))
            return (false, $"Workspace '{oldName}' not found.");

        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)
            && _workspaces.ContainsKey(newName))
        {
            return (false, $"A workspace named '{newName}' already exists.");
        }

        _workspaces.Remove(oldName);
        ws.Name = newName;
        _workspaces[newName] = ws;
        return (true, newName);
    }

    /// <summary>
    /// Commits a workspace into the <see cref="DjBuddyFolder"/> as a <see cref="PlaylistNode"/>.
    /// Ordered workspaces preserve their sequence; unordered ones materialize in hash-set order
    /// (which is arbitrary but stable within a process run).
    /// </summary>
    /// <param name="workspaceName">Name of the workspace to commit.</param>
    /// <param name="playlistName">
    /// Optional playlist name. When null or blank, the workspace name is reused. If a playlist
    /// with the resulting name already exists under <see cref="DjBuddyFolder"/>, it is replaced.
    /// </param>
    /// <returns><c>(true, playlistName, trackCount)</c> on success; <c>(false, error, 0)</c> otherwise.</returns>
    public (bool Ok, string Message, int Count) Commit(string workspaceName, string? playlistName = null)
    {
        if (!_workspaces.TryGetValue(workspaceName, out var ws))
            return (false, $"Workspace '{workspaceName}' not found.", 0);

        var name = string.IsNullOrWhiteSpace(playlistName) ? ws.Name : playlistName.Trim();

        var trackKeys = ws.Ids.ToList();
        var node = new PlaylistNode
        {
            Name = name,
            IsFolder = false,
            Children = [],
            TrackKeys = trackKeys,
        };

        var existing = DjBuddyFolder.Children.FindIndex(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            DjBuddyFolder.Children[existing] = node;
        else
            DjBuddyFolder.Children.Add(node);

        return (true, name, trackKeys.Count);
    }
}
