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
    public async Task ExportAndSaveAsync(string bookmarkToken, Func<Stream, Task<byte[]>> exportAction)
    {
        if (!File.Exists(bookmarkToken))
            throw new FileNotFoundException("The rekordbox.xml file was not found.", bookmarkToken);

        var dir = Path.GetDirectoryName(bookmarkToken)!;
        var name = Path.GetFileNameWithoutExtension(bookmarkToken);
        var ext = Path.GetExtension(bookmarkToken);
        var backupPath = Path.Combine(dir, $"{name}_backup{ext}");

        File.Copy(bookmarkToken, backupPath, overwrite: true);

        byte[] exported;
        using (var readStream = File.OpenRead(bookmarkToken))
            exported = await exportAction(readStream);

        await File.WriteAllBytesAsync(bookmarkToken, exported);
    }
}
