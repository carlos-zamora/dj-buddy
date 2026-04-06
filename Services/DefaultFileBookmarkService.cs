using CommunityToolkit.Maui.Storage;

namespace dj_buddy.Services;

/// <summary>
/// Default implementation for platforms without sandbox restrictions (Windows, Android).
/// The bookmark token is just the raw file path.
/// </summary>
public class DefaultFileBookmarkService : IFileBookmarkService
{
    /// <inheritdoc/>
    /// <remarks>Returns <paramref name="filePath"/> directly — no serialization needed on this platform.</remarks>
    public Task<string?> SaveBookmarkAsync(string filePath)
        => Task.FromResult<string?>(filePath);

    /// <inheritdoc/>
    public Task<Stream?> OpenBookmarkedFileAsync(string bookmarkToken)
    {
        if (!File.Exists(bookmarkToken))
            return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(File.OpenRead(bookmarkToken));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the bookmarked file, passes it to <paramref name="exportAction"/> to produce
    /// the exported bytes, then presents a save-file dialog via MCT <see cref="FileSaver"/>
    /// so the user can choose the destination.
    /// </remarks>
    public async Task ExportAndSaveAsync(string bookmarkToken, Func<Stream, Task<byte[]>> exportAction)
    {
        if (!File.Exists(bookmarkToken))
            throw new FileNotFoundException("The rekordbox.xml file was not found.", bookmarkToken);

        byte[] exported;
        using (var readStream = File.OpenRead(bookmarkToken))
            exported = await exportAction(readStream);

        var fileName = Path.GetFileName(bookmarkToken);
        using var exportStream = new MemoryStream(exported);
        var result = await FileSaver.Default.SaveAsync(fileName, exportStream, CancellationToken.None);
        result.EnsureSuccess();
    }
}
