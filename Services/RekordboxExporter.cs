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
    /// Reads a rekordbox.xml from <paramref name="input"/>, injects the
    /// <paramref name="djBuddyFolder"/> into the ROOT playlist node
    /// (replacing any existing DJ_BUDDY node), and returns the result as a byte array.
    /// </summary>
    /// <param name="input">Readable stream of the source rekordbox.xml.</param>
    /// <param name="djBuddyFolder">The DJ_BUDDY folder to inject.</param>
    /// <returns>The modified XML content as a UTF-8 byte array.</returns>
    public static async Task<byte[]> ExportAsync(Stream input, PlaylistNode djBuddyFolder)
    {
        var doc = await XDocument.LoadAsync(input, LoadOptions.PreserveWhitespace, CancellationToken.None);

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

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms, SaveOptions.None, CancellationToken.None);
        return ms.ToArray();
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
