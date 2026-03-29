using System.Xml;
using dj_buddy.Models;

namespace dj_buddy.Services;

/// <summary>
/// Streaming parser for rekordbox.xml files. Reads the COLLECTION (tracks) and
/// PLAYLISTS (folder/playlist tree) sections into a <see cref="RekordboxLibrary"/>.
/// </summary>
public static class RekordboxParser
{
    /// <summary>
    /// Parses a rekordbox.xml stream into a <see cref="RekordboxLibrary"/>.
    /// </summary>
    /// <param name="stream">A readable stream of the rekordbox.xml file.</param>
    /// <returns>A fully populated <see cref="RekordboxLibrary"/> with tracks and playlist tree.</returns>
    public static async Task<RekordboxLibrary> ParseAsync(Stream stream)
    {
        var library = new RekordboxLibrary();

        var settings = new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(stream, settings);

        while (await reader.ReadAsync())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.Name == "COLLECTION")
            {
                await ReadCollection(reader, library);
            }
            else if (reader.Name == "PLAYLISTS")
            {
                await ReadPlaylists(reader, library);
            }
        }

        return library;
    }

    /// <summary>
    /// Reads all TRACK elements inside the COLLECTION element and populates the library's track dictionary.
    /// </summary>
    /// <param name="reader">An XmlReader positioned at the opening COLLECTION element.</param>
    /// <param name="library">The library to populate with parsed tracks.</param>
    private static async Task ReadCollection(XmlReader reader, RekordboxLibrary library)
    {
        if (reader.IsEmptyElement) return;

        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "COLLECTION")
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.Name == "TRACK")
            {
                var track = new Track
                {
                    TrackId = reader.GetAttribute("TrackID") ?? "",
                    Name = reader.GetAttribute("Name") ?? "",
                    Artist = reader.GetAttribute("Artist") ?? "",
                    Key = reader.GetAttribute("Tonality") ?? "",
                };

                if (double.TryParse(reader.GetAttribute("AverageBpm"), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var bpm))
                    track.Bpm = bpm;

                library.Tracks[track.TrackId] = track;
            }
        }
    }

    /// <summary>
    /// Reads the PLAYLISTS section and assigns the ROOT node to the library.
    /// </summary>
    /// <param name="reader">An XmlReader positioned at the opening PLAYLISTS element.</param>
    /// <param name="library">The library whose Root will be set from the parsed tree.</param>
    private static async Task ReadPlaylists(XmlReader reader, RekordboxLibrary library)
    {
        if (reader.IsEmptyElement) return;

        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "PLAYLISTS")
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.Name == "NODE")
            {
                var node = await ReadNode(reader);
                if (node != null && node.Name == "ROOT")
                    library.Root = node;
            }
        }
    }

    /// <summary>
    /// Recursively reads a single NODE element, which may be a folder (Type="0") containing
    /// child NODEs, or a playlist (Type="1") containing TRACK key references.
    /// </summary>
    /// <param name="reader">An XmlReader positioned at the opening NODE element.</param>
    /// <returns>The parsed <see cref="PlaylistNode"/>, or null if parsing fails.</returns>
    private static async Task<PlaylistNode?> ReadNode(XmlReader reader)
    {
        var type = reader.GetAttribute("Type");
        var name = reader.GetAttribute("Name") ?? "";
        var isFolder = type == "0";
        var isEmpty = reader.IsEmptyElement;

        var node = new PlaylistNode { Name = name, IsFolder = isFolder };

        if (isEmpty) return node;

        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "NODE")
                break;

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.Name == "NODE")
            {
                var child = await ReadNode(reader);
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
