using CommunityToolkit.Maui.Layouts;
using DJBuddy.MAUI.Services;
using DJBuddy.Rekordbox.Graph;
using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using MauiIcons.Core;

namespace DJBuddy.MAUI.Pages;

/// <summary>
/// Detail view for a single <see cref="Track"/>. Shows metadata and three lazily-populated
/// related-track groupings: playlists that contain the track, harmonically-compatible
/// neighbors (from <see cref="CompatibilityEdge"/>), and playlist co-occurrence neighbors
/// (from <see cref="CoOccurrenceEdge"/>). Graph-backed groupings await the shared
/// <see cref="GraphStore.GraphTask"/> and render a Loading state in the meantime.
/// </summary>
[QueryProperty(nameof(Track), "Track")]
public partial class TrackInfoPage : ContentPage
{
    /// <summary>Maximum number of related tracks shown per graph-backed grouping. Tune here.</summary>
    public const int RelatedTracksLimit = 30;

    /// <summary>
    /// How aggressively playlist co-occurrence pulls a "Mixes well" candidate up in the ranking.
    /// The adjusted weight subtracts <c>log(1 + sharedPlaylists) * CoOccurrenceBoostFactor</c>
    /// from the raw <see cref="CompatibilityEdge.Weight"/>; higher values favor tracks the user
    /// has already grouped together in playlists over purely harmonic/BPM neighbors.
    /// </summary>
    /// <remarks>Tunable: 0.0 disables the boost entirely; compatibility weights are roughly 0–3.</remarks>
    public const double CoOccurrenceBoostFactor = 0.5;

    private static readonly Color SameKeyColor = Color.FromArgb("#3848D868");
    private static readonly Color AdjacentKeyColor = Color.FromArgb("#385B9FD4");
    private static readonly Color EnergyBoostColor = Color.FromArgb("#38F0883C");
    private static readonly Color EnergyDropColor = Color.FromArgb("#38A77BCA");

    private static readonly Color SameKeyBar = Color.FromArgb("#48D868");
    private static readonly Color AdjacentKeyBar = Color.FromArgb("#5B9FD4");
    private static readonly Color EnergyBoostBar = Color.FromArgb("#F0883C");
    private static readonly Color EnergyDropBar = Color.FromArgb("#A77BCA");

    private Track? _track;
    private Grouping _grouping = Grouping.AppearsOn;

    private List<PlaylistRow>? _cachedPlaylists;
    private List<CompatibleTrackRow>? _cachedCompatible;
    private List<CoOccurTrackRow>? _cachedCoOccur;

    public TrackInfoPage()
    {
        InitializeComponent();
        // Workaround for MauiIcons URL-style namespace: https://github.com/AathifMahir#workaround
        _ = new MauiIcon();
        StateContainer.SetCurrentState(ContentRoot, null);
        UpdateSegmentedButtons();
    }

    /// <summary>
    /// The track to display. Set by Shell navigation; triggers a rebuild of the header and
    /// clears cached grouping results.
    /// </summary>
    public Track? Track
    {
        get => _track;
        set
        {
            _track = value;
            _cachedPlaylists = null;
            _cachedCompatible = null;
            _cachedCoOccur = null;
            PopulateHeader();
            // Start on "Appears on" — it's library-only and needs no graph.
            _grouping = Grouping.AppearsOn;
            UpdateSegmentedButtons();
            _ = LoadCurrentGroupingAsync();
        }
    }

    /// <summary>Populates the header metadata chips and secondary details from <see cref="_track"/>.</summary>
    private void PopulateHeader()
    {
        if (_track == null) return;

        Title = string.IsNullOrWhiteSpace(_track.Name) ? "Track Info" : _track.Name;
        TitleLabel.Text = string.IsNullOrWhiteSpace(_track.Name) ? "(Untitled)" : _track.Name;
        ArtistLabel.Text = string.IsNullOrWhiteSpace(_track.Artist) ? "Unknown artist" : _track.Artist;
        AlbumLabel.Text = string.IsNullOrWhiteSpace(_track.Album) ? "" : _track.Album;
        AlbumLabel.IsVisible = !string.IsNullOrWhiteSpace(_track.Album);

        BpmChipLabel.Text = $"{_track.BpmDisplay} BPM";
        KeyChipLabel.Text = $"Key {_track.KeyDisplay}";
        GenreChipLabel.Text = string.IsNullOrWhiteSpace(_track.Genre) ? "No genre" : _track.Genre;
        DurationChipLabel.Text = FormatDuration(_track.TotalTime);
        RatingChipLabel.Text = FormatRating(_track.Rating);

        DateAddedLabel.Text = _track.DateAdded?.ToString("yyyy-MM-dd") ?? "—";
        CommentsLabel.Text = _track.Comments ?? "";
        CommentsLabel.IsVisible = !string.IsNullOrWhiteSpace(_track.Comments);
    }

