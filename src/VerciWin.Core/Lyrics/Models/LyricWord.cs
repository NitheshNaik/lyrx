namespace VerciWin.Core.Lyrics.Models;

/// <summary>
/// A single word within a lyric line, with its start and end timestamps.
/// <para>
/// When the source is line-level LRC (no inline word tags), words are created
/// by <see cref="WordTimingInterpolator"/> with proportional timing.
/// When the source has A2 word-level tags (&lt;mm:ss.xx&gt;), the timestamps
/// are parsed directly.
/// </para>
/// </summary>
public sealed class LyricWord
{
    public string Text { get; set; } = string.Empty;
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }

    /// <summary>Duration of this word's active window.</summary>
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    public override string ToString() => $"[{Start:mm\\:ss\\.ff}–{End:mm\\:ss\\.ff}] {Text}";
}
