using System.Globalization;
using System.Xml.Linq;
using Rekordbox.Models;

namespace Rekordbox.Xml;

/// <summary>
/// Writes rekordbox.xml content. Supports two modes:
/// <list type="bullet">
///   <item><see cref="PatchPlaylistNodeAsync"/> — targeted patch that preserves every byte of the
///         source XML except the specific folder/playlist being injected. Use when round-tripping
///         an existing library that the parser may not fully model.</item>
///   <item><see cref="WriteAsync(RekordboxLibrary, Stream, CancellationToken)"/> — full serialization
///         from a <see cref="RekordboxLibrary"/> model. Use when building a library from scratch.</item>
/// </list>
/// </summary>
public static class RekordboxExporter
{
    // ---------- Patch mode ----------

    /// <summary>
    /// Reads an existing <c>rekordbox.xml</c> from <paramref name="input"/>, injects or replaces a
    /// folder/playlist node directly under <c>ROOT</c>, and returns the modified XML as bytes.
    /// Preserves every element and attribute in the source file that the parser does not model.
    /// </summary>
    /// <param name="input">Readable stream of the source rekordbox.xml.</param>
    /// <param name="node">The folder/playlist node to inject.</param>
    /// <param name="replaceByName">
    /// Name of an existing child of <c>ROOT</c> to remove before injecting the new node.
    /// Defaults to <paramref name="node"/>'s <c>Name</c>. Pass a different value when the incoming
    /// node should replace a differently-named existing entry.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The modified XML content as a UTF-8 byte array.</returns>
    public static async Task<byte[]> PatchPlaylistNodeAsync(
        Stream input,
        PlaylistNode node,
        string? replaceByName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(node);

        var doc = await XDocument.LoadAsync(input, LoadOptions.PreserveWhitespace, ct).ConfigureAwait(false);

        var root = doc.Descendants("NODE")
            .FirstOrDefault(n => n.Attribute("Name")?.Value == "ROOT"
                              && n.Attribute("Type")?.Value == "0")
            ?? throw new InvalidOperationException("Could not find ROOT node in rekordbox.xml.");

        // Remove existing node with the matching name if present.
        var targetName = replaceByName ?? node.Name;
        root.Elements("NODE")
            .FirstOrDefault(n => n.Attribute("Name")?.Value == targetName)
            ?.Remove();

        root.Add(BuildNodeElement(node));

        // Keep ROOT's Count attribute in sync with its immediate children.
        var count = root.Elements("NODE").Count();
        root.SetAttributeValue("Count", count);

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms, SaveOptions.None, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    // ---------- Full serialization ----------

    /// <summary>
    /// Serializes a complete <see cref="RekordboxLibrary"/> as <c>rekordbox.xml</c> and writes it
    /// to <paramref name="output"/>. Only data present on the model is emitted — attributes that
    /// weren't parsed (or were dropped) will not appear in the output.
    /// </summary>
    public static async Task WriteAsync(RekordboxLibrary library, Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(output);

        var doc = BuildDocument(library);
        await doc.SaveAsync(output, SaveOptions.None, ct).ConfigureAwait(false);
    }

    /// <summary>Convenience wrapper over <see cref="WriteAsync"/> that returns a UTF-8 byte array.</summary>
    public static async Task<byte[]> WriteToBytesAsync(RekordboxLibrary library, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await WriteAsync(library, ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static XDocument BuildDocument(RekordboxLibrary library)
    {
        var collection = new XElement("COLLECTION",
            new XAttribute("Entries", library.Tracks.Count));
        foreach (var track in library.Tracks.Values)
            collection.Add(BuildTrackElement(track));

        var playlists = new XElement("PLAYLISTS",
            BuildNodeElement(library.Root));

        var djPlaylists = new XElement("DJ_PLAYLISTS",
            new XAttribute("Version", "1.0.0"),
            new XElement("PRODUCT",
                new XAttribute("Name", "rekordbox"),
                new XAttribute("Version", "0.0.0"),
                new XAttribute("Company", "AlphaTheta")),
            collection,
            playlists);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            djPlaylists);
    }

    private static XElement BuildTrackElement(Track track)
    {
        var element = new XElement("TRACK");

        // Identity
        SetAttr(element, "TrackID", track.TrackId);

        // Metadata (always emit, even when empty, to match rekordbox's own output)
        SetAttr(element, "Name", track.Name);
        SetAttr(element, "Artist", track.Artist);
        SetAttr(element, "Composer", track.Composer);
        SetAttr(element, "Album", track.Album);
        SetAttr(element, "Grouping", track.Grouping);
        SetAttr(element, "Genre", track.Genre);
        SetAttr(element, "Kind", track.Kind);
        SetAttr(element, "Size", track.Size.ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "TotalTime", track.TotalTime.ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "DiscNumber", (track.DiscNumber ?? 0).ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "TrackNumber", (track.TrackNumber ?? 0).ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "Year", (track.Year ?? 0).ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "AverageBpm", track.Bpm.ToString("0.00", CultureInfo.InvariantCulture));
        if (track.DateModified is { } mod)
            SetAttr(element, "DateModified", mod.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (track.DateAdded is { } added)
            SetAttr(element, "DateAdded", added.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        SetAttr(element, "BitRate", track.BitRate.ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "SampleRate", track.SampleRate.ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "Comments", track.Comments);
        SetAttr(element, "PlayCount", track.PlayCount.ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "Rating", track.Rating.ToString(CultureInfo.InvariantCulture));
        SetAttr(element, "Location", track.Location);
        SetAttr(element, "Remixer", track.Remixer);
        SetAttr(element, "Tonality", track.Tonality);
        SetAttr(element, "Label", track.Label);
        SetAttr(element, "Mix", track.Mix);

        // Preserved unknown attributes (after the known ones so known attrs win on duplicates).
        foreach (var kv in track.ExtraAttributes)
            if (element.Attribute(kv.Key) is null)
                element.SetAttributeValue(kv.Key, kv.Value);

        // Nested beatgrid.
        foreach (var tempo in track.TempoMarks)
        {
            element.Add(new XElement("TEMPO",
                new XAttribute("Inizio", tempo.Inizio.ToString("0.000", CultureInfo.InvariantCulture)),
                new XAttribute("Bpm", tempo.Bpm.ToString("0.00", CultureInfo.InvariantCulture)),
                new XAttribute("Metro", tempo.Metro),
                new XAttribute("Battito", tempo.Battito.ToString(CultureInfo.InvariantCulture))));
        }

        // Nested cue points.
        foreach (var cue in track.CuePoints)
        {
            var cueElement = new XElement("POSITION_MARK",
                new XAttribute("Name", cue.Name),
                new XAttribute("Type", cue.Type.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("Start", cue.Start.ToString("0.000", CultureInfo.InvariantCulture)),
                new XAttribute("Num", (cue.Num ?? -1).ToString(CultureInfo.InvariantCulture)));
            if (cue.End is { } end)
                cueElement.SetAttributeValue("End", end.ToString("0.000", CultureInfo.InvariantCulture));
            cueElement.SetAttributeValue("Red", cue.Red.ToString(CultureInfo.InvariantCulture));
            cueElement.SetAttributeValue("Green", cue.Green.ToString(CultureInfo.InvariantCulture));
            cueElement.SetAttributeValue("Blue", cue.Blue.ToString(CultureInfo.InvariantCulture));
            element.Add(cueElement);
        }

        return element;
    }

    private static void SetAttr(XElement element, string name, string value)
        => element.SetAttributeValue(name, value);

    // ---------- Shared NODE builder ----------

    /// <summary>
    /// Builds an <see cref="XElement"/> for a folder or playlist node, matching the rekordbox
    /// XML schema. Used by both <see cref="PatchPlaylistNodeAsync"/> and <see cref="WriteAsync"/>.
    /// </summary>
    private static XElement BuildNodeElement(PlaylistNode node)
    {
        if (node.IsFolder)
        {
            var element = new XElement("NODE",
                new XAttribute("Name", node.Name),
                new XAttribute("Type", "0"),
                new XAttribute("Count", node.Children.Count));

            foreach (var child in node.Children)
                element.Add(BuildNodeElement(child));

            return element;
        }

        var playlist = new XElement("NODE",
            new XAttribute("Name", node.Name),
            new XAttribute("Type", "1"),
            new XAttribute("KeyType", "0"),
            new XAttribute("Entries", node.TrackKeys.Count));

        foreach (var trackKey in node.TrackKeys)
            playlist.Add(new XElement("TRACK", new XAttribute("Key", trackKey)));

        return playlist;
    }
}