    /// <summary>Converts a duration in seconds into <c>m:ss</c> or <c>h:mm:ss</c>.</summary>
    private static string FormatDuration(int totalSeconds)
    {
        if (totalSeconds <= 0) return "—";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// Rekordbox encodes star ratings as 0/51/102/153/204/255; map back to 0–5 stars.
    /// </summary>
    private static string FormatRating(int rating)
    {
        var stars = rating switch
        {
            >= 255 => 5,
            >= 204 => 4,
            >= 153 => 3,
            >= 102 => 2,
            >= 51 => 1,
            _ => 0,
        };
        return stars == 0 ? "No rating" : new string('\u2605', stars);
    }

    private void OnAppearsOnClicked(object? sender, EventArgs e) => SwitchGrouping(Grouping.AppearsOn);
    private void OnMixesWellClicked(object? sender, EventArgs e) => SwitchGrouping(Grouping.MixesWell);
    private void OnYouMayLikeClicked(object? sender, EventArgs e) => SwitchGrouping(Grouping.YouMayLike);

    private async void SwitchGrouping(Grouping g)
    {
        if (_grouping == g) return;
        _grouping = g;
        UpdateSegmentedButtons();
        await LoadCurrentGroupingAsync();
    }

    /// <summary>Highlights the selected segmented button and greys out the others.</summary>
    private void UpdateSegmentedButtons()
    {
        StyleButton(AppearsOnButton, _grouping == Grouping.AppearsOn);
        StyleButton(MixesWellButton, _grouping == Grouping.MixesWell);
        StyleButton(YouMayLikeButton, _grouping == Grouping.YouMayLike);
    }

    private static void StyleButton(Button button, bool active)
    {
        button.BackgroundColor = active ? Colors.DodgerBlue : Colors.Transparent;
        button.TextColor = active ? Colors.White : Colors.Gray;
        button.BorderColor = active ? Colors.DodgerBlue : Colors.Gray;
        button.BorderWidth = 1;
    }

    /// <summary>
    /// Loads and displays the list for the currently selected grouping. Graph-backed groupings
    /// show the Loading state while awaiting <see cref="GraphStore.GraphTask"/>.
    /// </summary>
    private async Task LoadCurrentGroupingAsync()
    {
        if (_track == null) return;

        switch (_grouping)
        {
            case Grouping.AppearsOn:
                ShowList();
                var rows = _cachedPlaylists ??= BuildAppearsOn(_track);
                ResultsList.ItemTemplate = (DataTemplate)Resources["PlaylistRowTemplate"];
                ResultsList.ItemsSource = rows;
                if (rows.Count == 0)
                    ShowEmpty("This track is not in any playlist.");
                break;

            case Grouping.MixesWell:
                if (_cachedCompatible == null)
                {
                    if (GraphStore.GraphTask == null)
                    {
                        ShowEmpty("Library not loaded.");
                        return;
                    }
                    ShowLoading();
                    var graph = await GraphStore.GraphTask;
                    _cachedCompatible = BuildMixesWell(graph, _track);
                }
                ShowList();
                ResultsList.ItemTemplate = (DataTemplate)Resources["CompatibleRowTemplate"];
                ResultsList.ItemsSource = _cachedCompatible;
                if (_cachedCompatible.Count == 0)
                    ShowEmpty("No harmonically compatible tracks found.");
                break;

            case Grouping.YouMayLike:
                if (_cachedCoOccur == null)
                {
                    if (GraphStore.GraphTask == null)
                    {
                        ShowEmpty("Library not loaded.");
                        return;
                    }
                    ShowLoading();
                    var graph = await GraphStore.GraphTask;
                    _cachedCoOccur = BuildYouMayLike(graph, _track);
                }
                ShowList();
                ResultsList.ItemTemplate = (DataTemplate)Resources["CoOccurRowTemplate"];
                ResultsList.ItemsSource = _cachedCoOccur;
                if (_cachedCoOccur.Count == 0)
                    ShowEmpty("No co-occurrence data for this track.");
                break;
        }
    }

    private void ShowLoading() => StateContainer.SetCurrentState(ContentRoot, "Loading");

    private void ShowList() => StateContainer.SetCurrentState(ContentRoot, null);

    private void ShowEmpty(string message)
    {
        EmptyLabel.Text = message;
        StateContainer.SetCurrentState(ContentRoot, "Empty");
    }

    /// <summary>Finds every playlist containing this track's ID.</summary>
    private static List<PlaylistRow> BuildAppearsOn(Track track)
    {
        var library = LibraryStore.Library;
        if (library == null) return [];

        var rows = new List<PlaylistRow>();
        foreach (var playlist in library.Root.EnumeratePlaylists())
        {
            if (playlist.TrackKeys.Contains(track.TrackId))
                rows.Add(new PlaylistRow(playlist, playlist.TrackKeys.Count));
        }
        return rows;
    }

    /// <summary>
    /// Top N compatibility neighbors for this track, ordered by an adjusted weight that blends
    /// raw <see cref="CompatibilityEdge.Weight"/> with a log-scaled co-occurrence bonus. Tracks
    /// the user has already grouped into playlists alongside this one are pulled up in the
    /// ranking — a harmonic-only match still ranks if no co-occurrence exists, but shared-playlist
    /// history breaks ties in favor of tracks that actually sound good together in practice.
    /// </summary>
    private static List<CompatibleTrackRow> BuildMixesWell(
        QuikGraph.BidirectionalGraph<Track, TrackEdge> graph, Track track)
    {
        if (!graph.TryGetOutEdges(track, out var outs))
            return [];

        var outList = outs.ToList();

        // Build a TrackId -> CoOccurrenceEdge lookup once so each compatibility candidate can
        // check whether a playlist-co-occurrence edge also exists to the same neighbor.
        var coOccurByTarget = outList.OfType<CoOccurrenceEdge>()
            .ToDictionary(e => e.Target.TrackId, e => e);

        return outList.OfType<CompatibilityEdge>()
            .Select(e =>
            {
                coOccurByTarget.TryGetValue(e.Target.TrackId, out var coOccur);
                var shared = coOccur?.PlaylistCount ?? 0;
                // Lower weight = better. Subtract a log-scaled bonus so large shared counts
                // stop mattering linearly (going from 1→2 shared playlists matters more than
                // 10→11). Guaranteed ≥ 0 because shared is non-negative.
                var boost = Math.Log(1.0 + shared) * CoOccurrenceBoostFactor;
                var adjusted = e.Weight - boost;
                return (edge: e, shared, adjusted);
            })
            .OrderBy(x => x.adjusted)
            .Take(RelatedTracksLimit)
            .Select(x => new CompatibleTrackRow(
                x.edge.Target,
                x.edge.Relation,
                BarColorFor(x.edge.Relation),
                BackgroundColorFor(x.edge.Relation),
                BuildRelationLabel(x.edge, x.shared)))
            .ToList();
    }

    /// <summary>Top N co-occurrence neighbors, ordered by shared-playlist count (desc) then weight.</summary>
    private static List<CoOccurTrackRow> BuildYouMayLike(
        QuikGraph.BidirectionalGraph<Track, TrackEdge> graph, Track track)
    {
        if (!graph.TryGetOutEdges(track, out var outs))
            return [];

        return outs.OfType<CoOccurrenceEdge>()
            .OrderByDescending(e => e.PlaylistCount)
            .ThenBy(e => e.Weight)
            .Take(RelatedTracksLimit)
            .Select(e => new CoOccurTrackRow(
                e.Target,
                e.PlaylistCount,
                e.PlaylistCount == 1 ? "1 shared playlist" : $"{e.PlaylistCount} shared playlists"))
            .ToList();
    }

    private static Color BarColorFor(HarmonicRelation r) => r switch
    {
        HarmonicRelation.Same => SameKeyBar,
        HarmonicRelation.Adjacent => AdjacentKeyBar,
        HarmonicRelation.EnergyBoost => EnergyBoostBar,
        HarmonicRelation.EnergyDrop => EnergyDropBar,
        _ => Colors.Transparent,
    };

    private static Color BackgroundColorFor(HarmonicRelation r) => r switch
    {
        HarmonicRelation.Same => SameKeyColor,
        HarmonicRelation.Adjacent => AdjacentKeyColor,
        HarmonicRelation.EnergyBoost => EnergyBoostColor,
        HarmonicRelation.EnergyDrop => EnergyDropColor,
        _ => Colors.Transparent,
    };

    /// <summary>
    /// Builds the subtitle shown on a "Mixes well" row: harmonic relation name (+ half-time mark
    /// when applicable, + shared playlist count when the neighbor also co-occurs).
    /// </summary>
    private static string BuildRelationLabel(CompatibilityEdge e, int sharedPlaylists)
    {
        var relation = e.Relation switch
        {
            HarmonicRelation.Same => "Same key",
            HarmonicRelation.Adjacent => "Adjacent",
            HarmonicRelation.EnergyBoost => "Energy boost",
            HarmonicRelation.EnergyDrop => "Energy drop",
            _ => e.Relation.ToString(),
        };
        var half = e.IsHalfTimeMatch ? " \u00BD" : "";
        var shared = sharedPlaylists > 0
            ? $" \u2022 {sharedPlaylists} shared"
            : "";
        return $"{relation}{half}{shared}";
    }

    /// <summary>Navigates into a playlist the track appears on.</summary>
    private async void OnPlaylistRowTapped(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not PlaylistRow row)
            return;

        var library = LibraryStore.Library;
        if (library == null) return;

        var resolved = row.Node.GetTracks(library).ToList();

        await Shell.Current.GoToAsync("playlist", new Dictionary<string, object>
        {
            { "Node", row.Node },
            { "Tracks", resolved },
        });
    }

