using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Copilot SDK tools that render data directly to the console rather than returning it through
/// the LLM transcript. The split between <c>get_tracks</c> (data for the agent's reasoning) and
/// <c>display_tracks</c> (rendered for the user) keeps formatting in the harness where Spectre
/// can do it well, and keeps the model from re-emitting tables that have already been drawn.
/// </summary>
internal static class DisplayTools
{
    /// <summary>
    /// Resolves <paramref name="trackIds"/> against the library and renders them as a Spectre
    /// table via <see cref="ConsoleUi.RenderTrackTable"/>. Missing IDs are dropped from the
    /// table and reported back to the agent so it can self-correct (e.g. drop stale IDs from
    /// a workspace before re-displaying).
    /// </summary>
    /// <param name="library">The parent library; supplies the ID → <see cref="Track"/> resolution.</param>
    /// <param name="trackIds">Track IDs to display. Order is preserved — agents passing ordered set output get the canonical order on screen.</param>
    /// <param name="fields">
    /// Optional column list from <see cref="TrackFieldProjector.SupportedFields"/>. Defaults to
    /// <c>artist, name, bpm, key</c> when null/empty — the columns DJs glance at first.
    /// </param>
    /// <param name="title">Optional caption printed above the table.</param>
    /// <returns>
    /// <c>{ ok, displayed, missingIds }</c>. The full row data is intentionally NOT returned —
    /// the user already saw the table, so re-emitting it would just bloat the transcript.
    /// </returns>
    public static object DisplayTracks(
        RekordboxLibrary library,
        IReadOnlyList<string> trackIds,
        IReadOnlyList<string>? fields,
        string? title)
    {
        if (trackIds is null || trackIds.Count == 0)
            return new { ok = false, error = "No track IDs supplied." };

        // Default columns target the at-a-glance DJ view. Caller can override with explicit fields.
        var resolvedFields = fields is null || fields.Count == 0
            ? (IReadOnlyList<string>)["artist", "name", "bpm", "key"]
            : TrackFieldProjector.Normalize(fields);

        var resolved = new List<Track>(trackIds.Count);
        var missing = new List<string>();
        foreach (var id in trackIds)
        {
            if (library.Tracks.TryGetValue(id, out var t))
                resolved.Add(t);
            else
                missing.Add(id);
        }

        ConsoleUi.RenderTrackTable(resolved, resolvedFields, title);

        return new
        {
            ok = true,
            displayed = resolved.Count,
            missingIds = missing,
        };
    }
}
