using Foundation;
using ObjCRuntime;
using UIKit;
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
    /// Resolves the bookmark, reads the file (original stays untouched as the backup),
    /// runs the export action to produce the modified bytes, writes them to a temporary
    /// file, then presents a <c>UIDocumentPickerViewController</c> so the user can save
    /// the exported file to any accessible location.
    /// </remarks>
    public async Task ExportAndSaveAsync(string bookmarkToken, Func<Stream, Task<byte[]>> exportAction)
    {
        var url = ResolveBookmark(bookmarkToken)
            ?? throw new FileNotFoundException("Could not resolve the bookmarked file.");

        if (url.Path == null || !File.Exists(url.Path))
            throw new FileNotFoundException("The rekordbox.xml file was not found.");

        // Read the source file under security-scoped access; the original is left
        // untouched and serves as the implicit backup.
        byte[] exported;
        url.StartAccessingSecurityScopedResource();
        try
        {
            using var readStream = File.OpenRead(url.Path);
            exported = await exportAction(readStream);
        }
        finally
        {
            url.StopAccessingSecurityScopedResource();
        }

        // Write exported bytes to a temp file, then hand it to the document picker.
        var fileName = Path.GetFileName(url.Path);
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllBytesAsync(tempPath, exported);
        var tempUrl = NSUrl.FromFilename(tempPath);

        var tcs = new TaskCompletionSource<bool>();
        var pickerDelegate = new ExportPickerDelegate(tcs);
        // asCopy: true — picker copies to destination; temp file is ours to clean up.
        var picker = new UIDocumentPickerViewController(new[] { tempUrl }, asCopy: true);
        picker.Delegate = pickerDelegate;

        var vc = Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("No current view controller.");

        await MainThread.InvokeOnMainThreadAsync(() =>
            vc.PresentViewController(picker, true, null));

        await tcs.Task;
        File.Delete(tempPath);
    }

    /// <summary>
    /// Document picker delegate that signals the <see cref="TaskCompletionSource{T}"/>
    /// when the user finishes or cancels the save interaction.
    /// </summary>
    private sealed class ExportPickerDelegate : UIDocumentPickerDelegate
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public ExportPickerDelegate(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        [Export("documentPicker:didPickDocumentsAtURLs:")]
        public void DidPickDocuments(UIDocumentPickerViewController controller, NSUrl[] urls)
            => _tcs.TrySetResult(true);

        public override void WasCancelled(UIDocumentPickerViewController controller)
            => _tcs.TrySetResult(false);
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
