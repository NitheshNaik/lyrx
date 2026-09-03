using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Lyrics.Models;

/// <summary>
/// A single lyric line, containing one or more <see cref="LyricWord"/> objects.
/// <para>
/// Empty lines (instrumental breaks in LRC) have <see cref="Words"/> count == 0
/// and are used to clear the display — they are not skipped during parsing.
/// </para>
/// </summary>
public sealed class LyricLine
{
    public List<LyricWord> Words { get; init; } = new();
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }

    /// <summary>
    /// <c>true</c> when this line's word timing was synthesised by
    /// <see cref="WordTimingInterpolator"/> rather than parsed from A2 tags.
    /// </summary>
    public bool IsInterpolated { get; set; }

    /// <summary>
    /// The full text of the line, reconstructed from its words.
    /// Suitable for display and cache serialisation.
    /// </summary>
    public string Text => string.Join(" ", Words.Select(w => w.Text));

    /// <summary><c>true</c> if this line represents an instrumental/silence break.</summary>
    public bool IsEmpty => Words.Count == 0 || Words.All(w => string.IsNullOrWhiteSpace(w.Text));

    public override string ToString() => $"[{Start:mm\\:ss\\.ff}] {Text}";
}
