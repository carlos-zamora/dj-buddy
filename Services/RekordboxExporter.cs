using System.Xml.Linq;
using dj_buddy.Models;

namespace dj_buddy.Services;

/// <summary>
/// Exports DJ_BUDDY playlists into a rekordbox.xml file by injecting
/// a <c>DJ_BUDDY</c> folder node into the playlist tree.
/// </summary>
public static class RekordboxExporter
{
    /// <summary>
    /// Reads a rekordbox.xml from <paramref name="stream"/>, injects the
    /// <paramref name="djBuddyFolder"/> into the ROOT playlist node
    /// (replacing any existing DJ_BUDDY node), and writes the result back
    /// to <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">A read/write seekable stream to the rekordbox.xml file.</param>
    /// <param name="djBuddyFolder">The DJ_BUDDY folder to inject.</param>
    public static async Task ExportAsync(Stream stream, PlaylistNode djBuddyFolder)
    {
        var doc = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, CancellationToken.None);

        var root = doc.Descendants("NODE")
            .FirstOrDefault(n => n.Attribute("Name")?.Value == "ROOT"
                              && n.Attribute("Type")?.Value == "0")
            ?? throw new InvalidOperationException("Could not find ROOT node in rekordbox.xml.");

        // Remove existing DJ_BUDDY node if present
        root.Elements("NODE")
            .FirstOrDefault(n => n.Attribute("Name")?.Value == "DJ_BUDDY")
            ?.Remove();

        // Build the new DJ_BUDDY element
        var djBuddyElement = BuildFolderElement(djBuddyFolder);
        root.Add(djBuddyElement);

        // Update ROOT's Count attribute
        var count = root.Elements("NODE").Count();
        root.SetAttributeValue("Count", count);

        // Write back
        stream.SetLength(0);
        stream.Seek(0, SeekOrigin.Begin);
        await doc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
    }

    /// <summary>
    /// Recursively builds an <see cref="XElement"/> for a folder or playlist node
    /// matching the rekordbox XML schema.
    /// </summary>
    private static XElement BuildFolderElement(PlaylistNode node)
    {
        if (node.IsFolder)
        {
            var element = new XElement("NODE",
                new XAttribute("Name", node.Name),
                new XAttribute("Type", "0"),
                new XAttribute("Count", node.Children.Count));

            foreach (var child in node.Children)
                element.Add(BuildFolderElement(child));

            return element;
        }

        // Playlist node
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
