# DJ Buddy

A rekordbox.xml browser built with .NET MAUI, plus an AI-powered DJ assistant console app using the GitHub Copilot SDK. Load a rekordbox XML export and navigate your playlist/folder tree with sorting, search, key filtering, and harmonic mixing highlights — or chat with DJ Buddy to query your library with natural language.

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
- **AI assistant** — Console-based DJ Buddy agent (GitHub Copilot SDK) answers natural language queries about your library with multi-turn contextual conversation, colored output with emoji formatting, a thinking spinner, and REPL commands (`/help`, `/load`, `/stats`, `/clear`)
- **Graph analysis** — Optional `Rekordbox.Graph` library builds a QuikGraph `BidirectionalGraph` of your library with directed harmonic-compatibility edges (same key, adjacent, energy boost/drop, with half/double-time BPM matching) and playlist co-occurrence edges — ready for shortest-path, clustering, and other graph algorithms

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) with the MAUI workload installed

## Build & Run

```bash
# Windows (primary dev target)
dotnet build -f net10.0-windows10.0.19041.0
dotnet run -f net10.0-windows10.0.19041.0

# Android
dotnet build -f net10.0-android

# Agent console app (auto-loads default rekordbox.xml, or specify a path)
dotnet run --project src/Agent/Agent.csproj
dotnet run --project src/Agent/Agent.csproj -- path/to/rekordbox.xml

# Shared library only
dotnet build Rekordbox/Rekordbox.csproj

# Tests
dotnet test Rekordbox.Tests/Rekordbox.Tests.csproj
```

## Usage

1. Launch the app
2. Click the button to select your `rekordbox.xml` export file
3. Browse playlists and folders from the tree view
4. Inside a playlist, tap a track to see harmonic key compatibility highlights
5. Use the search bar, key filter, and column headers to find the right next track
6. Swipe right on a track to add it to Favorites or a doubles playlist
7. Click **Export** to write your DJ Buddy playlists into the rekordbox.xml

### Agent

Run the DJ Buddy agent for a conversational AI experience in the terminal:

```bash
dotnet run --project src/Agent/Agent.csproj
```

On startup the agent loads your rekordbox library (from `%MUSIC%/rekordbox/rekordbox.xml` by default, or pass a path as a CLI argument). If the default file isn't found, you'll be prompted to enter a path.

**REPL commands:**

| Command | Description |
|---------|-------------|
| `/help` | Show available commands |
| `/load <path>` | Load a different rekordbox.xml (resets conversation) |
| `/stats` | Show library statistics (genres, artists, keys, BPM range) |
| `/clear` | Clear the screen |
| `/exit` | Exit the agent |

Type any question to chat with DJ Buddy — it remembers conversation context, so you can refer back to previous results ("tell me more about the first one", "find tracks that mix with those").

## Project Structure

```
├── Agent/                         # Console-based AI assistant (GitHub Copilot SDK)
│   ├── Program.cs                 # Entry point, REPL loop, Copilot session wiring
│   ├── SystemPrompt.cs            # DJ Buddy agent persona and tool-use guidelines
│   ├── ConsoleUi.cs               # Formatted output (banner, help, stats, prompts)
│   ├── Spinner.cs                 # Async VT-powered thinking indicator
│   ├── Vt.cs                      # Virtual Terminal escape sequence constants
│   └── Tools/
│       └── LibraryTools.cs        # 5 query tools (search, details, playlists, stats)
├── Rekordbox/                     # Shared .NET library (net10.0, no MAUI dependency)
│   ├── Models/
│   │   ├── Track.cs               # Full-fidelity track model (~25 attributes + cues + beatgrid)
│   │   ├── PlaylistNode.cs        # Playlist/folder tree node
│   │   ├── RekordboxLibrary.cs    # Parsed library (tracks + playlist tree)
│   │   ├── CuePoint.cs            # Hot cue / memory cue / loop from POSITION_MARK
│   │   └── TempoMark.cs           # Beatgrid entry from TEMPO
│   ├── Xml/
│   │   ├── RekordboxParser.cs     # Streaming XML parser for rekordbox.xml
│   │   ├── RekordboxExporter.cs   # Patch or full-write rekordbox.xml
│   │   ├── KeyConverter.cs        # Tonality -> Camelot wheel notation
│   │   └── ParseOptions.cs        # Toggle tempo/cue/unknown-attr parsing
│   └── Query/
│       ├── TrackQuery.cs          # Search, WhereKey, WhereBpmBetween, OrderBy extensions
│       ├── LibraryExtensions.cs   # GetTracks, EnumeratePlaylists, FindByName
│       ├── TrackSearchFields.cs   # [Flags] enum for search field selection
│       ├── TrackSortKey.cs        # Sort key enum
│       └── CamelotKeyComparer.cs  # Numeric Camelot key ordering
├── Rekordbox.Graph/               # Optional graph layer (net10.0, depends on QuikGraph)
│   ├── TrackEdge.cs               # Abstract directed IEdge<Track> base
│   ├── CompatibilityEdge.cs       # Harmonic/BPM edge (Relation, Tier, IsHalfTimeMatch)
│   ├── CoOccurrenceEdge.cs        # Playlist co-occurrence edge (PlaylistCount)
│   ├── HarmonicRelation.cs        # Same / Adjacent / EnergyBoost / EnergyDrop
│   ├── BpmTier.cs                 # Same / Close / Medium / Far buckets
│   ├── CamelotWheel.cs            # Key parsing + wheel arithmetic
│   ├── TrackGraphOptions.cs       # Tier thresholds, include flags, weight knobs
│   ├── TrackQualityWeight.cs      # Rating + freshness weight contribution
│   └── TrackGraphBuilder.cs       # Build BidirectionalGraph<Track, TrackEdge>
├── Rekordbox.Tests/               # xUnit v3 tests for the shared library
│   ├── Fixtures/                  # Embedded XML fixtures (minimal.xml, round_trip.xml)
│   └── Xml/ Query/                # Test classes mirroring library structure
├── Services/
│   ├── DjBuddyPlaylistStore.cs    # Static store for favorites & doubles (JSON persistence)
│   └── LibraryStore.cs            # Static singleton holding the current library
├── Pages/
│   └── PlaylistPage.xaml.cs       # Playlist view with sorting, search, key highlights
├── MainPage.xaml.cs               # Welcome screen + top-level library view
└── dj-buddy.csproj               # MAUI app, references Rekordbox.csproj
```
