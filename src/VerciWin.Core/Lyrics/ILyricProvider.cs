using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Lyrics;

/// <summary>
/// Abstraction over an external lyric source.
/// Implementations: <see cref="LrcLibProvider"/>.
/// </summary>
public interface ILyricProvider
{
    /// <summary>
    /// Fetches lyrics for the given track.
    /// Returns <c>null</c> if no match was found (not-found is not an exception).
    /// Network or parse errors should propagate as exceptions for <see cref="LyricService"/>
    /// to catch and decide whether to serve stale cache.
    /// </summary>
    Task<LyricDocument?> GetLyricsAsync(
        string title,
        string artist,
        string album,
        TimeSpan duration,
        CancellationToken ct = default);
}
