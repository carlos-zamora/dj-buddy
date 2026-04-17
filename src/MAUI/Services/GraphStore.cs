using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using QuikGraph;

namespace DJBuddy.MAUI.Services;

/// <summary>
/// Static holder for the shared <see cref="BidirectionalGraph{TVertex, TEdge}"/> built from
/// the currently loaded library. The graph is expensive to construct, so we build it exactly
/// once per loaded library on a background thread; consumers <c>await</c> the resulting
/// <see cref="GraphTask"/> rather than blocking the UI thread.
/// </summary>
/// <remarks>
/// Mirrors the Agent's pattern: kick off the build right after library load so it overlaps
/// with UI work, and let every consumer await the single shared task.
/// </remarks>
public static class GraphStore
{
    /// <summary>
    /// A task that resolves to the shared graph for the currently loaded library, or
    /// <c>null</c> if no library has been loaded yet (or if the previous build was reset).
    /// </summary>
    public static Task<BidirectionalGraph<Track, TrackEdge>>? GraphTask { get; private set; }

    /// <summary>
    /// Starts building the graph for <paramref name="library"/> on a background thread and
    /// assigns the resulting task to <see cref="GraphTask"/>. Safe to call immediately after
    /// <c>LibraryStore.Library</c> is assigned — construction overlaps with UI work.
    /// </summary>
    public static void BeginBuild(RekordboxLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        GraphTask = Task.Run(() => TrackGraphBuilder.Build(library));
    }

    /// <summary>Clears the current graph task. Call before loading a new library.</summary>
    public static void Reset() => GraphTask = null;
}
