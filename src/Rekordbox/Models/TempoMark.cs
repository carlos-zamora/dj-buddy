namespace DJBuddy.Rekordbox.Models;

/// <summary>
/// A single beatgrid entry inside a <c>TRACK</c> element, corresponding to a rekordbox
/// <c>&lt;TEMPO&gt;</c> child. Multiple marks describe a variable-tempo track.
/// </summary>
public class TempoMark
{
    /// <summary>Start time of this tempo segment, in seconds (rekordbox's "Inizio").</summary>
    public double Inizio { get; set; }

    /// <summary>BPM for this segment.</summary>
    public double Bpm { get; set; }

    /// <summary>Time signature, e.g. <c>"4/4"</c>.</summary>
    public string Metro { get; set; } = "";

    /// <summary>Beat number within the bar at <see cref="Inizio"/>.</summary>
    public int Battito { get; set; }
}
