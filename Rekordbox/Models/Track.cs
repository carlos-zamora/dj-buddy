namespace Rekordbox.Models;

/// <summary>
/// A single track from the rekordbox <c>COLLECTION</c>. Models every attribute on the
/// <c>&lt;TRACK&gt;</c> element plus its nested beatgrid and cue-point children.
/// </summary>
public class Track
{
    // ----- Identity -----

    /// <summary>The <c>TrackID</c> attribute — primary key, referenced by playlist entries.</summary>
    public string TrackId { get; set; } = "";

    // ----- Metadata -----

    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Composer { get; set; } = "";
    public string Album { get; set; } = "";
    public string Grouping { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Remixer { get; set; } = "";
    public string Label { get; set; } = "";
    public string Mix { get; set; } = "";
    public string Comments { get; set; } = "";

    public int? Year { get; set; }
    public int? DiscNumber { get; set; }
    public int? TrackNumber { get; set; }

    /// <summary>0-5 star rating. Rekordbox encodes stars as 0/51/102/153/204/255 in XML,
    /// but this library exposes the raw attribute value — callers can normalize if they wish.</summary>
    public int Rating { get; set; }

    public int PlayCount { get; set; }

    // ----- Audio technical -----

    /// <summary>File format description, e.g. <c>"WAV File"</c>, <c>"MP3 File"</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>File size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Track duration in seconds.</summary>
    public int TotalTime { get; set; }

    public int BitRate { get; set; }
    public int SampleRate { get; set; }

    // ----- Key / BPM -----

    /// <summary>Average BPM parsed from the <c>AverageBpm</c> attribute.</summary>
    public double Bpm { get; set; }

    /// <summary>Raw <c>Tonality</c> attribute as it appears in rekordbox, e.g. <c>"Gm"</c>, <c>"F#"</c>.
    /// Preserved alongside <see cref="Key"/> so consumers that need the original notation still have it.</summary>
    public string Tonality { get; set; } = "";

    /// <summary>Camelot-wheel notation derived from <see cref="Tonality"/> via
    /// <see cref="Xml.KeyConverter.ToCamelotNotation"/>, e.g. <c>"6A"</c>.</summary>
    public string Key { get; set; } = "";

    // ----- File -----

    /// <summary>File URL, typically <c>file://localhost/…</c>.</summary>
    public string Location { get; set; } = "";

    // ----- Dates -----

    public DateTime? DateAdded { get; set; }
    public DateTime? DateModified { get; set; }

    // ----- Nested analysis data -----

    /// <summary>Beatgrid entries parsed from nested <c>&lt;TEMPO&gt;</c> elements.</summary>
    public List<TempoMark> TempoMarks { get; set; } = [];

    /// <summary>Cue points and loops parsed from nested <c>&lt;POSITION_MARK&gt;</c> elements.</summary>
    public List<CuePoint> CuePoints { get; set; } = [];

    /// <summary>
    /// Attributes present on the <c>&lt;TRACK&gt;</c> element that this library does not model.
    /// Populated when <see cref="Xml.ParseOptions.PreserveUnknownAttributes"/> is true.
    /// </summary>
    public Dictionary<string, string> ExtraAttributes { get; set; } = [];

    // ----- Display helpers -----

    /// <summary>Formatted BPM for display. Returns an em-dash when BPM is zero or unset.</summary>
    public string BpmDisplay => Bpm > 0 ? Bpm.ToString("0.##") : "—";

    /// <summary>Formatted key for display. Returns an em-dash when the key is empty.</summary>
    public string KeyDisplay => string.IsNullOrEmpty(Key) ? "—" : Key;
}
