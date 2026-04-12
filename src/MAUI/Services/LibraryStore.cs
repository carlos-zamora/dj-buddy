using DJBuddy.Rekordbox.Models;

namespace DJBuddy.MAUI;

/// <summary>
/// Static holder for the currently loaded rekordbox library. Used to share the library
/// across pages without passing it through every navigation parameter.
/// </summary>
public static class LibraryStore
{
    /// <summary>
    /// The currently loaded library, or null if no file has been loaded yet.
    /// </summary>
    public static RekordboxLibrary? Library { get; set; }
}
