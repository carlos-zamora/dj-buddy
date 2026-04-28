using DJBuddy.Rekordbox.Models;

namespace DJBuddy.Rekordbox.Query;

/// <summary>
/// How <see cref="TrackSet.Apply(TrackSetMode, IEnumerable{string})"/> folds an incoming
/// sequence of track IDs into an existing <see cref="TrackSet"/>.
/// </summary>
public enum TrackSetMode
{
    /// <summary>Discard the current membership and use the incoming IDs.</summary>
    Replace,

    /// <summary>Union: keep existing members, add any new ones from the incoming IDs.</summary>
    Union,

    /// <summary>Intersect: keep only existing members that also appear in the incoming IDs.</summary>
    Intersect,

    /// <summary>Except: remove existing members that appear in the incoming IDs.</summary>
    Except,
}

/// <summary>
/// A lightweight, named subset of tracks drawn from a parent <see cref="RekordboxLibrary"/>.
/// Stores track IDs only — full <see cref="Track"/> objects are resolved lazily via the
/// parent library, so subsets are cheap to create, combine, and discard.
/// </summary>
/// <remarks>
/// A <see cref="TrackSet"/> has two shapes:
/// <list type="bullet">
/// <item><description>Unordered — <see cref="OrderedIds"/> is <c>null</c> and <see cref="Ids"/> enumerates
/// the hash set in arbitrary order. Suitable for playlists and staging filtered subsets.</description></item>
/// <item><description>Ordered — <see cref="OrderedIds"/> holds the canonical sequence; enumerating
/// <see cref="Ids"/> yields tracks in that order. Produced by <see cref="SetOrder"/> after ordering
/// algorithms run. Any mutation clears the order.</description></item>
/// </list>
/// </remarks>
public sealed class TrackSet
{
    private readonly HashSet<string> _ids;
    private List<string>? _orderedIds;

    /// <summary>Display name of this workspace / subset.</summary>
    public string Name { get; set; }

    /// <summary>The parent library. Set operations between two <see cref="TrackSet"/>s require
    /// both operands to share the same library reference.</summary>
    public RekordboxLibrary Library { get; }

    /// <summary>
    /// Creates a new <see cref="TrackSet"/> optionally seeded with a starting set of IDs.
    /// IDs that do not exist in <paramref name="library"/> are kept — resolution to
    /// <see cref="Track"/> objects silently skips missing ones, matching the rest of the library.
    /// </summary>
    /// <param name="name">Display name (non-empty recommended, not enforced here — the
    /// workspace store is responsible for naming rules).</param>
    /// <param name="library">Parent library; must not be null.</param>
    /// <param name="initialIds">Optional initial track IDs; duplicates are collapsed.</param>
    public TrackSet(string name, RekordboxLibrary library, IEnumerable<string>? initialIds = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(library);

        Name = name;
        Library = library;
        _ids = initialIds is null ? [] : [.. initialIds];
    }

    /// <summary>Number of distinct track IDs in the set.</summary>
    public int Count => _ids.Count;

    /// <summary>True when <see cref="SetOrder"/> has been called and no subsequent mutation cleared it.</summary>
    public bool Ordered => _orderedIds is not null;

    /// <summary>
    /// The canonical ordered sequence, or <c>null</c> when the set is unordered. The returned
    /// list is the internal storage — do not mutate it in place.
    /// </summary>
    public IReadOnlyList<string>? OrderedIds => _orderedIds;

    /// <summary>All track IDs in the set. Ordered sets yield <see cref="OrderedIds"/>; unordered sets
    /// yield the underlying hash set in unspecified order.</summary>
    public IEnumerable<string> Ids => _orderedIds ?? (IEnumerable<string>)_ids;

    /// <summary>
    /// Resolves <see cref="Ids"/> against <see cref="Library"/>. Track IDs not in the library
    /// are silently skipped — mirrors the behavior of <see cref="LibraryExtensions.GetTracks"/>.
    /// </summary>
    public IEnumerable<Track> Tracks
    {
        get
        {
            foreach (var id in Ids)
            {
                if (Library.Tracks.TryGetValue(id, out var t))
                    yield return t;
            }
        }
    }

    /// <summary>True when <paramref name="trackId"/> is a member of this set.</summary>
    public bool Contains(string trackId) => _ids.Contains(trackId);

    /// <summary>
    /// Folds <paramref name="ids"/> into this set under the given <paramref name="mode"/>.
    /// Any mutation clears <see cref="OrderedIds"/> — a subsequent ordering pass must re-run.
    /// </summary>
    /// <returns>The number of tracks in the set after the operation.</returns>
    public int Apply(TrackSetMode mode, IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var incoming = ids as ICollection<string> ?? [.. ids];

        switch (mode)
        {
            case TrackSetMode.Replace:
                _ids.Clear();
                foreach (var id in incoming) _ids.Add(id);
                break;
            case TrackSetMode.Union:
                foreach (var id in incoming) _ids.Add(id);
                break;
            case TrackSetMode.Intersect:
                _ids.IntersectWith(incoming);
                break;
            case TrackSetMode.Except:
                _ids.ExceptWith(incoming);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode.");
        }

        _orderedIds = null;
        return _ids.Count;
    }

    /// <summary>
    /// Replaces membership with <paramref name="orderedIds"/> and records them as the canonical
    /// order. Duplicates are dropped (first occurrence wins) so the resulting hash set stays
    /// consistent with the ordered list.
    /// </summary>
    public void SetOrder(IEnumerable<string> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);

        var seen = new HashSet<string>();
        var ordered = new List<string>();
        foreach (var id in orderedIds)
        {
            if (seen.Add(id))
                ordered.Add(id);
        }

        _ids.Clear();
        foreach (var id in ordered) _ids.Add(id);
        _orderedIds = ordered;
    }

    /// <summary>Removes ordering without touching membership.</summary>
    public void ClearOrder() => _orderedIds = null;

    /// <summary>
    /// Returns a new unordered <see cref="TrackSet"/> containing the union of <c>this</c> and
    /// <paramref name="other"/>. Both sets must share the same <see cref="Library"/>.
    /// </summary>
    public TrackSet Union(TrackSet other, string newName)
    {
        EnsureSameLibrary(other);
        var result = new TrackSet(newName, Library, _ids);
        result._ids.UnionWith(other._ids);
        return result;
    }

    /// <summary>
    /// Returns a new unordered <see cref="TrackSet"/> containing the intersection of <c>this</c>
    /// and <paramref name="other"/>. Both sets must share the same <see cref="Library"/>.
    /// </summary>
    public TrackSet Intersect(TrackSet other, string newName)
    {
        EnsureSameLibrary(other);
        var result = new TrackSet(newName, Library, _ids);
        result._ids.IntersectWith(other._ids);
        return result;
    }

    /// <summary>
    /// Returns a new unordered <see cref="TrackSet"/> containing the members of <c>this</c> that
    /// are not in <paramref name="other"/>. Both sets must share the same <see cref="Library"/>.
    /// </summary>
    public TrackSet Except(TrackSet other, string newName)
    {
        EnsureSameLibrary(other);
        var result = new TrackSet(newName, Library, _ids);
        result._ids.ExceptWith(other._ids);
        return result;
    }

    private void EnsureSameLibrary(TrackSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!ReferenceEquals(Library, other.Library))
            throw new InvalidOperationException("Set operations require both TrackSets to share the same Library.");
    }
}
