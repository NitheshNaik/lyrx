using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Lyrics.Models;

/// <summary>
/// The full parsed lyric document for a track.
/// </summary>
public sealed class LyricDocument
{
    public List<LyricLine> Lines { get; init; } = new();

    /// <summary>
    /// <c>true</c> when the source contained inline A2 word-level timestamps.
    /// <c>false</c> when timing was interpolated from line-level LRC.
    /// </summary>
    public bool IsWordLevel { get; init; }

    // ── Flat sorted word list for O(log n) binary search in the renderer ────

    private IReadOnlyList<LyricWord>? _allWordsSorted;

    /// <summary>
    /// All words across all lines, sorted by <see cref="LyricWord.Start"/>.
    /// Computed once on first access, then cached.
    /// Invalidated if <see cref="Lines"/> is modified after construction
    /// (call <see cref="InvalidateWordCache"/> in that case).
    /// </summary>
    public IReadOnlyList<LyricWord> AllWordsSorted =>
        _allWordsSorted ??= Lines
            .Where(l => !l.IsEmpty)
            .SelectMany(l => l.Words)
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderBy(w => w.Start)
            .ToList();

    public void InvalidateWordCache() => _allWordsSorted = null;

    /// <summary>Total song duration as reported by the last line's end time.</summary>
    public TimeSpan TotalDuration =>
        Lines.Count > 0 ? Lines[^1].End : TimeSpan.Zero;
}
