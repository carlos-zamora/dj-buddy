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

        ContentList.ItemTemplate = new PlaylistDataTemplateSelector
        {
            HeaderTemplate = (DataTemplate)Resources["HeaderTemplate"],
            NodeTemplate = (DataTemplate)Resources["NodeTemplate"],
            TrackTemplate = (DataTemplate)Resources["TrackTemplate"],
        };
    }

    /// <summary>
    /// Combines child nodes and resolved tracks into a single list with section headers
    /// and assigns it to the CollectionView. Called when Node or Tracks is set.
    /// </summary>
    private void BuildContent()
    {
        if (_node == null) return;

        var items = new List<object>();

        if (_node.Children.Count > 0)
        {
            items.Add(new SectionHeader("Playlists"));
            items.AddRange(_node.Children);
        }

        if (_tracks?.Count > 0)
        {
            items.Add(new SectionHeader("Tracks"));
            items.AddRange(_tracks);
        }

        ContentList.ItemsSource = items;
    }

    /// <summary>
    /// Handles selection on the CollectionView. If a PlaylistNode is selected, resolves
    /// its tracks from <see cref="LibraryStore"/> and navigates to a new PlaylistPage.
    /// </summary>
    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PlaylistNode node) return;

        // Clear selection so the same item can be tapped again
        ContentList.SelectedItem = null;

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
/// Marker item for rendering section headings in the mixed-type CollectionView.
/// </summary>
public record SectionHeader(string Title);

/// <summary>
/// Selects a DataTemplate based on item type. Templates are assigned from XAML resources.
/// </summary>
public class PlaylistDataTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? NodeTemplate { get; set; }
    public DataTemplate? TrackTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        return item switch
        {
            SectionHeader => HeaderTemplate!,
            PlaylistNode => NodeTemplate!,
            _ => TrackTemplate!,
        };
    }
}
