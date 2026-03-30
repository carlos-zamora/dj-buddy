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
    private string _searchText = "";
    private string? _keyFilter;

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
        FilterBar.IsVisible = hasTracks;
        TrackColumnHeaders.IsVisible = hasTracks;
        BindableLayout.SetItemsSource(TrackList, hasTracks ? GetFilteredAndSortedTracks() : null);

        UpdateSortIndicators();
        UpdateKeyFilterButton();
    }

    private IEnumerable<Track> GetFilteredAndSortedTracks()
    {
        if (_tracks == null) return [];

        IEnumerable<Track> filtered = _tracks;

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.Trim();
            filtered = filtered.Where(t =>
                t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(_keyFilter))
        {
            filtered = filtered.Where(t =>
                string.Equals(t.Key, _keyFilter, StringComparison.OrdinalIgnoreCase));
        }

        return _sortField switch
        {
            SortField.Title => _sortAscending
                ? filtered.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                : filtered.OrderByDescending(t => t.Name, StringComparer.CurrentCultureIgnoreCase),
            SortField.Bpm => _sortAscending
                ? filtered.OrderBy(t => t.Bpm)
                : filtered.OrderByDescending(t => t.Bpm),
            SortField.Key => _sortAscending
                ? filtered.OrderBy(t => t.Key, KeyComparer.Instance)
                : filtered.OrderByDescending(t => t.Key, KeyComparer.Instance),
            _ => filtered,
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

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchText = e.NewTextValue ?? "";
        BuildContent();
    }

    private void OnKeyFilterButtonClicked(object? sender, EventArgs e)
    {
        PopulateKeyGrid();
        KeyPickerOverlay.IsVisible = true;
    }

    private void OnKeyPickerDismissed(object? sender, EventArgs e)
    {
        KeyPickerOverlay.IsVisible = false;
    }

    private void OnKeyPickerClear(object? sender, EventArgs e)
    {
        _keyFilter = null;
        KeyPickerOverlay.IsVisible = false;
        BuildContent();
    }

    /// <summary>
    /// Populates the key picker grid with buttons for all 24 Camelot keys (1A–12B).
    /// Highlights the currently active filter key.
    /// </summary>
    private void PopulateKeyGrid()
    {
        KeyGrid.Children.Clear();
        KeyGrid.RowDefinitions.Clear();

        for (int number = 1; number <= 12; number++)
        {
            KeyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            AddKeyButton($"{number}B", number - 1, 0);
            AddKeyButton($"{number}A", number - 1, 1);
        }
    }

    private void AddKeyButton(string key, int row, int col)
    {
        bool isSelected = string.Equals(key, _keyFilter, StringComparison.OrdinalIgnoreCase);

        var button = new Button
        {
            Text = key,
            FontSize = 14,
            HeightRequest = 40,
            Padding = new Thickness(0),
            CornerRadius = 6,
            BackgroundColor = isSelected ? Colors.DodgerBlue : Colors.Transparent,
            TextColor = isSelected ? Colors.White : Colors.Gray,
            BorderColor = Colors.Gray,
            BorderWidth = 1,
        };

        button.Clicked += (_, _) =>
        {
            _keyFilter = key;
            KeyPickerOverlay.IsVisible = false;
            BuildContent();
        };

        KeyGrid.SetRow(button, row);
        KeyGrid.SetColumn(button, col);
        KeyGrid.Children.Add(button);
    }

    private void UpdateKeyFilterButton()
    {
        if (string.IsNullOrEmpty(_keyFilter))
        {
            KeyFilterButton.Text = "Key";
            KeyFilterButton.BackgroundColor = Colors.Transparent;
            KeyFilterButton.TextColor = Colors.Gray;
        }
        else
        {
            KeyFilterButton.Text = _keyFilter;
            KeyFilterButton.BackgroundColor = Colors.DodgerBlue;
            KeyFilterButton.TextColor = Colors.White;
        }
    }

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
