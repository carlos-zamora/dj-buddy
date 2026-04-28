using DJBuddy.Rekordbox.Models;
using DJBuddy.Rekordbox.Query;
using Xunit;

namespace DJBuddy.Rekordbox.Tests.Query;

/// <summary>
/// Splitting and contributor-enumeration invariants for <see cref="ArtistSplitter"/>.
/// </summary>
public class ArtistSplitterTests
{
    [Fact]
    public void SplitArtist_splits_on_comma()
    {
        var tokens = ArtistSplitter.SplitArtist("Zeds Dead, Subtronics").ToList();
        Assert.Equal(["Zeds Dead", "Subtronics"], tokens);
    }

    [Fact]
    public void SplitArtist_splits_multiple_commas()
    {
        var tokens = ArtistSplitter.SplitArtist("Inzo, ProbCause, Blookah, Mind Splitter").ToList();
        Assert.Equal(["Inzo", "ProbCause", "Blookah", "Mind Splitter"], tokens);
    }

    [Theory]
    [InlineData("Skrillex feat. Sirah")]
    [InlineData("Skrillex ft. Sirah")]
    [InlineData("Skrillex ft Sirah")]
    [InlineData("Skrillex featuring Sirah")]
    [InlineData("Skrillex FEAT. Sirah")]
    public void SplitArtist_splits_on_feat_variants(string input)
    {
        var tokens = ArtistSplitter.SplitArtist(input).ToList();
        Assert.Equal(["Skrillex", "Sirah"], tokens);
    }

    [Theory]
    [InlineData("Subtronics x Excision")]
    [InlineData("Subtronics vs Excision")]
    [InlineData("Subtronics vs. Excision")]
    public void SplitArtist_splits_on_x_and_vs(string input)
    {
        var tokens = ArtistSplitter.SplitArtist(input).ToList();
        Assert.Equal(["Subtronics", "Excision"], tokens);
    }

    [Fact]
    public void SplitArtist_does_not_split_on_ampersand()
    {
        // Above & Beyond is one act; splitting would corrupt artist counts.
        var tokens = ArtistSplitter.SplitArtist("Above & Beyond").ToList();
        Assert.Single(tokens);
        Assert.Equal("Above & Beyond", tokens[0]);
    }

    [Fact]
    public void SplitArtist_dedupes_case_insensitively()
    {
        var tokens = ArtistSplitter.SplitArtist("X, x").ToList();
        Assert.Single(tokens);
    }

    [Fact]
    public void SplitArtist_handles_mixed_delimiters()
    {
        var tokens = ArtistSplitter.SplitArtist("A, B feat. C x D").ToList();
        Assert.Equal(["A", "B", "C", "D"], tokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SplitArtist_returns_empty_for_blank(string? input)
    {
        Assert.Empty(ArtistSplitter.SplitArtist(input));
    }

    [Fact]
    public void ExtractTitleAttributions_pulls_bracketed_flip()
    {
        var attributions = ArtistSplitter
            .ExtractTitleAttributions("Outside (Ft. Ellie Goulding) [NEOTEK Flip]")
            .ToList();
        Assert.Contains("NEOTEK", attributions);
    }

    [Fact]
    public void ExtractTitleAttributions_splits_multi_name_attribution()
    {
        var attributions = ArtistSplitter
            .ExtractTitleAttributions("Foo (Subtronics x Excision Remix)")
            .ToList();
        Assert.Equal(["Subtronics", "Excision"], attributions);
    }

    [Fact]
    public void ExtractTitleAttributions_preserves_ampersand_in_remix_credit()
    {
        // Ampersand isn't a split delimiter, so duo names survive.
        var attributions = ArtistSplitter
            .ExtractTitleAttributions("Foo (A & B Remix)")
            .ToList();
        Assert.Single(attributions);
        Assert.Equal("A & B", attributions[0]);
    }

    [Fact]
    public void ExtractTitleAttributions_returns_empty_when_no_pattern()
    {
        Assert.Empty(ArtistSplitter.ExtractTitleAttributions("Just a plain title"));
    }

    [Fact]
    public void EnumerateContributors_unions_artist_and_title_attributions()
    {
        var track = new Track
        {
            Name = "Outside (Ft. Ellie Goulding) [NEOTEK Flip]",
            Artist = "Calvin Harris",
        };
        var contributors = ArtistSplitter.EnumerateContributors(track).ToList();

        Assert.Contains("Calvin Harris", contributors);
        Assert.Contains("NEOTEK", contributors);
    }

    [Fact]
    public void EnumerateContributors_dedupes_across_artist_and_title()
    {
        var track = new Track
        {
            Name = "Track (Skrillex Remix)",
            Artist = "Skrillex",
        };
        var contributors = ArtistSplitter.EnumerateContributors(track).ToList();
        Assert.Single(contributors);
    }
}
