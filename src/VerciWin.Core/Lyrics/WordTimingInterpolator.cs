using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Lyrics;

/// <summary>
/// Given a lyric line with only a start and end timestamp (line-level LRC),
/// distributes per-word start/end times proportionally by character length.
/// <para>
/// A 12-character word and a 2-character word should NOT receive equal durations —
/// longer words take proportionally more time to sing. Pure equal division is a
/// common failure mode that produces noticeably wrong highlighting.
/// </para>
/// <para>
/// Algorithm:
/// <list type="number">
///   <item>Tokenise the line on whitespace.</item>
///   <item>Compute the total character count across all tokens.</item>
///   <item>Reserve an inter-word gap of <see cref="InterWordGap"/> between each pair
///         of adjacent words (not after the last word).</item>
///   <item>Distribute the remaining duration proportionally by character count.</item>
///   <item>Accumulate start times sequentially; clamp each word's end to the line end.</item>
/// </list>
/// </para>
/// </summary>
public sealed class WordTimingInterpolator
{
    /// <summary>
    /// Gap inserted between adjacent words.
    /// Default 50 ms — short enough not to feel laggy, long enough to give each
    /// word a visually distinct highlight moment.
    /// </summary>
    public TimeSpan InterWordGap { get; init; } = TimeSpan.FromMilliseconds(50);

    // ─────────────────────────────────────────────────────────────────────────
    // Core interpolation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits <paramref name="lineText"/> into words and assigns proportional
    /// timings within [<paramref name="lineStart"/>, <paramref name="lineEnd"/>].
    /// </summary>
    /// <param name="lineText">
    ///   The raw text of the line (a single LRC timestamp's content).
    ///   Leading/trailing whitespace is ignored; internal runs of whitespace are
    ///   collapsed to single token separators.
    /// </param>
    /// <param name="lineStart">The line's start timestamp.</param>
    /// <param name="lineEnd">The line's end timestamp (= next line's start, usually).</param>
    /// <returns>
    ///   A list of <see cref="LyricWord"/> with computed timings, or an empty list
    ///   if the input is empty/whitespace.
    /// </returns>
    public IReadOnlyList<LyricWord> Interpolate(
        string lineText,
        TimeSpan lineStart,
        TimeSpan lineEnd)
    {
        if (string.IsNullOrWhiteSpace(lineText))
            return Array.Empty<LyricWord>();

        var tokens = lineText.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return Array.Empty<LyricWord>();

        // Single-word line: give the word the full duration.
        if (tokens.Length == 1)
        {
            return new[]
            {
                new LyricWord
                {
                    Text = tokens[0],
                    Start = lineStart,
                    End = lineEnd,
                }
            };
        }

        double lineDurationSecs = (lineEnd - lineStart).TotalSeconds;
        if (lineDurationSecs <= 0)
        {
            // Degenerate case: zero-duration line.
            // Give every word a zero-duration slot at lineStart.
            return tokens.Select(t => new LyricWord
            {
                Text = t,
                Start = lineStart,
                End = lineStart,
            }).ToList();
        }

        // Reserve inter-word gaps.
        double totalGapSecs = InterWordGap.TotalSeconds * (tokens.Length - 1);
        double availableSecs = Math.Max(0, lineDurationSecs - totalGapSecs);

        int totalChars = tokens.Sum(t => t.Length);
        if (totalChars == 0) totalChars = tokens.Length; // fallback: equal division

        var result = new List<LyricWord>(tokens.Length);
        double cursorSecs = lineStart.TotalSeconds;

        for (int i = 0; i < tokens.Length; i++)
        {
            double proportion = (double)tokens[i].Length / totalChars;
            double wordDurSecs = availableSecs * proportion;
            double wordEndSecs = cursorSecs + wordDurSecs;

            // Clamp end to line end.
            wordEndSecs = Math.Min(wordEndSecs, lineEnd.TotalSeconds);

            result.Add(new LyricWord
            {
                Text = tokens[i],
                Start = TimeSpan.FromSeconds(cursorSecs),
                End = TimeSpan.FromSeconds(wordEndSecs),
            });

            // Advance cursor: word duration + gap (no gap after the last word).
            cursorSecs = wordEndSecs + (i < tokens.Length - 1 ? InterWordGap.TotalSeconds : 0);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Document-level helper (processes the whole document in one pass)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies interpolation to every line in <paramref name="document"/> that
    /// has not already received word-level timing (i.e. lines where the parser
    /// left a single placeholder word).
    /// Mutates the document's lines in-place; also invalidates the word cache.
    /// </summary>
    public void InterpolateDocument(Models.LyricDocument document)
    {
        if (document.IsWordLevel) return; // Already word-level — nothing to do.

        foreach (var line in document.Lines)
        {
            if (line.IsEmpty) continue;

            // Each line-level line has exactly one placeholder word whose Text
            // is the full line text. Replace it with interpolated words.
            string lineText = line.Words.Count == 1
                ? line.Words[0].Text
                : string.Join(" ", line.Words.Select(w => w.Text));

            var interpolated = Interpolate(lineText, line.Start, line.End);

            line.Words.Clear();
            line.Words.AddRange(interpolated);
            line.IsInterpolated = true;
        }

        document.InvalidateWordCache();
    }
}
