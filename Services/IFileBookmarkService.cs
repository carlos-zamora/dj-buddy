namespace dj_buddy.Services;

/// <summary>
/// Persists access to a user-picked file across app restarts.
/// On iOS/macOS the file picker grants only temporary access, so the
/// implementation must serialize the access grant as a bookmark.
/// </summary>
public interface IFileBookmarkService
{
    /// <summary>
    /// Creates a persistent bookmark for <paramref name="filePath"/> and returns
    /// an opaque token that can be stored in <see cref="Preferences"/>.
    /// Returns <c>null</c> if the bookmark cannot be created.
    /// </summary>
    Task<string?> SaveBookmarkAsync(string filePath);

    /// <summary>
    /// Opens a readable stream for the file identified by <paramref name="bookmarkToken"/>.
    /// Returns <c>null</c> if the file is inaccessible or the token is invalid.
    /// </summary>
    Task<Stream?> OpenBookmarkedFileAsync(string bookmarkToken);

    /// <summary>
    /// Creates a backup of the bookmarked file (appending "_backup" before the extension),
    /// then opens a read/write stream so the caller can modify the file in place.
    /// The <paramref name="exportAction"/> receives the stream to perform the actual export.
    /// </summary>
    /// <param name="bookmarkToken">Bookmark token from <see cref="SaveBookmarkAsync"/>.</param>
    /// <param name="exportAction">Async action that reads and rewrites the file via the stream.</param>
    Task ExportWithBackupAsync(string bookmarkToken, Func<Stream, Task> exportAction);
}
