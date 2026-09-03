using System.IO;

namespace VerciWin.Core.Media;

/// <summary>
/// Snapshot of the current media session state, raised via
/// <see cref="MediaSessionWatcher.StateChanged"/>.
/// All fields are value-typed or immutable — callers may cache this struct
/// without worrying about concurrent mutation.
/// </summary>
public sealed record PlaybackState
{
    // ── Track metadata ───────────────────────────────────────────────────────
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;

    /// <summary>
    /// AUMID / AppUserModelId of the source application (e.g. "Spotify.exe").
    /// May be empty for browser-based sources.
    /// </summary>
    public string SourceAppId { get; init; } = string.Empty;

    // ── Album art ────────────────────────────────────────────────────────────
    /// <summary>
    /// Album art as a seekable <see cref="MemoryStream"/> (PNG/JPEG as provided
    /// by the media session). <c>null</c> when GSMTC provides no thumbnail —
    /// callers must handle this gracefully (use <see cref="VerciWin.Core.Color.PaletteExtractor"/>
    /// which returns a neutral palette for null streams).
    /// </summary>
    public Stream? AlbumArtStream { get; init; }

    // ── Playback ─────────────────────────────────────────────────────────────
    public TimeSpan Position { get; init; }
    public TimeSpan EndTime { get; init; }
    public double PlaybackRate { get; init; } = 1.0;
    public bool IsPaused { get; init; } = true;

    /// <summary>
    /// <c>false</c> when the source app does not report timeline properties
    /// (some browser tabs, some apps). The overlay degrades gracefully by
    /// showing title/artist without word-level highlighting.
    /// </summary>
    public bool HasTimeline { get; init; }

    // ── Convenience ──────────────────────────────────────────────────────────
    public bool IsEmpty =>
        string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Artist);

    public static readonly PlaybackState Empty = new();
}