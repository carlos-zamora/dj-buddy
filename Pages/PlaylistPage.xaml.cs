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
    private static readonly Color SameKeyColor = Color.FromArgb("#3848D868");      // green — harmony
    private static readonly Color AdjacentKeyColor = Color.FromArgb("#385B9FD4");  // blue — smooth flow
    private static readonly Color EnergyBoostColor = Color.FromArgb("#38F0883C");  // warm amber — energy up
    private static readonly Color EnergyDropColor = Color.FromArgb("#38A77BCA");   // cool purple — wind down

    private PlaylistNode? _node;
    private List<Track>? _tracks;
    private Track? _selectedTrack;
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
        BindableLayout.SetItemsSource(TrackList, hasTracks ? GetDisplayItems() : null);

        UpdateSortIndicators();
        UpdateKeyFilterButton();
        UpdateKeyLegend();
    }

    private IEnumerable<Track> GetFilteredAndSortedTracks()
    {
        if (_tracks == null) return [];

        IEnumerable<Track> filtered = _tracks;

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var terms = _searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            filtered = filtered.Where(t =>
                terms.All(term =>
                    t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    t.Artist.Contains(term, StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>
    /// Wraps filtered/sorted tracks with highlight colors based on the current selection.
    /// Groups: same key, adjacent (±1 / opposite letter), energy boost (+2/+7), energy drop (-2/-7).
    /// </summary>
    private List<TrackDisplayItem> GetDisplayItems()
    {
        var sameKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var adjacentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var boostKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dropKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_selectedTrack != null && TryParseCamelotKey(_selectedTrack.Key, out int num, out char letter))
        {
            sameKeys.Add(_selectedTrack.Key);

            char opp = letter == 'A' ? 'B' : 'A';
            adjacentKeys.Add($"{Wrap(num - 1)}{letter}");
            adjacentKeys.Add($"{Wrap(num + 1)}{letter}");
            adjacentKeys.Add($"{num}{opp}");

            boostKeys.Add($"{Wrap(num + 2)}{letter}");
            boostKeys.Add($"{Wrap(num + 7)}{letter}");

            dropKeys.Add($"{Wrap(num - 2)}{letter}");
            dropKeys.Add($"{Wrap(num - 7)}{letter}");
        }

        return GetFilteredAndSortedTracks().Select(t =>
        {
            Color? color = null;
            if (_selectedTrack != null && !string.IsNullOrEmpty(t.Key))
            {
                if (t == _selectedTrack || sameKeys.Contains(t.Key))
                    color = SameKeyColor;
                else if (adjacentKeys.Contains(t.Key))
                    color = AdjacentKeyColor;
                else if (boostKeys.Contains(t.Key))
                    color = EnergyBoostColor;
                else if (dropKeys.Contains(t.Key))
                    color = EnergyDropColor;
            }

            return new TrackDisplayItem(t, color, _selectedTrack != null && t == _selectedTrack);
        }).ToList();
    }

    /// <summary>Wraps a Camelot number into the 1–12 range.</summary>
    private static int Wrap(int n) => (n - 1 + 12) % 12 + 1;

    private static bool TryParseCamelotKey(string? key, out int number, out char letter)
    {
        number = 0;
        letter = ' ';
        if (string.IsNullOrEmpty(key) || key.Length < 2) return false;

        letter = char.ToUpper(key[^1]);
        if (letter is not ('A' or 'B')) return false;

        return int.TryParse(key.AsSpan(0, key.Length - 1), out number) && number >= 1 && number <= 12;
    }

    private void OnTrackTapped(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not TrackDisplayItem item)
            return;

        _selectedTrack = _selectedTrack == item.Track ? null : item.Track;
        BindableLayout.SetItemsSource(TrackList, GetDisplayItems());
        UpdateKeyLegend();
    }

    /// <summary>
    /// Shows or hides the key compatibility legend based on whether a track is selected
    /// and has a valid Camelot key.
    /// </summary>
    private void UpdateKeyLegend()
    {
        if (_selectedTrack == null || !TryParseCamelotKey(_selectedTrack.Key, out int num, out char letter))
        {
            KeyLegend.IsVisible = false;
            return;
        }

        char opp = letter == 'A' ? 'B' : 'A';
        string adj1 = $"{Wrap(num - 1)}{letter}";
        string adj2 = $"{Wrap(num + 1)}{letter}";
        string adj3 = $"{num}{opp}";
        string boost1 = $"{Wrap(num + 2)}{letter}";
        string boost2 = $"{Wrap(num + 7)}{letter}";
        string drop1 = $"{Wrap(num - 2)}{letter}";
        string drop2 = $"{Wrap(num - 7)}{letter}";

        LegendSameLabel.Text = $"Same key: {_selectedTrack.Key}";
        LegendAdjacentLabel.Text = $"Adjacent: {adj1}, {adj2}, {adj3}";
        LegendBoostLabel.Text = $"Energy boost: {boost1}, {boost2}";
        LegendDropLabel.Text = $"Energy drop: {drop1}, {drop2}";

        KeyLegend.IsVisible = true;
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

/// <summary>
/// Wraps a <see cref="Track"/> with an optional highlight color for display.
/// </summary>
public class TrackDisplayItem(Track track, Color? highlightColor, bool isSelected)
{
    public Track Track { get; } = track;
    public Color HighlightColor { get; } = highlightColor ?? Colors.Transparent;
    public bool IsSelected { get; } = isSelected;
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
