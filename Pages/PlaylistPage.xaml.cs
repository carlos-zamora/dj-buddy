using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using dj_buddy.Models;
using dj_buddy.Services;
using MauiIcons.Core;

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
    private List<TrackDisplayItem>? _displayItems;
    private bool _isDjBuddyPlaylist;
    private Track? _selectedTrack;
    private SortField _sortField = SortField.None;
    private bool _sortAscending = true;
    private string _searchText = "";
    private string? _keyFilter;
    private bool? _isNarrowLegend;

    /// <summary>
    /// Bound to <see cref="CommunityToolkit.Maui.Behaviors.UserStoppedTypingBehavior"/> on the
    /// search entry. Receives the current text value after the user pauses typing.
    /// </summary>
    public ICommand SearchCommand { get; }

    public PlaylistNode? Node
    {
        get => _node;
        set
        {
            _node = value;
            _isDjBuddyPlaylist = _node != null && DjBuddyPlaylistStore.DjBuddyFolder.Children.Contains(_node);
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
        SearchCommand = new Command<string>(text =>
        {
            _searchText = text ?? "";
            BuildContent();
        });
        InitializeComponent();
        // Workaround for MauiIcons URL-style namespace: https://github.com/AathifMahir/MauiIcons#workaround
        _ = new MauiIcon();
        BindingContext = this;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateKeyLegendLayout(width);
    }

    private void UpdateKeyLegendLayout(double width)
    {
        bool narrow = width < 500; // portrait phone threshold
        if (_isNarrowLegend == narrow) return; // no change, skip rebuild
        _isNarrowLegend = narrow;

        if (narrow)
        {
            // 2 columns, 2 rows — items aligned in columns
            KeyLegend.ColumnDefinitions = [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            ];
            KeyLegend.RowDefinitions = [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            ];
            Grid.SetRow(LegendSameItem, 0);     Grid.SetColumn(LegendSameItem, 0);
            Grid.SetRow(LegendAdjacentItem, 0); Grid.SetColumn(LegendAdjacentItem, 1);
            Grid.SetRow(LegendBoostItem, 1);    Grid.SetColumn(LegendBoostItem, 0);
            Grid.SetRow(LegendDropItem, 1);     Grid.SetColumn(LegendDropItem, 1);
        }
        else
        {
            // 4 columns, 1 row — all items side by side
            KeyLegend.ColumnDefinitions = [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            ];
            KeyLegend.RowDefinitions = [new RowDefinition(GridLength.Auto)];
            Grid.SetRow(LegendSameItem, 0);     Grid.SetColumn(LegendSameItem, 0);
            Grid.SetRow(LegendAdjacentItem, 0); Grid.SetColumn(LegendAdjacentItem, 1);
            Grid.SetRow(LegendBoostItem, 0);    Grid.SetColumn(LegendBoostItem, 2);
            Grid.SetRow(LegendDropItem, 0);     Grid.SetColumn(LegendDropItem, 3);
        }
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
        KeyLegend.IsVisible = hasTracks;
        var trackTemplate = (DataTemplate)Resources[_isDjBuddyPlaylist ? "DjBuddyTrackTemplate" : "TrackTemplate"];
        TrackList.ItemTemplate = trackTemplate;
        _displayItems = hasTracks ? GetDisplayItems() : null;
        TrackList.ItemsSource = _displayItems;

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
    /// Builds Camelot key sets for the currently selected track.
    /// All sets are empty when no track is selected or its key is unparseable.
    /// </summary>
    private void BuildKeyGroups(
        out HashSet<string> sameKeys,
        out HashSet<string> adjacentKeys,
        out HashSet<string> boostKeys,
        out HashSet<string> dropKeys)
    {
        sameKeys    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        adjacentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        boostKeys   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        dropKeys    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_selectedTrack == null || !TryParseCamelotKey(_selectedTrack.Key, out int num, out char letter))
            return;

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

    /// <summary>
    /// Returns the highlight color for a track given the current key groups, or null for no highlight.
    /// </summary>
    private Color? HighlightColorFor(Track t,
        HashSet<string> sameKeys, HashSet<string> adjacentKeys,
        HashSet<string> boostKeys, HashSet<string> dropKeys)
    {
        if (_selectedTrack == null || string.IsNullOrEmpty(t.Key)) return null;
        if (t == _selectedTrack || sameKeys.Contains(t.Key))  return SameKeyColor;
        if (adjacentKeys.Contains(t.Key))                      return AdjacentKeyColor;
        if (boostKeys.Contains(t.Key))                         return EnergyBoostColor;
        if (dropKeys.Contains(t.Key))                          return EnergyDropColor;
        return null;
    }

    /// <summary>
    /// Wraps filtered/sorted tracks with highlight colors based on the current selection.
    /// Groups: same key, adjacent (±1 / opposite letter), energy boost (+2/+7), energy drop (-2/-7).
    /// </summary>
    private List<TrackDisplayItem> GetDisplayItems()
    {
        BuildKeyGroups(out var sameKeys, out var adjacentKeys, out var boostKeys, out var dropKeys);
        return GetFilteredAndSortedTracks()
            .Select(t => new TrackDisplayItem(t,
                HighlightColorFor(t, sameKeys, adjacentKeys, boostKeys, dropKeys),
                _selectedTrack != null && t == _selectedTrack))
            .ToList();
    }

    /// <summary>
    /// Updates highlight colors and selection state on the existing display items in-place.
    /// Avoids replacing ItemsSource (which resets CollectionView scroll position).
    /// </summary>
    private void RefreshHighlightColors()
    {
        if (_displayItems == null) return;
        BuildKeyGroups(out var sameKeys, out var adjacentKeys, out var boostKeys, out var dropKeys);
        foreach (var di in _displayItems)
        {
            di.HighlightColor = HighlightColorFor(di.Track, sameKeys, adjacentKeys, boostKeys, dropKeys) ?? Colors.Transparent;
            di.IsSelected = _selectedTrack != null && di.Track == _selectedTrack;
        }
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
        RefreshHighlightColors();
        UpdateKeyLegend();
    }

    private async void OnAddToSwipeInvoked(object? sender, EventArgs e)
    {
        var item = (sender as SwipeItem)?.BindingContext as TrackDisplayItem
                ?? (sender as BindableObject)?.BindingContext as TrackDisplayItem;
        if (item != null)
            await ShowAddToPlaylistSheet(item);
    }

    private async void OnRemoveFromDjBuddyInvoked(object? sender, EventArgs e)
    {
        var item = (sender as SwipeItem)?.BindingContext as TrackDisplayItem
                ?? (sender as BindableObject)?.BindingContext as TrackDisplayItem;
        if (item == null || _node == null) return;

        _node.TrackKeys.Remove(item.Track.TrackId);
        _tracks?.Remove(item.Track);
        await DjBuddyPlaylistStore.SaveAsync();
        BuildContent();
    }

    private async void OnTrackRightClicked(object? sender, EventArgs e)
    {
        // Buttons.Secondary is not supported on touch idioms — both recognizers fire on a
        // normal tap, which would open the popup instead of just selecting the track.
        // Swipe handles "add" on Phone/Tablet; this handler is Desktop-only.
        var idiom = DeviceInfo.Idiom;
        if (idiom == DeviceIdiom.Phone || idiom == DeviceIdiom.Tablet)
            return;

        if (sender is not BindableObject bindable || bindable.BindingContext is not TrackDisplayItem item)
            return;
        await ShowAddToPlaylistSheet(item);
    }

    /// <summary>
    /// Shows an action sheet letting the user add a track to Favorites,
    /// an existing DJ Buddy playlist, or a newly created one.
    /// </summary>
    private async Task ShowAddToPlaylistSheet(TrackDisplayItem item)
    {
        var options = new List<string> { "\u2B50 Favorites" };
        options.AddRange(DjBuddyPlaylistStore.GetPlaylistNames());
        options.Add("+ New playlist...");

        var trackName = item.Track.Name;
        if (trackName.Length > 30)
            trackName = trackName[..27] + "...";

        var result = await DisplayActionSheetAsync(
            $"Add \"{trackName}\"", "Cancel", null, options.ToArray());

        if (string.IsNullOrEmpty(result) || result == "Cancel") return;

        if (result == "+ New playlist...")
        {
            var name = await DisplayPromptAsync("New Playlist", "Enter playlist name:",
                initialValue: item.Track.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            DjBuddyPlaylistStore.CreatePlaylist(name);
            DjBuddyPlaylistStore.AddTrackToPlaylist(name, item.Track.TrackId);
        }
        else if (result == "\u2B50 Favorites")
        {
            DjBuddyPlaylistStore.AddTrackToFavorites(item.Track.TrackId);
        }
        else
        {
            DjBuddyPlaylistStore.AddTrackToPlaylist(result, item.Track.TrackId);
        }

        await DjBuddyPlaylistStore.SaveAsync();
    }

    /// <summary>
    /// Updates the key compatibility legend labels. When a track with a valid Camelot key
    /// is selected, shows the specific compatible keys; otherwise shows generic category labels.
    /// Visibility is controlled by <see cref="BuildContent"/>.
    /// </summary>
    private void UpdateKeyLegend()
    {
        if (_selectedTrack == null || !TryParseCamelotKey(_selectedTrack.Key, out int num, out char letter))
        {
            LegendSameLabel.Text = "Same key";
            LegendAdjacentLabel.Text = "Adjacent keys";
            LegendBoostLabel.Text = "Energy boost";
            LegendDropLabel.Text = "Energy drop";
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
    }

    private void UpdateSortIndicators()
    {
        TitleHeaderLabel.Text = "Title" + SortIndicator(SortField.Title);
        BpmHeaderLabel.Text = "BPM" + SortIndicator(SortField.Bpm);
        KeyHeaderLabel.Text = "Key" + SortIndicator(SortField.Key);
    }

    private string SortIndicator(SortField field) =>
        _sortField != field ? "" : _sortAscending ? " \u25B2" : " \u25BC";

    /// <summary>
    /// Opens the Camelot key picker popup. On return, updates the key filter if
    /// the user selected a key or cleared it; does nothing if they dismissed by
    /// tapping outside.
    /// </summary>
    private async void OnKeyFilterButtonClicked(object? sender, EventArgs e)
    {
        var popup = new KeyPickerPopup(_keyFilter);

        popup.KeySelected += async (_, key) =>
            await this.ClosePopupAsync(key, CancellationToken.None);
        popup.FilterCleared += async (_, _) =>
            await this.ClosePopupAsync(string.Empty, CancellationToken.None);

        var result = await this.ShowPopupAsync<string>(popup, new PopupOptions(), CancellationToken.None);

        if (!result.WasDismissedByTappingOutsideOfPopup)
        {
            _keyFilter = string.IsNullOrEmpty(result.Result) ? null : result.Result;
            BuildContent();
        }
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

        if (_selectedTrack != null)
            _ = ScrollToSelectedAsync();
    }

    /// <summary>
    /// Scrolls the selected track into view after a sort or filter rebuilds the list.
    /// Delays one timer tick so the CollectionView layout pass for the new ItemsSource
    /// completes before ScrollTo is called (Task.Yield is not enough).
    /// </summary>
    private async Task ScrollToSelectedAsync()
    {
        if (_selectedTrack == null || _displayItems == null) return;

        await Task.Delay(1);

        var index = _displayItems.FindIndex(i => i.Track == _selectedTrack);
        if (index < 0) return;

        TrackList.ScrollTo(index, position: ScrollToPosition.MakeVisible, animate: true);
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
/// Implements <see cref="INotifyPropertyChanged"/> so highlight and selection state
/// can be updated in-place without replacing the CollectionView's ItemsSource.
/// </summary>
public class TrackDisplayItem(Track track, Color? highlightColor, bool isSelected) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public Track Track { get; } = track;

    private Color _highlightColor = highlightColor ?? Colors.Transparent;
    public Color HighlightColor
    {
        get => _highlightColor;
        set { _highlightColor = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HighlightColor))); }
    }

    private bool _isSelected = isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
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
