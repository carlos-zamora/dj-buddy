namespace DJBuddy.Rekordbox.Models;

/// <summary>
/// A cue point or loop inside a <c>TRACK</c> element, corresponding to a rekordbox
/// <c>&lt;POSITION_MARK&gt;</c> child.
/// </summary>
public class CuePoint
{
    /// <summary>User-visible label for the cue.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Cue type. DJBuddy.Rekordbox uses: <c>0</c>=cue, <c>1</c>=fade-in, <c>2</c>=fade-out,
    /// <c>3</c>=load, <c>4</c>=loop.
    /// </summary>
    public int Type { get; set; }

    /// <summary>Start position in seconds.</summary>
    public double Start { get; set; }

    /// <summary>End position in seconds. Only set for loops (<see cref="Type"/> = 4).</summary>
    public double? End { get; set; }

    /// <summary>
    /// Hot cue number (<c>0..7</c>) when assigned to a hot cue, or <c>-1</c> for a memory cue.
    /// Null when the attribute is missing from the XML.
    /// </summary>
    public int? Num { get; set; }

    /// <summary>Red component of the cue color (0-255).</summary>
    public int Red { get; set; }

    /// <summary>Green component of the cue color (0-255).</summary>
    public int Green { get; set; }

    /// <summary>Blue component of the cue color (0-255).</summary>
    public int Blue { get; set; }
}
