using Rekordbox.Models;
using Rekordbox.Xml;
using Xunit;

namespace Rekordbox.Tests.Xml;

// Usage:
//   using var input = File.OpenRead("rekordbox.xml");
//   var folder = new PlaylistNode { Name = "DJ_BUDDY", IsFolder = true, Children = { ... } };
//   var bytes = await RekordboxExporter.PatchPlaylistNodeAsync(input, folder);
//   File.WriteAllBytes("rekordbox_modified.xml", bytes);
public class RekordboxExporterTests
{
    private static async Task<RekordboxLibrary> ReParse(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return await RekordboxParser.ParseAsync(ms);
    }

    [Fact]
    public async Task PatchPlaylistNodeAsync_injects_a_new_folder_under_ROOT()
    {
        using var input = TestData.OpenFixture("round_trip.xml");
        var newFolder = new PlaylistNode
        {
            Name = "INJECTED",
            IsFolder = true,
            Children = { new PlaylistNode { Name = "p", IsFolder = false, TrackKeys = { "2001" } } },
        };

        var bytes = await RekordboxExporter.PatchPlaylistNodeAsync(input, newFolder);
        var lib = await ReParse(bytes);

        var injected = lib.Root.Children.SingleOrDefault(c => c.Name == "INJECTED");
        Assert.NotNull(injected);
        Assert.True(injected!.IsFolder);
        Assert.Single(injected.Children);
        Assert.Equal("p", injected.Children[0].Name);

        // ROOT.Count updated to reflect the injection (Existing + DJ_BUDDY + INJECTED = 3).
        Assert.Equal(3, lib.Root.Children.Count);
    }

    [Fact]
    public async Task PatchPlaylistNodeAsync_replaces_existing_node_with_same_name()
    {
        using var input = TestData.OpenFixture("round_trip.xml");
        // Fixture already has a DJ_BUDDY folder with a Favorites child containing track 2001.
        var replacement = new PlaylistNode
        {
            Name = "DJ_BUDDY",
            IsFolder = true,
            Children = { new PlaylistNode { Name = "Favorites", IsFolder = false, TrackKeys = { "2002" } } },
        };

        var bytes = await RekordboxExporter.PatchPlaylistNodeAsync(input, replacement);
        var lib = await ReParse(bytes);

        // Only one DJ_BUDDY node should exist.
        var djBuddies = lib.Root.Children.Where(c => c.Name == "DJ_BUDDY").ToList();
        Assert.Single(djBuddies);

        var favs = djBuddies[0].Children.Single(c => c.Name == "Favorites");
        // The Favorites playlist was replaced — now contains 2002 instead of 2001.
        Assert.Equal(new[] { "2002" }, favs.TrackKeys);
    }

    [Fact]
    public async Task PatchPlaylistNodeAsync_replaceByName_targets_a_differently_named_node()
    {
        using var input = TestData.OpenFixture("round_trip.xml");
        var replacement = new PlaylistNode
        {
            Name = "NEW_NAME",
            IsFolder = true,
        };

        // Replace the existing "Existing" playlist with a folder named "NEW_NAME".
        var bytes = await RekordboxExporter.PatchPlaylistNodeAsync(input, replacement, replaceByName: "Existing");
        var lib = await ReParse(bytes);

        Assert.DoesNotContain(lib.Root.Children, c => c.Name == "Existing");
        Assert.Contains(lib.Root.Children, c => c.Name == "NEW_NAME");
    }

    [Fact]
    public async Task PatchPlaylistNodeAsync_round_trip_preserves_modeled_track_data()
    {
        using var input = TestData.OpenFixture("round_trip.xml");
        // No-op patch: replace DJ_BUDDY with an empty DJ_BUDDY. Track collection must be untouched.
        var noop = new PlaylistNode { Name = "DJ_BUDDY", IsFolder = true };
        var bytes = await RekordboxExporter.PatchPlaylistNodeAsync(input, noop);
        var lib = await ReParse(bytes);

        Assert.Equal(2, lib.Tracks.Count);
        var trackA = lib.Tracks["2001"];
        Assert.Equal("Track A", trackA.Name);
        Assert.Equal("Artist A", trackA.Artist);
        Assert.Equal("Album A", trackA.Album);
        Assert.Equal(124.00, trackA.Bpm);
        Assert.Equal("Am", trackA.Tonality);
        Assert.Equal("8B", trackA.Key); // Am -> 8B
    }

    [Fact]
    public async Task WriteAsync_full_serialize_round_trips_through_parser()
    {
        // Build a library from scratch in code, serialize, re-parse, assert fields survive.
        var original = new RekordboxLibrary();
        original.Tracks["9001"] = new Track
        {
            TrackId = "9001",
            Name = "Ghost Track",
            Artist = "Nobody",
            Album = "Album",
            Genre = "Test",
            Bpm = 126.50,
            Tonality = "Am",
            Key = "8B",
            Rating = 51,
            PlayCount = 3,
            DateAdded = new DateTime(2024, 3, 15),
            TotalTime = 300,
            BitRate = 320,
            SampleRate = 44100,
            Size = 1234,
            Location = "file://localhost/C:/music/ghost.mp3",
            Year = 2024,
            TempoMarks = { new TempoMark { Inizio = 0.0, Bpm = 126.50, Metro = "4/4", Battito = 1 } },
            CuePoints =
            {
                new CuePoint { Name = "Start", Type = 0, Start = 0.0, Num = 0, Red = 255, Green = 0, Blue = 0 },
                new CuePoint { Name = "Loop", Type = 4, Start = 60.0, End = 64.0, Num = -1, Red = 0, Green = 255, Blue = 0 },
            },
        };
        original.Root.Children.Add(new PlaylistNode
        {
            Name = "My Playlist",
            IsFolder = false,
            TrackKeys = { "9001" },
        });

        var bytes = await RekordboxExporter.WriteToBytesAsync(original);
        var reparsed = await ReParse(bytes);

        Assert.Single(reparsed.Tracks);
        var track = reparsed.Tracks["9001"];
        Assert.Equal("Ghost Track", track.Name);
        Assert.Equal("Nobody", track.Artist);
        Assert.Equal(126.50, track.Bpm);
        Assert.Equal("Am", track.Tonality);
        Assert.Equal(51, track.Rating);
        Assert.Equal(3, track.PlayCount);
        Assert.Equal(new DateTime(2024, 3, 15), track.DateAdded);
        Assert.Equal(300, track.TotalTime);

        Assert.Single(track.TempoMarks);
        Assert.Equal(126.50, track.TempoMarks[0].Bpm);
        Assert.Equal("4/4", track.TempoMarks[0].Metro);

        Assert.Equal(2, track.CuePoints.Count);
        var loop = track.CuePoints[1];
        Assert.Equal("Loop", loop.Name);
        Assert.Equal(4, loop.Type);
        Assert.Equal(60.0, loop.Start, precision: 3);
        Assert.Equal(64.0, loop.End!.Value, precision: 3);

        var playlist = Assert.Single(reparsed.Root.Children);
        Assert.Equal("My Playlist", playlist.Name);
        Assert.False(playlist.IsFolder);
        Assert.Equal(new[] { "9001" }, playlist.TrackKeys);
    }
}
