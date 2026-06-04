using DJBuddy.Rekordbox.Models;
using NAudio.Wave;

namespace DJBuddy.Agent;

/// <summary>Wraps NAudio to provide simple play/pause/seek for track previews.</summary>
internal sealed class MediaPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private bool _disposed;

    /// <summary>The track currently loaded (playing or paused), or <c>null</c> if nothing is loaded.</summary>
    public Track? CurrentTrack { get; private set; }

    /// <summary>Whether audio is actively playing (not paused or stopped).</summary>
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>Current playback position.</summary>
    public TimeSpan CurrentTime => _reader?.CurrentTime ?? TimeSpan.Zero;

    /// <summary>Total duration of the loaded track.</summary>
    public TimeSpan TotalTime => _reader?.TotalTime ?? TimeSpan.Zero;

    /// <summary>
    /// Loads a file for playback. Stops and disposes any previously loaded file first.
    /// Does not start playback — call <see cref="Play"/> after loading.
    /// </summary>
    /// <exception cref="Exception">
    /// Propagates <see cref="FileNotFoundException"/> or NAudio format errors to the caller.
    /// </exception>
    public void Load(string localPath, Track track)
    {
        // Stop and release the old device before closing the old stream.
        _output?.Stop();
        _output?.Dispose();
        _output = null;

        _reader?.Dispose();
        _reader = null;

        _reader = new AudioFileReader(localPath);
        _output = new WaveOutEvent();
        _output.Init(_reader);
        CurrentTrack = track;
    }

    /// <summary>Starts playback. No-op if nothing is loaded.</summary>
    public void Play()
    {
        if (_output is null) return;
        _output.Play();
    }

    /// <summary>Toggles between playing and paused. No-op if stopped or nothing is loaded.</summary>
    public void Pause()
    {
        if (_output is null) return;
        if (_output.PlaybackState == PlaybackState.Playing)
            _output.Pause();
        else if (_output.PlaybackState == PlaybackState.Paused)
            _output.Play();
    }

    /// <summary>Stops playback and rewinds to the beginning.</summary>
    public void Stop()
    {
        if (_output is null) return;
        _output.Stop();
        if (_reader is not null)
            _reader.CurrentTime = TimeSpan.Zero;
    }

    /// <summary>Seeks to an absolute position, clamped to the valid range.</summary>
    public void Seek(TimeSpan position)
    {
        if (_reader is null) return;
        var clamped = TimeSpan.FromSeconds(
            Math.Clamp(position.TotalSeconds, 0, _reader.TotalTime.TotalSeconds));
        _reader.CurrentTime = clamped;
    }

    /// <summary>Seeks relative to the current position by <paramref name="seconds"/> (negative = rewind).</summary>
    public void SkipSeconds(int seconds) => Seek(CurrentTime + TimeSpan.FromSeconds(seconds));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
        _disposed = true;
    }
}
