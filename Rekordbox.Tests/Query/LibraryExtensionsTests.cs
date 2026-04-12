using Rekordbox.Models;
using Rekordbox.Query;
using Xunit;

namespace Rekordbox.Tests.Query;

// Usage:
//   var tracks = playlistNode.GetTracks(library);
//   var all = library.Root.EnumeratePlaylists();
//   var favorites = library.Root.FindByName("Favorites");
public class LibraryExtensionsTests
{
    private static RekordboxLibrary BuildLibrary()
    {
        var lib = new RekordboxLibrary();
        lib.Tracks["1"] = new Track { TrackId = "1", Name = "One" };
        lib.Tracks["2"] = new Track { TrackId = "2", Name = "Two" };
        lib.Tracks["3"] = new Track { TrackId = "3", Name = "Three" };

        lib.Root.Children.Add(new PlaylistNode
        {
            Name = "Top",
            IsFolder = false,
            TrackKeys = { "1", "2", "missing", "3" },
        });
        lib.Root.Children.Add(new PlaylistNode
        {
            Name = "Folder",
            IsFolder = true,
            Children =
            {
                new PlaylistNode { Name = "Nested", IsFolder = false, TrackKeys = { "2" } },
                new PlaylistNode
                {
                    Name = "Subfolder",
                    IsFolder = true,
                    Children = { new PlaylistNode { Name = "Deep", IsFolder = false, TrackKeys = { "3" } } },
                },
            },
        });
        return lib;
    }

    [Fact]
    public void GetTracks_resolves_ids_in_playlist_order()
    {
        var lib = BuildLibrary();
        var top = lib.Root.Children[0];

        var tracks = top.GetTracks(lib).ToList();

        // 3 resolved, "missing" skipped silently.
        Assert.Equal(new[] { "1", "2", "3" }, tracks.Select(t => t.TrackId));
    }

    [Fact]
    public void GetTracks_skips_missing_ids_without_throwing()
    {
        var lib = BuildLibrary();
        var badPlaylist = new PlaylistNode
        {
            Name = "Bad",
            IsFolder = false,
            TrackKeys = { "does-not-exist", "also-missing" },
        };

        Assert.Empty(badPlaylist.GetTracks(lib));
    }

    [Fact]
    public void EnumeratePlaylists_returns_every_non_folder_node_depth_first()
    {
        var lib = BuildLibrary();
        var playlists = lib.Root.EnumeratePlaylists().ToList();

        // Top (order matters — pre-order DFS), then Nested, then Deep.
        Assert.Equal(new[] { "Top", "Nested", "Deep" }, playlists.Select(p => p.Name));
        Assert.All(playlists, p => Assert.False(p.IsFolder));
    }

    [Fact]
    public void EnumeratePlaylists_excludes_folders()
    {
        var lib = BuildLibrary();
        var playlists = lib.Root.EnumeratePlaylists().ToList();
        Assert.DoesNotContain(playlists, p => p.Name == "Folder");
        Assert.DoesNotContain(playlists, p => p.Name == "Subfolder");
    }

    [Fact]
    public void FindByName_returns_first_match_depth_first()
    {
        var lib = BuildLibrary();
        var nested = lib.Root.FindByName("Nested");
        Assert.NotNull(nested);
        Assert.False(nested!.IsFolder);

        // Folders are matchable too.
        var folder = lib.Root.FindByName("Folder");
        Assert.NotNull(folder);
        Assert.True(folder!.IsFolder);
    }

    [Fact]
    public void FindByName_returns_null_when_missing()
    {
        Assert.Null(BuildLibrary().Root.FindByName("nope"));
    }
}
