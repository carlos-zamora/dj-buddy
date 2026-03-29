namespace dj_buddy.Models;

/// <summary>
/// Represents a single track from the rekordbox COLLECTION.
/// </summary>
public class Track
{
    public string TrackId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public double Bpm { get; set; }

    /// <summary>
    /// The musical key in alphanumeric notation (e.g. "8A", "11B"), sourced from the Tonality attribute.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Formatted BPM for display. Returns an em-dash when BPM is zero or unset.
    /// </summary>
    public string BpmDisplay => Bpm > 0 ? Bpm.ToString("0.##") : "—";

    /// <summary>
    /// Formatted key for display. Returns an em-dash when the key is empty.
    /// </summary>
    public string KeyDisplay => string.IsNullOrEmpty(Key) ? "—" : Key;
}
