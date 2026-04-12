using Rekordbox.Models;
using Rekordbox.Xml;
using Xunit;

namespace Rekordbox.Tests.Xml;

// Usage:
//   using var stream = File.OpenRead("rekordbox.xml");
//   var library = await RekordboxParser.ParseAsync(stream);
//   var track = library.Tracks["1001"];
public class RekordboxParserTests
{
    private static async Task<RekordboxLibrary> ParseMinimal(ParseOptions? options = null)
    {
        using var stream = TestData.OpenFixture("minimal.xml");
        return options is null
            ? await RekordboxParser.ParseAsync(stream)
            : await RekordboxParser.ParseAsync(stream, options);
    }

    [Fact]
    public async Task Parses_all_tracks_into_dictionary()
    {
        var library = await ParseMinimal();
        Assert.Equal(3, library.Tracks.Count);
        Assert.Contains("1001", library.Tracks.Keys);
        Assert.Contains("1002", library.Tracks.Keys);
        Assert.Contains("1003", library.Tracks.Keys);
    }

    [Fact]
    public async Task Parses_every_known_attribute_on_fully_populated_track()
    {
        var library = await ParseMinimal();
        var track = library.Tracks["1001"];

        Assert.Equal("1001", track.TrackId);
        Assert.Equal("Strobe", track.Name);
        Assert.Equal("deadmau5", track.Artist);
        Assert.Equal("Joel Zimmerman", track.Composer);
        Assert.Equal("For Lack Of A Better Name", track.Album);
        Assert.Equal("Progressive", track.Grouping);
        Assert.Equal("Progressive House", track.Genre);
        Assert.Equal("MP3 File", track.Kind);
        Assert.Equal(15000000L, track.Size);
        Assert.Equal(634, track.TotalTime);
        Assert.Equal(1, track.DiscNumber);
        Assert.Equal(9, track.TrackNumber);
        Assert.Equal(2009, track.Year);
        Assert.Equal(128.00, track.Bpm);
        Assert.Equal(new DateTime(2023, 1, 15), track.DateAdded);
        Assert.Equal(new DateTime(2023, 6, 1), track.DateModified);
        Assert.Equal(320, track.BitRate);
        Assert.Equal(44100, track.SampleRate);
        Assert.Equal("Classic", track.Comments);
        Assert.Equal(42, track.PlayCount);
        Assert.Equal(204, track.Rating);
        Assert.Equal("file://localhost/C:/music/strobe.mp3", track.Location);
        Assert.Equal("Bbm", track.Tonality);
        Assert.Equal("3B", track.Key); // Bbm -> 3B
        Assert.Equal("mau5trap", track.Label);
        Assert.Equal("Original", track.Mix);
    }

    [Fact]
    public async Task Handles_self_closing_and_non_empty_track_forms()
    {
        var library = await ParseMinimal();
        // Track 1 is self-closing, track 2 has nested children — both must land in the dictionary.
        Assert.True(library.Tracks.ContainsKey("1001"));
        Assert.True(library.Tracks.ContainsKey("1002"));
    }

    [Fact]
    public async Task Nested_tempo_elements_land_in_tempo_marks()
    {
        var library = await ParseMinimal();
        var track = library.Tracks["1002"];

        Assert.Equal(2, track.TempoMarks.Count);

        var first = track.TempoMarks[0];
        Assert.Equal(0.014, first.Inizio, precision: 3);
        Assert.Equal(128.00, first.Bpm);
        Assert.Equal("4/4", first.Metro);
        Assert.Equal(1, first.Battito);

        var second = track.TempoMarks[1];
        Assert.Equal(60.000, second.Inizio, precision: 3);
        Assert.Equal(128.50, second.Bpm);
        Assert.Equal(3, second.Battito);
    }

    [Fact]
    public async Task Nested_position_mark_elements_land_in_cue_points()
    {
        var library = await ParseMinimal();
        var track = library.Tracks["1002"];

        Assert.Equal(3, track.CuePoints.Count);

        var intro = track.CuePoints[0];
        Assert.Equal("Intro", intro.Name);
        Assert.Equal(0, intro.Type);
        Assert.Equal(0.500, intro.Start, precision: 3);
        Assert.Equal(0, intro.Num);
        Assert.Equal(40, intro.Red);
        Assert.Equal(226, intro.Green);
        Assert.Equal(20, intro.Blue);
        Assert.Null(intro.End);

        var loop = track.CuePoints[2];
        Assert.Equal("Loop A", loop.Name);
        Assert.Equal(4, loop.Type);
        Assert.Equal(120.000, loop.Start, precision: 3);
        Assert.Equal(124.000, loop.End!.Value, precision: 3);
        Assert.Equal(-1, loop.Num);
    }

    [Fact]
    public async Task Unknown_attributes_are_preserved_by_default()
    {
        var library = await ParseMinimal();
        var track = library.Tracks["1001"];
        Assert.True(track.ExtraAttributes.ContainsKey("CustomTag"));
        Assert.Equal("banger", track.ExtraAttributes["CustomTag"]);
    }

    [Fact]
    public async Task Unknown_attributes_are_dropped_when_preserve_disabled()
    {
        var library = await ParseMinimal(new ParseOptions { PreserveUnknownAttributes = false });
        var track = library.Tracks["1001"];
        Assert.Empty(track.ExtraAttributes);
    }

    [Fact]
    public async Task Read_tempo_marks_flag_skips_tempo_elements()
    {
        var library = await ParseMinimal(new ParseOptions { ReadTempoMarks = false });
        var track = library.Tracks["1002"];
        Assert.Empty(track.TempoMarks);
        // CuePoints still populated since ReadCuePoints defaults to true.
        Assert.NotEmpty(track.CuePoints);
    }

    [Fact]
    public async Task Read_cue_points_flag_skips_position_mark_elements()
    {
        var library = await ParseMinimal(new ParseOptions { ReadCuePoints = false });
        var track = library.Tracks["1002"];
        Assert.Empty(track.CuePoints);
        Assert.NotEmpty(track.TempoMarks);
    }

    [Fact]
    public async Task Missing_attributes_leave_defaults()
    {
        var library = await ParseMinimal();
        var track = library.Tracks["1003"];
        Assert.Null(track.DateAdded);
        Assert.Equal("", track.Artist);
        Assert.Equal(0.0, track.Bpm);
        Assert.Equal("", track.Key);
    }

    [Fact]
    public async Task Playlists_tree_is_parsed_with_folders_and_playlists()
    {
        var library = await ParseMinimal();
        var root = library.Root;

        Assert.True(root.IsFolder);
        Assert.Equal("ROOT", root.Name);
        Assert.Equal(2, root.Children.Count);

        var topPlaylist = root.Children[0];
        Assert.False(topPlaylist.IsFolder);
        Assert.Equal("Top-level Playlist", topPlaylist.Name);
        Assert.Equal(new[] { "1001", "1002" }, topPlaylist.TrackKeys);

        var folderA = root.Children[1];
        Assert.True(folderA.IsFolder);
        Assert.Equal("Folder A", folderA.Name);
        Assert.Single(folderA.Children);

        var nested = folderA.Children[0];
        Assert.False(nested.IsFolder);
        Assert.Equal("Nested Playlist", nested.Name);
        Assert.Equal(new[] { "1003" }, nested.TrackKeys);
    }
}
