using Foundation;
using dj_buddy.Services;

namespace dj_buddy.Platforms.iOS;

/// <summary>
/// iOS implementation using NSUrl bookmarks to persist access to files picked
/// via the document picker (including iCloud Drive, Files app, etc.) across
/// app restarts. The bookmark token is Base64-encoded NSData.
/// </summary>
public class FileBookmarkService : IFileBookmarkService
{
    /// <inheritdoc/>
    /// <remarks>
    /// Creates an <c>NSUrl</c> bookmark from <paramref name="filePath"/> and returns it
    /// as a Base64 string. The bookmark encodes the OS-level access grant so it can be
    /// restored after the app process exits.
    /// </remarks>
    public Task<string?> SaveBookmarkAsync(string filePath)
    {
        var url = NSUrl.FromFilename(filePath);
        var data = url.CreateBookmarkData(
            (NSUrlBookmarkCreationOptions)0,
            Array.Empty<string>(),
            null,
            out var error);

        if (error != null || data == null)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(
            data.GetBase64EncodedString(NSDataBase64EncodingOptions.None));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Decodes <paramref name="bookmarkToken"/> from Base64, resolves it via
    /// <c>NSUrl.FromBookmarkData</c> to recover the original URL with its access grant,
    /// then opens a read stream from the resolved path.
    /// </remarks>
    public Task<Stream?> OpenBookmarkedFileAsync(string bookmarkToken)
    {
        var url = ResolveBookmark(bookmarkToken);
        if (url?.Path == null || !File.Exists(url.Path))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(File.OpenRead(url.Path));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Resolves the bookmark, starts security-scoped access, creates a backup,
    /// opens a read/write stream, runs the export action, then stops access.
    /// </remarks>
    public async Task ExportWithBackupAsync(string bookmarkToken, Func<Stream, Task> exportAction)
    {
        var url = ResolveBookmark(bookmarkToken)
            ?? throw new FileNotFoundException("Could not resolve the bookmarked file.");

        if (url.Path == null || !File.Exists(url.Path))
            throw new FileNotFoundException("The rekordbox.xml file was not found.");

        url.StartAccessingSecurityScopedResource();
        try
        {
            var dir = Path.GetDirectoryName(url.Path)!;
            var name = Path.GetFileNameWithoutExtension(url.Path);
            var ext = Path.GetExtension(url.Path);
            var backupPath = Path.Combine(dir, $"{name}_backup{ext}");

            File.Copy(url.Path, backupPath, overwrite: true);

            using var stream = new FileStream(url.Path, FileMode.Open, FileAccess.ReadWrite);
            await exportAction(stream);
        }
        finally
        {
            url.StopAccessingSecurityScopedResource();
        }
    }

    /// <summary>
    /// Resolves a Base64-encoded bookmark token to an <see cref="NSUrl"/>.
    /// </summary>
    private static NSUrl? ResolveBookmark(string bookmarkToken)
    {
        var data = NSData.FromArray(Convert.FromBase64String(bookmarkToken));
        if (data == null) return null;

        var url = NSUrl.FromBookmarkData(
            data,
            (NSUrlBookmarkResolutionOptions)0,
            null,
            out _,
            out var error);

        return error != null ? null : url;
    }
}
