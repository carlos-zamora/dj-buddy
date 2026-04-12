using System.Globalization;
using System.Xml;
using Rekordbox.Models;

namespace Rekordbox.Xml;

/// <summary>
/// Streaming parser for <c>rekordbox.xml</c> files. Reads the <c>COLLECTION</c> (tracks) and
/// <c>PLAYLISTS</c> (folder/playlist tree) sections into a <see cref="RekordboxLibrary"/>.
/// </summary>
/// <example>
/// <code>
/// using var stream = File.OpenRead("rekordbox.xml");
/// var library = await RekordboxParser.ParseAsync(stream);
/// Console.WriteLine($"Loaded {library.Tracks.Count} tracks");
/// </code>
/// </example>
public static class RekordboxParser
{
    private static readonly ParseOptions DefaultOptions = new();

    /// <summary>
    /// Parses a <c>rekordbox.xml</c> stream into a <see cref="RekordboxLibrary"/> using default
    /// <see cref="ParseOptions"/>.
    /// </summary>
    public static Task<RekordboxLibrary> ParseAsync(Stream stream, CancellationToken ct = default)
        => ParseAsync(stream, DefaultOptions, ct);

    /// <summary>
    /// Parses a <c>rekordbox.xml</c> stream into a <see cref="RekordboxLibrary"/>.
    /// </summary>
    /// <param name="stream">A readable stream of the rekordbox.xml file.</param>
    /// <param name="options">Options controlling which optional elements are parsed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A fully populated <see cref="RekordboxLibrary"/> with tracks and playlist tree.</returns>
    public static async Task<RekordboxLibrary> ParseAsync(Stream stream, ParseOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        var library = new RekordboxLibrary();

        var settings = new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(stream, settings);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.Name == "COLLECTION")
                await ReadCollection(reader, library, options, ct).ConfigureAwait(false);
            else if (reader.Name == "PLAYLISTS")
                await ReadPlaylists(reader, library, ct).ConfigureAwait(false);
        }

        return library;
    }

    // ---------- COLLECTION ----------

    /// <summary>Reads all TRACK elements inside COLLECTION and populates the library's track dictionary.</summary>
    private static async Task ReadCollection(XmlReader reader, RekordboxLibrary library, ParseOptions options, CancellationToken ct)
    {
        if (reader.IsEmptyElement) return;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "COLLECTION")
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.Name == "TRACK")
            {
                var track = await ReadTrack(reader, options, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(track.TrackId))
                    library.Tracks[track.TrackId] = track;
            }
        }
    }

    /// <summary>Reads a single TRACK element, including all attributes and any nested TEMPO / POSITION_MARK children.</summary>
    private static async Task<Track> ReadTrack(XmlReader reader, ParseOptions options, CancellationToken ct)
    {
        var track = new Track();
        var isEmpty = reader.IsEmptyElement;

        // Snapshot attributes before we descend into children.
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
                ApplyAttribute(track, reader.Name, reader.Value, options);
            reader.MoveToElement();
        }

        // Derive Camelot key from Tonality.
        track.Key = KeyConverter.ToCamelotNotation(track.Tonality);

        if (isEmpty)
            return track;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "TRACK")
                break;

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.Name == "TEMPO")
            {
                if (options.ReadTempoMarks)
                    track.TempoMarks.Add(ReadTempoMark(reader));
            }
            else if (reader.Name == "POSITION_MARK")
            {
                if (options.ReadCuePoints)
                    track.CuePoints.Add(ReadCuePoint(reader));
            }
        }

        return track;
    }

    private static void ApplyAttribute(Track track, string name, string value, ParseOptions options)
    {
        switch (name)
        {
            case "TrackID":     track.TrackId = value; break;
            case "Name":        track.Name = value; break;
            case "Artist":      track.Artist = value; break;
            case "Composer":    track.Composer = value; break;
            case "Album":       track.Album = value; break;
            case "Grouping":    track.Grouping = value; break;
            case "Genre":       track.Genre = value; break;
            case "Remixer":     track.Remixer = value; break;
            case "Label":       track.Label = value; break;
            case "Mix":         track.Mix = value; break;
            case "Comments":    track.Comments = value; break;
            case "Kind":        track.Kind = value; break;
            case "Location":    track.Location = value; break;
            case "Tonality":    track.Tonality = value; break;

            case "Year":        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)) track.Year = year; break;
            case "DiscNumber":  if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var disc)) track.DiscNumber = disc; break;
            case "TrackNumber": if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tn)) track.TrackNumber = tn; break;
            case "Rating":      if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating)) track.Rating = rating; break;
            case "PlayCount":   if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plays)) track.PlayCount = plays; break;
            case "TotalTime":   if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)) track.TotalTime = total; break;
            case "BitRate":     if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitRate)) track.BitRate = bitRate; break;
            case "SampleRate":  if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sampleRate)) track.SampleRate = sampleRate; break;

            case "Size":        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) track.Size = size; break;

            case "AverageBpm":  if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm)) track.Bpm = bpm; break;

            case "DateAdded":    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var added)) track.DateAdded = added; break;
            case "DateModified": if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var modified)) track.DateModified = modified; break;

            default:
                if (options.PreserveUnknownAttributes)
                    track.ExtraAttributes[name] = value;
                break;
        }
    }

    private static TempoMark ReadTempoMark(XmlReader reader)
    {
        var mark = new TempoMark();
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                switch (reader.Name)
                {
                    case "Inizio":  if (double.TryParse(reader.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var inizio)) mark.Inizio = inizio; break;
                    case "Bpm":     if (double.TryParse(reader.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm)) mark.Bpm = bpm; break;
                    case "Metro":   mark.Metro = reader.Value; break;
                    case "Battito": if (int.TryParse(reader.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var battito)) mark.Battito = battito; break;
                }
            }
            reader.MoveToElement();
        }
        return mark;
    }

    private static CuePoint ReadCuePoint(XmlReader reader)
    {
        var cue = new CuePoint();
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                switch (reader.Name)
                {
                    case "Name":  cue.Name = reader.Value; break;
                    case "Type":  if (int.TryParse(reader.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type)) cue.Type = type; break;
                    case "Start": if (double.TryParse(reader.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var start)) cue.Start = start; break;
                    case "End":   if (double.TryParse(reader.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var end)) cue.End = end; break;
                    case "Num":   if (int.TryParse(reader.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)) cue.Num = num; break;
                    case "Red":   if (int.TryParse(reader.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var red)) cue.Red = red; break;
                    case "Green": if (int.TryParse(reader.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var green)) cue.Green = green; break;
                    case "Blue":  if (int.TryParse(reader.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var blue)) cue.Blue = blue; break;
                }
            }
            reader.MoveToElement();
        }
        return cue;
    }

    // ---------- PLAYLISTS ----------

    /// <summary>Reads the PLAYLISTS section and assigns the ROOT node to the library.</summary>
    private static async Task ReadPlaylists(XmlReader reader, RekordboxLibrary library, CancellationToken ct)
    {
        if (reader.IsEmptyElement) return;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "PLAYLISTS")
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.Name == "NODE")
            {
                var node = await ReadNode(reader, ct).ConfigureAwait(false);
                if (node != null && node.Name == "ROOT")
                    library.Root = node;
            }
        }
    }

    /// <summary>
    /// Recursively reads a single NODE element, which may be a folder (<c>Type="0"</c>) containing
    /// child NODEs, or a playlist (<c>Type="1"</c>) containing TRACK key references.
    /// </summary>
    private static async Task<PlaylistNode?> ReadNode(XmlReader reader, CancellationToken ct)
    {
        var type = reader.GetAttribute("Type");
        var name = reader.GetAttribute("Name") ?? "";
        var isFolder = type == "0";
        var isEmpty = reader.IsEmptyElement;

        var node = new PlaylistNode { Name = name, IsFolder = isFolder };

        if (isEmpty) return node;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "NODE")
                break;

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.Name == "NODE")
            {
                var child = await ReadNode(reader, ct).ConfigureAwait(false);
                if (child != null)
                    node.Children.Add(child);
            }
            else if (reader.Name == "TRACK")
            {
                var key = reader.GetAttribute("Key") ?? "";
                if (!string.IsNullOrEmpty(key))
                    node.TrackKeys.Add(key);
            }
        }

        return node;
    }
}
