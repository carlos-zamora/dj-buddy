using DJBuddy.Rekordbox.Xml;
using Xunit;

namespace DJBuddy.Rekordbox.Tests.Xml;

// Usage: KeyConverter.ToCamelotNotation("Gm") => "6B"
//        KeyConverter.ToCamelotNotation("6A") => "6A" (already Camelot, passes through)
public class KeyConverterTests
{
    [Theory]
    // Major keys — the current dj-buddy convention uses the "A" suffix for major.
    [InlineData("C", "8A")]
    [InlineData("C#", "3A")]
    [InlineData("Db", "3A")]
    [InlineData("D", "10A")]
    [InlineData("D#", "5A")]
    [InlineData("Eb", "5A")]
    [InlineData("E", "12A")]
    [InlineData("F", "7A")]
    [InlineData("F#", "2A")]
    [InlineData("Gb", "2A")]
    [InlineData("G", "9A")]
    [InlineData("G#", "4A")]
    [InlineData("Ab", "4A")]
    [InlineData("A", "11A")]
    [InlineData("A#", "6A")]
    [InlineData("Bb", "6A")]
    [InlineData("B", "1A")]
    // Minor keys — "B" suffix.
    [InlineData("Cm", "5B")]
    [InlineData("C#m", "12B")]
    [InlineData("Dbm", "12B")]
    [InlineData("Dm", "7B")]
    [InlineData("D#m", "2B")]
    [InlineData("Ebm", "2B")]
    [InlineData("Em", "9B")]
    [InlineData("Fm", "4B")]
    [InlineData("F#m", "11B")]
    [InlineData("Gbm", "11B")]
    [InlineData("Gm", "6B")]
    [InlineData("G#m", "1B")]
    [InlineData("Abm", "1B")]
    [InlineData("Am", "8B")]
    [InlineData("A#m", "3B")]
    [InlineData("Bbm", "3B")]
    [InlineData("Bm", "10B")]
    public void Standard_notation_maps_to_expected_camelot(string input, string expected)
    {
        Assert.Equal(expected, KeyConverter.ToCamelotNotation(input));
    }

    [Theory]
    [InlineData("1A")]
    [InlineData("6A")]
    [InlineData("11B")]
    [InlineData("12B")]
    public void Already_camelot_passes_through(string input)
    {
        Assert.Equal(input, KeyConverter.ToCamelotNotation(input));
    }

    [Fact]
    public void Input_is_case_insensitive()
    {
        Assert.Equal("6B", KeyConverter.ToCamelotNotation("gm"));
        Assert.Equal("6B", KeyConverter.ToCamelotNotation("GM"));
    }

    [Fact]
    public void Empty_input_returns_empty_string()
    {
        Assert.Equal("", KeyConverter.ToCamelotNotation(""));
    }

    [Fact]
    public void Whitespace_input_returns_itself()
    {
        // Current behavior: IsNullOrWhiteSpace short-circuits and returns input as-is.
        Assert.Equal("   ", KeyConverter.ToCamelotNotation("   "));
    }

    [Fact]
    public void Unknown_input_returns_itself()
    {
        // Current behavior: anything not in the map is returned unchanged. Locks behavior down;
        // if we ever decide to return "" for unknown, update this test.
        Assert.Equal("H9", KeyConverter.ToCamelotNotation("H9"));
    }
}
