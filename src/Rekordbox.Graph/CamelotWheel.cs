namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// Arithmetic helpers for the 12-position Camelot wheel. Each Camelot key is a digit 1–12
/// followed by a letter (<c>A</c> or <c>B</c>); the inner/outer ring distinction is captured
/// by the letter and the step position by the digit. Adjacency rules for DJ harmonic mixing:
/// same key, ±1 on the wheel (same letter), or a swap between letters at the same number
/// (relative major/minor).
/// </summary>
internal static class CamelotWheel
{
    /// <summary>
    /// Tries to parse a Camelot key string (e.g. <c>"6A"</c>, <c>"12B"</c>) into its numeric
    /// position (1–12) and letter (<c>A</c>/<c>B</c>).
    /// </summary>
    /// <param name="key">Raw key string; whitespace is trimmed and the letter is uppercased.</param>
    /// <param name="number">Parsed 1–12 wheel position on success.</param>
    /// <param name="letter">Parsed <c>A</c> or <c>B</c> ring indicator on success.</param>
    /// <returns><c>true</c> if the string is a valid Camelot key; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? key, out int number, out char letter)
    {
        number = 0;
        letter = '\0';
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var trimmed = key.Trim();
        if (trimmed.Length < 2)
            return false;

        var last = char.ToUpperInvariant(trimmed[^1]);
        if (last != 'A' && last != 'B')
            return false;

        if (!int.TryParse(trimmed[..^1], out var num) || num < 1 || num > 12)
            return false;

        number = num;
        letter = last;
        return true;
    }

    /// <summary>
    /// Classifies the harmonic relationship going from <paramref name="source"/> to
    /// <paramref name="target"/>. Returns <c>null</c> when the keys are unparseable or not
    /// harmonically related under the Same / Adjacent / EnergyBoost / EnergyDrop rules.
    /// </summary>
    /// <param name="source">Source Camelot key.</param>
    /// <param name="target">Target Camelot key.</param>
    /// <param name="allowRelativeMajorMinor">
    /// When true (the default in <see cref="TrackGraphOptions"/>), a same-number different-letter
    /// pair (e.g. <c>6A</c> ↔ <c>6B</c>) counts as <see cref="HarmonicRelation.Adjacent"/>.
    /// </param>
    public static HarmonicRelation? Classify(string? source, string? target, bool allowRelativeMajorMinor)
    {
        if (!TryParse(source, out var sNum, out var sLetter) ||
            !TryParse(target, out var tNum, out var tLetter))
        {
            return null;
        }

        if (sNum == tNum && sLetter == tLetter)
            return HarmonicRelation.Same;

        // Relative major/minor: same number, different letter.
        if (allowRelativeMajorMinor && sNum == tNum && sLetter != tLetter)
            return HarmonicRelation.Adjacent;

        // Energy shifts only make sense within the same ring (same letter).
        if (sLetter != tLetter)
            return null;

        var signedDelta = WheelSignedDelta(sNum, tNum);
        return signedDelta switch
        {
            1 or -1 => HarmonicRelation.Adjacent,
            2 => HarmonicRelation.EnergyBoost,
            -2 => HarmonicRelation.EnergyDrop,
            _ => null,
        };
    }

    /// <summary>
    /// Shortest signed delta from <paramref name="from"/> to <paramref name="to"/> on the
    /// 12-position wheel, in the range [-6, 6]. Positive values go "up" (boost direction),
    /// negative "down" (drop direction). Ties at ±6 resolve to +6.
    /// </summary>
    private static int WheelSignedDelta(int from, int to)
    {
        var raw = (to - from) % 12;
        if (raw > 6) raw -= 12;
        else if (raw < -6) raw += 12;
        return raw;
    }
}
