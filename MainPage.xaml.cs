using dj_buddy.Models;
using dj_buddy.Services;

namespace dj_buddy;

/// <summary>
/// Main page with two states: a welcome screen prompting the user to load a rekordbox.xml,
/// and a library view showing top-level playlists/folders once a file is loaded.
/// Persists the last loaded file path and reloads it on startup.
/// </summary>
public partial class MainPage : ContentPage
{
    private const string PrefKeyBookmark = "rekordbox_bookmark";
    private const string PrefKeyDisplayName = "rekordbox_display_name";

    private readonly IFileBookmarkService _bookmarkService;
    private RekordboxLibrary? _library;
    private bool _djBuddySectionCollapsed;

    public MainPage(IFileBookmarkService bookmarkService)
    {
        InitializeComponent();
        _bookmarkService = bookmarkService;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await DjBuddyPlaylistStore.LoadAsync();
        await TryLoadSavedFile();
    }

    private async void OnPickFileClicked(object? sender, EventArgs e)
    {
        await PickAndLoadFile();
    }

    /// <summary>
    /// Opens a file picker for XML files, parses the selected rekordbox.xml,
    /// stores the result in <see cref="LibraryStore"/>, persists the path,
    /// and transitions to the library view.
    /// </summary>
    private async Task PickAndLoadFile()
    {
        try
        {
            var xmlType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".xml" } },
                { DevicePlatform.Android, new[] { "text/xml", "application/xml" } },
                { DevicePlatform.iOS, new[] { "public.xml" } },
                { DevicePlatform.macOS, new[] { "public.xml" } },
            });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select rekordbox.xml",
                FileTypes = xmlType,
            });

            if (result == null) return;

            using var stream = await result.OpenReadAsync();
            _library = await RekordboxParser.ParseAsync(stream);
            LibraryStore.Library = _library;

            var bookmark = await _bookmarkService.SaveBookmarkAsync(result.FullPath);
            if (bookmark != null)
                Preferences.Set(PrefKeyBookmark, bookmark);
            Preferences.Set(PrefKeyDisplayName, result.FileName);
            ShowLibrary();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"Failed to load file: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Attempts to reload the previously loaded rekordbox.xml from the persisted path.
    /// Falls back to the welcome screen if the file no longer exists.
    /// </summary>
    private async Task TryLoadSavedFile()
    {
        var bookmark = Preferences.Get(PrefKeyBookmark, null as string);
        if (string.IsNullOrEmpty(bookmark)) return;

        try
        {
            using var stream = await _bookmarkService.OpenBookmarkedFileAsync(bookmark);
            if (stream == null) return;

            _library = await RekordboxParser.ParseAsync(stream);
            LibraryStore.Library = _library;
            ShowLibrary();
        }
        catch
        {
            // File is corrupt or inaccessible — stay on welcome screen
        }
    }

    /// <summary>
    /// Switches from the welcome view to the library view and populates
    /// the header and playlist sections.
    /// </summary>
    private void ShowLibrary()
    {
        if (_library == null) return;

        FileNameLabel.Text = Preferences.Get(PrefKeyDisplayName, "rekordbox.xml");
        FilePathLabel.Text = Preferences.Get(PrefKeyBookmark, string.Empty);

        RefreshDjBuddySection();
        RefreshRekordboxSection();

        WelcomeView.IsVisible = false;
        LibraryView.IsVisible = true;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_library != null)
            RefreshDjBuddySection();
    }

    private void RefreshDjBuddySection()
    {
        BindableLayout.SetItemsSource(DjBuddyPlaylistList, null);
        BindableLayout.SetItemsSource(DjBuddyPlaylistList,
            _djBuddySectionCollapsed ? null : DjBuddyPlaylistStore.DjBuddyFolder.Children);
        DjBuddyCollapseIcon.Text = _djBuddySectionCollapsed ? "\u25B6" : "\u25BC";
    }

    private void RefreshRekordboxSection()
    {
        BindableLayout.SetItemsSource(RekordboxPlaylistList, _library?.Root.Children);
    }

    private void OnDjBuddySectionTapped(object? sender, EventArgs e)
    {
        _djBuddySectionCollapsed = !_djBuddySectionCollapsed;
        RefreshDjBuddySection();
    }

    /// <summary>
    /// Handles swipe-rename on a DJ Buddy playlist (touch/mobile). Shows a rename prompt.
    /// Favorites cannot be renamed and their swipe item is hidden, but guard anyway.
    /// </summary>
    private async void OnDjBuddySwipeRename(object? sender, EventArgs e)
    {
        var node = (sender as BindableObject)?.BindingContext as PlaylistNode;
        if (node == null || node.Name == "Favorites") return;
        await RenamePlaylistAsync(node);
    }

    /// <summary>
    /// Handles right-click on a DJ Buddy playlist (desktop). Shows an action sheet
    /// with Rename and Delete options. Favorites have no options and are ignored.
    /// </summary>
    private async void OnDjBuddyRightClicked(object? sender, EventArgs e)
    {
        // Buttons.Secondary fires on both recognizers on touch idioms — guard to desktop only.
        var idiom = DeviceInfo.Idiom;
        if (idiom == DeviceIdiom.Phone || idiom == DeviceIdiom.Tablet) return;

        var node = (sender as BindableObject)?.BindingContext as PlaylistNode;
        if (node == null || node.Name == "Favorites") return;

        string? action = await DisplayActionSheetAsync(node.Name, "Cancel", null, "Rename", "Delete");
        if (action == "Rename")
            await RenamePlaylistAsync(node);
        else if (action == "Delete")
            await DeletePlaylistAsync(node);
    }

    /// <summary>
    /// Handles swipe-delete on a DJ Buddy playlist (touch/mobile). Confirms before removing.
    /// Favorites cannot be deleted and their swipe item is hidden, but guard anyway.
    /// </summary>
    private async void OnDjBuddyPlaylistDelete(object? sender, EventArgs e)
    {
        var node = (sender as BindableObject)?.BindingContext as PlaylistNode;
        if (node == null || node.Name == "Favorites") return;
        await DeletePlaylistAsync(node);
    }

    /// <summary>
    /// Shows a rename prompt for the given playlist node and saves on confirmation.
    /// </summary>
    private async Task RenamePlaylistAsync(PlaylistNode node)
    {
        var newName = await DisplayPromptAsync("Rename Playlist", "Enter new name:",
            initialValue: node.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == node.Name) return;

        DjBuddyPlaylistStore.RenamePlaylist(node.Name, newName);
        await DjBuddyPlaylistStore.SaveAsync();
        RefreshDjBuddySection();
    }

    /// <summary>
    /// Shows a delete confirmation for the given playlist node and removes it on confirmation.
    /// </summary>
    private async Task DeletePlaylistAsync(PlaylistNode node)
    {
        bool confirm = await DisplayAlertAsync("Delete Playlist",
            $"Delete \"{node.Name}\"? This cannot be undone.",
            "Delete", "Cancel");
        if (!confirm) return;

        DjBuddyPlaylistStore.RemovePlaylist(node.Name);
        await DjBuddyPlaylistStore.SaveAsync();
        RefreshDjBuddySection();
    }

    /// <summary>
    /// Handles tap on any playlist/folder item. Resolves track keys
    /// and navigates to PlaylistPage.
    /// </summary>
    private async void OnPlaylistItemTapped(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not PlaylistNode node)
            return;

        var tracks = node.TrackKeys
            .Select(k => _library?.Tracks.GetValueOrDefault(k))
            .Where(t => t != null)
            .Cast<Track>()
            .ToList();

        await Shell.Current.GoToAsync("playlist", new Dictionary<string, object>
        {
            { "Node", node },
            { "Tracks", tracks },
        });
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        var bookmark = Preferences.Get(PrefKeyBookmark, null as string);
        if (bookmark == null || _library == null)
        {
            await DisplayAlertAsync("Export", "No library loaded.", "OK");
            return;
        }

        bool confirm = await DisplayAlertAsync("Export",
            "This will add DJ_BUDDY playlists to your rekordbox.xml and create a backup (rekordbox_backup.xml). Continue?",
            "Export", "Cancel");
        if (!confirm) return;

        try
        {
            await _bookmarkService.ExportWithBackupAsync(bookmark,
                stream => RekordboxExporter.ExportAsync(stream, DjBuddyPlaylistStore.DjBuddyFolder));

            await DisplayAlertAsync("Export Complete",
                "DJ_BUDDY playlists added to rekordbox.xml.\n\n" +
                "To import into rekordbox:\n" +
                "1. Open rekordbox\n" +
                "2. Preferences \u2192 Advanced \u2192 Database \u2192 rekordbox xml\n" +
                "3. Set \"Imported Library\" to your rekordbox.xml\n" +
                "4. In the sidebar under \"xml\", find DJ_BUDDY\n" +
                "5. Right-click a playlist \u2192 Import Playlist",
                "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"Export failed: {ex.Message}", "OK");
        }
    }
}

/// <summary>
/// Converts an IsFolder boolean to a folder or music note emoji.
/// </summary>
public class FolderIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "\U0001F4C1" : "\U0001F3B5";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the bound Name is not "Favorites". Used to hide swipe actions
/// (Rename, Delete) on the built-in Favorites playlist.
/// </summary>
public class IsNotFavoritesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string name && name != "Favorites";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