    /// <summary>Pushes a new <see cref="TrackInfoPage"/> for the tapped related track.</summary>
    private async void OnRelatedTrackTapped(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bindable) return;

        Track? target = bindable.BindingContext switch
        {
            CompatibleTrackRow c => c.Track,
            CoOccurTrackRow co => co.Track,
            _ => null,
        };
        if (target == null) return;

        await Shell.Current.GoToAsync("trackinfo", new Dictionary<string, object>
        {
            { "Track", target },
        });
    }

    private enum Grouping { AppearsOn, MixesWell, YouMayLike }
}

/// <summary>Row in the "Appears on..." list.</summary>
public class PlaylistRow(Rekordbox.Models.PlaylistNode node, int trackCount)
{
    public Rekordbox.Models.PlaylistNode Node { get; } = node;
    public string Name => Node.Name;
    public int TrackCount { get; } = trackCount;
    public string TrackCountDisplay => TrackCount == 1 ? "1 track" : $"{TrackCount} tracks";
}

/// <summary>Row in the "Mixes well with..." list (harmonic/BPM compatibility).</summary>
public class CompatibleTrackRow(
    Rekordbox.Models.Track track,
    Rekordbox.Graph.HarmonicRelation relation,
    Color accentBarColor,
    Color accentColor,
    string hintLabel)
{
    public Rekordbox.Models.Track Track { get; } = track;
    public Rekordbox.Graph.HarmonicRelation Relation { get; } = relation;
    public Color AccentBarColor { get; } = accentBarColor;
    public Color AccentColor { get; } = accentColor;

    /// <summary>Subtitle shown below the artist name (e.g. "Adjacent • 3 shared").</summary>
    public string HintLabel { get; } = hintLabel;

    /// <summary>Whether the hint line has content; collapses the label when empty.</summary>
    public bool HasHint => !string.IsNullOrEmpty(HintLabel);
}

/// <summary>Row in the "You may also like..." list (playlist co-occurrence).</summary>
public class CoOccurTrackRow(Rekordbox.Models.Track track, int playlistCount, string sharedLabel)
{
    public Rekordbox.Models.Track Track { get; } = track;
    public int PlaylistCount { get; } = playlistCount;
    public string SharedLabel { get; } = sharedLabel;
}
