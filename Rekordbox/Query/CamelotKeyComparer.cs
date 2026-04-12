namespace Rekordbox.Query;

/// <summary>
/// Compares rekordbox Camelot keys (e.g. <c>"8A"</c>, <c>"11B"</c>) by their numeric part first,
/// then by the letter suffix. Unparseable keys sort to the end.
/// </summary>
public sealed class CamelotKeyComparer : IComparer<string>
{
    /// <summary>Shared, thread-safe instance.</summary>
    public static readonly CamelotKeyComparer Instance = new();

    /// <inheritdoc/>
    public int Compare(string? x, string? y)
    {
        Parse(x, out var xNum, out var xLetter);
        Parse(y, out var yNum, out var yLetter);

        var cmp = xNum.CompareTo(yNum);
        return cmp != 0 ? cmp : string.Compare(xLetter, yLetter, StringComparison.Ordinal);
    }

    private static void Parse(string? key, out int num, out string letter)
    {
        if (string.IsNullOrEmpty(key))
        {
            num = int.MaxValue;
            letter = "";
            return;
        }

        var i = 0;
        while (i < key.Length && char.IsDigit(key[i])) i++;

        if (i > 0 && int.TryParse(key.AsSpan(0, i), out num))
        {
            letter = key[i..];
        }
        else
        {
            num = int.MaxValue;
            letter = key;
        }
    }
}
