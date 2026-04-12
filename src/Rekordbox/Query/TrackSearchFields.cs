namespace DJBuddy.Rekordbox.Query;

/// <summary>
/// Bit flags selecting which <see cref="Models.Track"/> fields <see cref="TrackQuery.Search"/>
/// matches against.
/// </summary>
[Flags]
public enum TrackSearchFields
{
    None     = 0,
    Name     = 1 << 0,
    Artist   = 1 << 1,
    Album    = 1 << 2,
    Genre    = 1 << 3,
    Comments = 1 << 4,
    Label    = 1 << 5,

    /// <summary>The default search surface — matches the behavior of the DJ Buddy MAUI app.</summary>
    NameAndArtist = Name | Artist,

    All = Name | Artist | Album | Genre | Comments | Label,
}
