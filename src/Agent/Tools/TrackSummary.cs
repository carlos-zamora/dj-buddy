using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Agent.Tools;

/// <summary>
/// Shared helpers for projecting a <see cref="Track"/> to a summary object
/// and formatting values for tool responses.
/// </summary>
internal static class TrackSummary
{
    /// <summary>Projects a track to a concise summary object for tool responses.</summary>
    public static object Of(Track t) => new
    {
        trackId = t.TrackId,
        name = t.Name,
        artist = t.Artist,
        bpm = t.Bpm,
        key = t.Key,
        genre = t.Genre,
        rating = NormalizeRating(t.Rating),
        totalTime = FormatTime(t.TotalTime),
    };

    /// <summary>Converts the rekordbox 0–255 rating scale to 0–5 stars.</summary>
    internal static int NormalizeRating(int raw) =>
        raw <= 0 ? 0 : Math.Clamp(raw / 51, 1, 5);

    /// <summary>Formats a duration in seconds as "m:ss" or "h:mm:ss".</summary>
    internal static string FormatTime(int totalSeconds)
    {
        if (totalSeconds <= 0) return "0:00";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}
