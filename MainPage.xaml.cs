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
    private const string PrefKeyFilePath = "rekordbox_file_path";

    private RekordboxLibrary? _library;

    public MainPage()
    {
        InitializeComponent();
        _ = TryLoadSavedFile();
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

            var fullPath = result.FullPath;

            using var stream = await result.OpenReadAsync();
            _library = await RekordboxParser.ParseAsync(stream);
            LibraryStore.Library = _library;

            Preferences.Set(PrefKeyFilePath, fullPath);
            ShowLibrary(fullPath);
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
        var savedPath = Preferences.Get(PrefKeyFilePath, null as string);
        if (string.IsNullOrEmpty(savedPath) || !File.Exists(savedPath)) return;

        try
        {
            using var stream = File.OpenRead(savedPath);
            _library = await RekordboxParser.ParseAsync(stream);
            LibraryStore.Library = _library;
            ShowLibrary(savedPath);
        }
        catch
        {
            // File is corrupt or unreadable — stay on welcome screen
        }
    }

    /// <summary>
    /// Switches from the welcome view to the library view and populates
    /// the header with the file name and full path.
    /// </summary>
    /// <param name="filePath">Full path to the loaded rekordbox.xml file.</param>
    private void ShowLibrary(string filePath)
    {
        if (_library == null) return;

        FileNameLabel.Text = Path.GetFileName(filePath);
        FilePathLabel.Text = filePath;
        PlaylistList.ItemsSource = _library.Root.Children;

        WelcomeView.IsVisible = false;
        LibraryView.IsVisible = true;
    }

    /// <summary>
    /// Handles selecting a playlist/folder entry. Resolves the node's track keys
    /// into full Track objects and navigates to the PlaylistPage.
    /// </summary>
    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PlaylistNode node) return;

        PlaylistList.SelectedItem = null;

        var tracks = node.TrackKeys
            .Select(k => _library!.Tracks.GetValueOrDefault(k))
            .Where(t => t != null)
            .Cast<Track>()
            .ToList();

        await Shell.Current.GoToAsync("playlist", new Dictionary<string, object>
        {
            { "Node", node },
            { "Tracks", tracks },
        });
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
