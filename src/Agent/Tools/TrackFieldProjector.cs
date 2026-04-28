using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Shared field projection for <see cref="Track"/>. Owns the canonical field-name vocabulary,
/// case-insensitive normalization, and per-field value extraction so <c>get_tracks</c> (data
/// for the agent) and <c>display_tracks</c> (rendered for the user) stay in lockstep.
/// </summary>
internal static class TrackFieldProjector
{
    /// <summary>
    /// Canonical, lowercase-first field-name set supported across every projection tool. Tool
    /// descriptions reference this list verbatim; adding a field here is the only change needed
    /// to expose it through both projection paths.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedFields =
    [
        "name", "artist", "album", "genre", "bpm", "key", "tonality",
        "rating", "playCount", "totalTime", "dateAdded", "label",
        "remixer", "year", "kind", "bitRate", "comments",
    ];

    /// <summary>
    /// Normalizes a caller-supplied field list to canonical casing, drops unknowns silently
    /// (model typos shouldn't error a whole call), and dedupes. When <paramref name="fields"/>
    /// is null/empty/all-unknown, returns a sensible <c>["name","artist"]</c> default.
    /// </summary>
    public static List<string> Normalize(IReadOnlyList<string>? fields)
    {
        if (fields is null || fields.Count == 0)
            return ["name", "artist"];

        var supportedLookup = new HashSet<string>(SupportedFields, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(fields.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            if (!supportedLookup.TryGetValue(f, out var canonical))
                canonical = SupportedFields.FirstOrDefault(s =>
                    string.Equals(s, f, StringComparison.OrdinalIgnoreCase)) ?? "";
            if (canonical.Length == 0) continue;
            if (seen.Add(canonical)) result.Add(canonical);
        }

        return result.Count == 0 ? ["name", "artist"] : result;
    }

    /// <summary>
    /// Extracts <paramref name="field"/> from <paramref name="t"/> using the canonical name from
    /// <see cref="SupportedFields"/>. Returns <c>null</c> for unknown field names.
    /// </summary>
    public static object? Project(Track t, string field) => field switch
    {
        "name" => t.Name,
        "artist" => t.Artist,
        "album" => t.Album,
        "genre" => t.Genre,
        "bpm" => t.Bpm,
        "key" => t.Key,
        "tonality" => t.Tonality,
        "rating" => NormalizeRating(t.Rating),
        "playCount" => t.PlayCount,
        "totalTime" => FormatTime(t.TotalTime),
        "dateAdded" => t.DateAdded?.ToString("yyyy-MM-dd"),
        "label" => t.Label,
        "remixer" => t.Remixer,
        "year" => t.Year,
        "kind" => t.Kind,
        "bitRate" => t.BitRate,
        "comments" => t.Comments,
        _ => null,
    };

    /// <summary>
    /// Returns a human-friendly string form of <paramref name="field"/> for table cells. BPM and
    /// rating get specialized formatting; everything else falls through to <c>ToString</c>.
    /// </summary>
    public static string FormatForDisplay(Track t, string field)
    {
        return field switch
        {
            "bpm" => t.Bpm > 0 ? t.Bpm.ToString("0.#") : "—",
            "rating" => NormalizeRating(t.Rating) is var r && r > 0 ? new string('★', r) : "",
            _ => Project(t, field)?.ToString() ?? "",
        };
    }

    /// <summary>Converts the rekordbox 0–255 rating scale to 0–5 stars.</summary>
    private static int NormalizeRating(int raw) =>
        raw <= 0 ? 0 : Math.Clamp(raw / 51, 1, 5);

    /// <summary>Formats a duration in seconds as "m:ss" or "h:mm:ss".</summary>
    private static string FormatTime(int totalSeconds)
    {
        if (totalSeconds <= 0) return "0:00";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}
