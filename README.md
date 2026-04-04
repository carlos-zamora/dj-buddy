# DJ Buddy

A rekordbox.xml browser built with .NET MAUI. Load a rekordbox XML export and navigate your playlist/folder tree with sorting, search, key filtering, and harmonic mixing highlights.

## Features

- **Library browser** — Navigate the full playlist and folder tree from your rekordbox XML export
- **Search & filter** — Filter tracks by title, artist, or musical key (Camelot notation)
- **Sorting** — Sort tracks by title, BPM, or key
- **Harmonic mixing** — Select a track to highlight compatible keys using the Camelot wheel:
  - Same key (green)
  - Adjacent keys (blue) — smooth transitions
  - Energy boost keys (amber) — raise the energy
  - Energy drop keys (purple) — wind down
- **Favorites & doubles** — Swipe right on a track (or right-click on desktop) to add it to Favorites or a custom "doubles" playlist. Playlists are saved locally and persist across sessions
- **Export to rekordbox** — Export your DJ Buddy playlists back into rekordbox.xml as a `DJ_BUDDY` folder. A backup is created automatically before writing
- **Auto-reload** — Remembers the last loaded file and reloads it on startup
- **Cross-platform** — Targets Windows, Android, iOS, and macOS via .NET MAUI

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) with the MAUI workload installed

## Build & Run

```bash
# Windows (primary dev target)
dotnet build -f net10.0-windows10.0.19041.0
dotnet run -f net10.0-windows10.0.19041.0

# Android
dotnet build -f net10.0-android
```

## Usage

1. Launch the app
2. Click the button to select your `rekordbox.xml` export file
3. Browse playlists and folders from the tree view
4. Inside a playlist, tap a track to see harmonic key compatibility highlights
5. Use the search bar, key filter, and column headers to find the right next track
6. Swipe right on a track to add it to Favorites or a doubles playlist
7. Click **Export** to write your DJ Buddy playlists into the rekordbox.xml

## Project Structure

```
├── Models/
│   ├── Track.cs              # Track data (name, artist, BPM, key)
│   ├── PlaylistNode.cs       # Playlist/folder tree node
│   └── RekordboxLibrary.cs   # Parsed library (tracks + playlist tree)
├── Services/
│   ├── RekordboxParser.cs       # Streaming XML parser for rekordbox.xml
│   ├── RekordboxExporter.cs     # Exports DJ_BUDDY playlists into rekordbox.xml
│   ├── DjBuddyPlaylistStore.cs  # Static store for favorites & doubles (JSON persistence)
│   └── LibraryStore.cs          # Static singleton holding the current library
├── Pages/
│   └── PlaylistPage.xaml.cs  # Playlist view with sorting, search, key highlights
├── MainPage.xaml.cs          # Welcome screen + top-level library view
└── dj-buddy.csproj
```
