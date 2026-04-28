using System.Text.RegularExpressions;
using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Rekordbox.Query;

/// <summary>
/// Splits artist credits into individual artist tokens for aggregation. Rekordbox stores
/// collaborations as a single string ("Zeds Dead, Subtronics" or "Skrillex feat. Sirah"),
/// so a naive <c>GroupBy(t =&gt; t.Artist)</c> miscounts collabs as their own unique artists.
/// This helper normalizes those strings, plus pulls flipper / remixer attributions out of the
/// track <see cref="Track.Name"/> when they appear in the conventional bracketed form.
/// </summary>
/// <remarks>
/// <para>
/// Splits intentionally exclude <c>&amp;</c> because many duos use it as part of their canonical
/// name (Above &amp; Beyond, Simon &amp; Garfunkel) — splitting would corrupt the count more often
/// than it would help.
/// </para>
/// <para>
/// All comparisons are <see cref="StringComparer.OrdinalIgnoreCase"/>; tokens are deduped
/// per call so <c>"X, x"</c> collapses to a single contributor.
/// </para>
/// </remarks>
public static class ArtistSplitter
{
    private static readonly Regex MultiCharDelimiters = new(
        @"\s+(?:feat\.?|featuring|ft\.?|x|vs\.?)\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TitleAttributionPattern = new(
        @"[\[\(]\s*([^\[\]\(\)]+?)\s+(?:Flip|Remix|Bootleg|Edit|VIP|Mashup|Rework)\s*[\]\)]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Splits an artist credit string into individual artist tokens.
    /// </summary>
    /// <param name="artist">Raw artist string from <see cref="Track.Artist"/>.</param>
    /// <returns>Distinct, trimmed artist tokens. Empty input yields an empty sequence.</returns>
    public static IEnumerable<string> SplitArtist(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Normalize multi-char delimiters to commas first, then split once on commas — keeps
        // the hot path simple and avoids repeated regex passes.
        var normalized = MultiCharDelimiters.Replace(artist, ",");
        foreach (var raw in normalized.Split(','))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            if (seen.Add(token))
                yield return token;
        }
    }

    /// <summary>
    /// Pulls bracketed attribution tokens out of a track name (e.g. <c>"[NEOTEK Flip]"</c> ⇒
    /// <c>NEOTEK</c>). Each captured attribution is passed through <see cref="SplitArtist"/>,
    /// so multi-name credits like <c>"[Subtronics x Excision Flip]"</c> yield both names.
    /// </summary>
    /// <param name="name">Track <see cref="Track.Name"/>.</param>
    /// <returns>Distinct attribution tokens; empty when no recognized pattern is present.</returns>
    public static IEnumerable<string> ExtractTitleAttributions(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in TitleAttributionPattern.Matches(name))
        {
            foreach (var token in SplitArtist(m.Groups[1].Value))
            {
                if (seen.Add(token))
                    yield return token;
            }
        }
    }

    /// <summary>
    /// Returns every distinct contributor to a track: the split <see cref="Track.Artist"/>
    /// tokens unioned with any attributions parsed from <see cref="Track.Name"/>.
    /// </summary>
    /// <param name="track">Track to analyze.</param>
    /// <returns>Distinct contributor tokens (case-insensitive).</returns>
    public static IEnumerable<string> EnumerateContributors(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in SplitArtist(track.Artist))
        {
            if (seen.Add(token))
                yield return token;
        }
        foreach (var token in ExtractTitleAttributions(track.Name))
        {
            if (seen.Add(token))
                yield return token;
        }
    }
}
