namespace DJBuddy.Rekordbox.Xml;

/// <summary>
/// Converts between different musical key notations (standard to Camelot wheel).
/// </summary>
public static class KeyConverter
{
    /// <summary>
    /// Converts a standard notation key (e.g., <c>"Gm"</c>, <c>"F#"</c>, <c>"Cm"</c>) to Camelot
    /// notation (e.g., <c>"6B"</c>, <c>"2A"</c>, <c>"4B"</c>).
    /// </summary>
    /// <param name="key">The key in standard notation, or already in Camelot notation.</param>
    /// <returns>The key in Camelot notation, or the original key if not recognized as standard notation.</returns>
    public static string ToCamelotNotation(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return key;

        key = key.Trim();

        // If it already looks like Camelot notation (number + letter), return as-is
        if (IsCamelotNotation(key))
            return key;

        return CamelotMap.TryGetValue(key, out var camelot) ? camelot : key;
    }

    /// <summary>
    /// Checks if a key is already in Camelot notation (digit(s) + letter A/B).
    /// </summary>
    private static bool IsCamelotNotation(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 2)
            return false;

        var lastChar = char.ToUpper(key[^1]);
        if (lastChar != 'A' && lastChar != 'B')
            return false;

        var numberPart = key[..^1];
        return int.TryParse(numberPart, out var num) && num >= 1 && num <= 12;
    }

    private static readonly Dictionary<string, string> CamelotMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Major keys
        { "C", "8A" },
        { "C#", "3A" },
        { "Db", "3A" },
        { "D", "10A" },
        { "D#", "5A" },
        { "Eb", "5A" },
        { "E", "12A" },
        { "F", "7A" },
        { "F#", "2A" },
        { "Gb", "2A" },
        { "G", "9A" },
        { "G#", "4A" },
        { "Ab", "4A" },
        { "A", "11A" },
        { "A#", "6A" },
        { "Bb", "6A" },
        { "B", "1A" },

        // Minor keys
        { "Cm", "5B" },
        { "C#m", "12B" },
        { "Dbm", "12B" },
        { "Dm", "7B" },
        { "D#m", "2B" },
        { "Ebm", "2B" },
        { "Em", "9B" },
        { "Fm", "4B" },
        { "F#m", "11B" },
        { "Gbm", "11B" },
        { "Gm", "6B" },
        { "G#m", "1B" },
        { "Abm", "1B" },
        { "Am", "8B" },
        { "A#m", "3B" },
        { "Bbm", "3B" },
        { "Bm", "10B" },
    };
}
