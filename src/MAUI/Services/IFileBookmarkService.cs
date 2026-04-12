namespace DJBuddy.MAUI.Services;

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
    /// Reads the bookmarked file, passes it to <paramref name="exportAction"/> to produce
    /// the exported bytes, then saves the result to a user-accessible location.
    /// On desktop platforms the original is backed up in place; on iOS a document picker
    /// is presented so the user can choose the destination.
    /// </summary>
    /// <param name="bookmarkToken">Bookmark token from <see cref="SaveBookmarkAsync"/>.</param>
    /// <param name="exportAction">
    /// Async function that receives the source file as a readable stream and returns
    /// the exported XML content as a byte array.
    /// </param>
    Task ExportAndSaveAsync(string bookmarkToken, Func<Stream, Task<byte[]>> exportAction);
}
