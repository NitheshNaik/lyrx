using System.Text.RegularExpressions;
using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Lyrics;

/// <summary>
/// Parses LRC-format lyric strings into a <see cref="LyricDocument"/>.
/// <para>
/// Supports two formats:
/// <list type="bullet">
///   <item><b>Line-level LRC</b> — <c>[mm:ss.xx] Line text</c></item>
///   <item><b>A2/Enhanced LRC</b> — inline <c>&lt;mm:ss.xx&gt;word</c> tags
///         interleaved within a line. When word tags are found, <c>IsWordLevel</c>
///         is set on the returned document.</item>
/// </list>
/// Note: as of this writing, LRCLIB returns only line-level <c>syncedLyrics</c>.
/// Word-level support is included for completeness and for sources that do use A2 tags.
/// </para>
/// </summary>
public sealed class LrcParser
{
    // [mm:ss.xx] or [mm:ss.xxx]
    private static readonly Regex LineTagRegex =
        new(@"^\[(\d{1,2}):(\d{2}[.,]\d{2,3})\](.*)", RegexOptions.Compiled);

    // <mm:ss.xx> inline word tag
    private static readonly Regex WordTagRegex =
        new(@"<(\d{1,2}):(\d{2}[.,]\d{2,3})>([^<]*)", RegexOptions.Compiled);

    // Metadata tags like [ar:Artist] — ignored
    private static readonly Regex MetaTagRegex =
        new(@"^\[[a-zA-Z]+:.*\]$", RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────────────────
    // Public entry points
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>syncedLyrics</c> string (timed LRC with optional A2 word tags).
    /// Returns an empty document if the input is null/whitespace.
    /// </summary>
    public LyricDocument Parse(string? lrcContent)
    {
        if (string.IsNullOrWhiteSpace(lrcContent))
            return new LyricDocument();

        var rawLines = lrcContent
            .Split('\n', StringSplitOptions.None)
            .Select(l => l.Trim())
            .ToList();

        var parsed = new List<(TimeSpan stamp, string text)>();

        foreach (var raw in rawLines)
        {
            if (string.IsNullOrEmpty(raw) || MetaTagRegex.IsMatch(raw))
                continue;

            var m = LineTagRegex.Match(raw);
            if (!m.Success) continue;

            var stamp = ParseTimestamp(m.Groups[1].Value, m.Groups[2].Value);
            var text = m.Groups[3].Value.Trim();
            parsed.Add((stamp, text));
        }

        if (parsed.Count == 0)
            return new LyricDocument();

        // Sort by timestamp — most sources are already sorted, but be safe.
        parsed.Sort((a, b) => a.stamp.CompareTo(b.stamp));

        bool hasWordLevel = false;
        var lines = new List<LyricLine>(parsed.Count);

        for (int i = 0; i < parsed.Count; i++)
        {
            var (stamp, text) = parsed[i];

            LyricLine line;
            if (!string.IsNullOrWhiteSpace(text) && text.Contains('<'))
            {
                // Try word-level parsing.
                var words = ParseWordLevel(text, stamp);
                if (words.Count > 0)
                {
                    hasWordLevel = true;
                    line = new LyricLine
                    {
                        Words = words,
                        Start = stamp,
                    };
                }
                else
                {
                    // Contained < but no valid word tags — treat as plain line.
                    line = MakeLineLevelLine(text.Replace("<", "").Replace(">", ""), stamp);
                }
            }
            else
            {
                line = MakeLineLevelLine(text, stamp);
            }

            lines.Add(line);
        }

        // Assign end times from the next line's start time.
        for (int i = 0; i < lines.Count - 1; i++)
        {
            lines[i].End = lines[i + 1].Start;

            // For line-level words (single placeholder word), propagate the end time.
            if (!hasWordLevel && lines[i].Words.Count == 1)
                lines[i].Words[0].End = lines[i].End;
        }

        // Last line has no successor — leave End at zero; callers may set it
        // to the track's EndTime from PlaybackState.
        if (lines.Count > 0 && lines[^1].End == TimeSpan.Zero)
            lines[^1].End = lines[^1].Start + TimeSpan.FromSeconds(10);

        return new LyricDocument { Lines = lines, IsWordLevel = hasWordLevel };
    }

    /// <summary>
    /// Wraps plain (unsynced) lyrics in a <see cref="LyricDocument"/> with no timing.
    /// Each line gets a start of 0:00 — the display will show the text but
    /// cannot highlight individual words. Used as a last-resort fallback.
    /// </summary>
    public LyricDocument ParsePlain(string? plainLyrics)
    {
        if (string.IsNullOrWhiteSpace(plainLyrics))
            return new LyricDocument();

        var lines = plainLyrics
            .Split('\n', StringSplitOptions.None)
            .Select(text =>
            {
                var word = new LyricWord { Text = text.Trim() };
                return new LyricLine
                {
                    Words = string.IsNullOrWhiteSpace(text) ? new() : new() { word },
                    Start = TimeSpan.Zero,
                    End = TimeSpan.Zero,
                };
            })
            .ToList();

        return new LyricDocument { Lines = lines, IsWordLevel = false };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static LyricLine MakeLineLevelLine(string text, TimeSpan stamp)
    {
        // Create a single placeholder word per line.
        // WordTimingInterpolator will split and time the words later.
        var word = new LyricWord
        {
            Text = text,
            Start = stamp,
            End = TimeSpan.Zero, // set from next-line stamp in the caller
        };
        return new LyricLine
        {
            Words = string.IsNullOrWhiteSpace(text)
                ? new List<LyricWord>()   // empty line / instrumental break
                : new List<LyricWord> { word },
            Start = stamp,
            IsInterpolated = false,
        };
    }

    private static List<LyricWord> ParseWordLevel(string lineText, TimeSpan lineStart)
    {
        var words = new List<LyricWord>();
        var matches = WordTagRegex.Matches(lineText);

        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var wordStart = ParseTimestamp(m.Groups[1].Value, m.Groups[2].Value);
            var wordText = m.Groups[3].Value.Trim();

            if (string.IsNullOrWhiteSpace(wordText)) continue;

            // End time = next word's start, or line start + 10s for the last word.
            var wordEnd = i < matches.Count - 1
                ? ParseTimestamp(matches[i + 1].Groups[1].Value, matches[i + 1].Groups[2].Value)
                : wordStart + TimeSpan.FromSeconds(10);

            words.Add(new LyricWord
            {
                Text = wordText,
                Start = wordStart,
                End = wordEnd,
            });
        }

        return words;
    }

    /// <summary>Parses "mm" and "ss.xx" group strings into a TimeSpan.</summary>
    private static TimeSpan ParseTimestamp(string minutesStr, string secondsStr)
    {
        // Normalise comma separator used in some LRC variants.
        secondsStr = secondsStr.Replace(',', '.');

        int minutes = int.Parse(minutesStr);
        double seconds = double.Parse(secondsStr,
            System.Globalization.CultureInfo.InvariantCulture);

        return TimeSpan.FromSeconds(minutes * 60.0 + seconds);
    }
}
