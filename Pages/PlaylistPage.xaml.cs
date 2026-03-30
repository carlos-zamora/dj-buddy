using dj_buddy.Models;

namespace dj_buddy.Pages;

/// <summary>
/// Displays the contents of a playlist/folder node: child playlists in one group
/// and tracks in another. Supports navigating into child folders.
/// </summary>
[QueryProperty(nameof(Node), "Node")]
[QueryProperty(nameof(Tracks), "Tracks")]
public partial class PlaylistPage : ContentPage
{
    private PlaylistNode? _node;
    private List<Track>? _tracks;
    private SortField _sortField = SortField.None;
    private bool _sortAscending = true;

    public PlaylistNode? Node
    {
        get => _node;
        set
        {
            _node = value;
            Title = _node?.Name ?? "Playlist";
            BuildContent();
        }
    }

    public List<Track>? Tracks
    {
        get => _tracks;
        set
        {
            _tracks = value;
            BuildContent();
        }
    }

    public PlaylistPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Populates the playlist and track sections based on current node and tracks.
    /// Called when Node or Tracks is set. Applies the current sort to tracks.
    /// </summary>
    private void BuildContent()
    {
        if (_node == null) return;

        bool hasChildren = _node.Children.Count > 0;
        PlaylistsHeader.IsVisible = hasChildren;
        BindableLayout.SetItemsSource(NodeList, hasChildren ? _node.Children : null);

        bool hasTracks = _tracks?.Count > 0;
        TracksHeader.IsVisible = hasTracks;
        TrackColumnHeaders.IsVisible = hasTracks;
        BindableLayout.SetItemsSource(TrackList, hasTracks ? GetSortedTracks() : null);

        UpdateSortIndicators();
    }

    private IEnumerable<Track> GetSortedTracks()
    {
        if (_tracks == null) return [];

        return _sortField switch
        {
            SortField.Title => _sortAscending
                ? _tracks.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                : _tracks.OrderByDescending(t => t.Name, StringComparer.CurrentCultureIgnoreCase),
            SortField.Bpm => _sortAscending
                ? _tracks.OrderBy(t => t.Bpm)
                : _tracks.OrderByDescending(t => t.Bpm),
            SortField.Key => _sortAscending
                ? _tracks.OrderBy(t => t.Key, KeyComparer.Instance)
                : _tracks.OrderByDescending(t => t.Key, KeyComparer.Instance),
            _ => _tracks,
        };
    }

    private void UpdateSortIndicators()
    {
        TitleHeaderLabel.Text = "Title" + SortIndicator(SortField.Title);
        BpmHeaderLabel.Text = "BPM" + SortIndicator(SortField.Bpm);
        KeyHeaderLabel.Text = "Key" + SortIndicator(SortField.Key);
    }

    private string SortIndicator(SortField field) =>
        _sortField != field ? "" : _sortAscending ? " \u25B2" : " \u25BC";

    private void OnTitleHeaderTapped(object? sender, EventArgs e) => ToggleSort(SortField.Title);
    private void OnBpmHeaderTapped(object? sender, EventArgs e) => ToggleSort(SortField.Bpm);
    private void OnKeyHeaderTapped(object? sender, EventArgs e) => ToggleSort(SortField.Key);

    private void ToggleSort(SortField field)
    {
        if (_sortField == field)
            _sortAscending = !_sortAscending;
        else
        {
            _sortField = field;
            _sortAscending = true;
        }

        BuildContent();
    }

    /// <summary>
    /// Handles tap on a playlist node. Resolves its tracks from
    /// <see cref="LibraryStore"/> and navigates to a new PlaylistPage.
    /// </summary>
    private async void OnNodeTapped(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not PlaylistNode node)
            return;

        var tracks = LibraryStore.Library?.Tracks;
        var resolved = node.TrackKeys
            .Select(k => tracks?.GetValueOrDefault(k))
            .Where(t => t != null)
            .Cast<Track>()
            .ToList();

        await Shell.Current.GoToAsync("playlist", new Dictionary<string, object>
        {
            { "Node", node },
            { "Tracks", resolved },
        });
    }
}

public enum SortField { None, Title, Bpm, Key }

/// <summary>
/// Compares rekordbox alphanumeric keys (e.g. "8A", "11B") by their numeric part first,
/// then by the letter suffix.
/// </summary>
public class KeyComparer : IComparer<string>
{
    public static readonly KeyComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        Parse(x, out var xNum, out var xLetter);
        Parse(y, out var yNum, out var yLetter);

        int cmp = xNum.CompareTo(yNum);
        return cmp != 0 ? cmp : string.Compare(xLetter, yLetter, StringComparison.Ordinal);
    }

    private static void Parse(string? key, out int num, out string letter)
    {
        if (string.IsNullOrEmpty(key))
        {
            num = int.MaxValue;
            letter = "";
            return;
        }

        int i = 0;
        while (i < key.Length && char.IsDigit(key[i])) i++;

        if (i > 0 && int.TryParse(key.AsSpan(0, i), out num))
            letter = key[i..];
        else
        {
            num = int.MaxValue;
            letter = key;
        }
    }
}
