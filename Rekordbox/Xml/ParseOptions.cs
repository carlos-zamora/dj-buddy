namespace Rekordbox.Xml;

/// <summary>
/// Controls optional work done by <see cref="RekordboxParser.ParseAsync(System.IO.Stream, ParseOptions, System.Threading.CancellationToken)"/>.
/// Disable flags you don't need to speed up parsing of very large libraries.
/// </summary>
public class ParseOptions
{
    /// <summary>Parse nested <c>&lt;TEMPO&gt;</c> elements into <c>Track.TempoMarks</c>. Default: true.</summary>
    public bool ReadTempoMarks { get; set; } = true;

    /// <summary>Parse nested <c>&lt;POSITION_MARK&gt;</c> elements into <c>Track.CuePoints</c>. Default: true.</summary>
    public bool ReadCuePoints { get; set; } = true;

    /// <summary>
    /// When true, any <c>&lt;TRACK&gt;</c> attribute that the parser does not recognize is copied
    /// into <c>Track.ExtraAttributes</c>. When false, unknown attributes are dropped. Default: true.
    /// </summary>
    public bool PreserveUnknownAttributes { get; set; } = true;
}
