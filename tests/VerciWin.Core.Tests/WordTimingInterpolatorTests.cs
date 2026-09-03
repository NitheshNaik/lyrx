using VerciWin.Core.Lyrics;
using Xunit;

namespace VerciWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="WordTimingInterpolator"/>.
/// These are pure-function tests — no I/O, no GSMTC, no WinUI.
/// </summary>
public sealed class WordTimingInterpolatorTests
{
    private static WordTimingInterpolator MakeInterpolator(int gapMs = 50) =>
        new() { InterWordGap = TimeSpan.FromMilliseconds(gapMs) };

    // ── Proportional distribution ─────────────────────────────────────────────

    [Fact]
    public void LongWordGetsMoreTimeThanShortWord()
    {
        var sut = MakeInterpolator(gapMs: 0); // zero gap to isolate proportion logic
        var lineStart = TimeSpan.Zero;
        var lineEnd = TimeSpan.FromSeconds(14); // 14s — 12+2 chars → 12s + 2s

        // "Hello" = 5 chars, "Pneumonoultramicroscopicsilicovolcanoconiosis" = 45 chars
        var words = sut.Interpolate("Hello Pneumonoultramicroscopicsilicovolcanoconiosis",
            lineStart, lineEnd);

        Assert.Equal(2, words.Count);
        double shortDur = words[0].Duration.TotalSeconds;
        double longDur = words[1].Duration.TotalSeconds;

        // 45/5 = 9× longer; allow ±0.5s tolerance for floating-point
        Assert.True(longDur > shortDur * 5,
            $"Long word ({longDur:F2}s) should be >> short word ({shortDur:F2}s)");
    }

    [Fact]
    public void ProportionalDurationsAreWeightedByCharacterCount()
    {
        // "AB" = 2 chars, "ABCDEF" = 6 chars → ratio 1:3
        var sut = MakeInterpolator(gapMs: 0);
        var words = sut.Interpolate("AB ABCDEF",
            TimeSpan.Zero, TimeSpan.FromSeconds(8));

        Assert.Equal(2, words.Count);
        double dur0 = words[0].Duration.TotalSeconds;
        double dur1 = words[1].Duration.TotalSeconds;

        // totalChars = 8 → word0 gets 2/8=25% of 8s = 2s, word1 gets 6/8=75% = 6s
        Assert.InRange(dur0, 1.9, 2.1);
        Assert.InRange(dur1, 5.9, 6.1);
    }

    // ── Inter-word gaps ───────────────────────────────────────────────────────

    [Fact]
    public void GapsLeaveNoOverlapBetweenAdjacentWords()
    {
        var sut = MakeInterpolator(gapMs: 50);
        var words = sut.Interpolate("one two three",
            TimeSpan.Zero, TimeSpan.FromSeconds(9));

        Assert.Equal(3, words.Count);
        for (int i = 0; i < words.Count - 1; i++)
        {
            Assert.True(words[i + 1].Start >= words[i].End,
                $"Word {i} end ({words[i].End}) overlaps word {i + 1} start ({words[i + 1].Start})");
        }
    }

    [Fact]
    public void TotalTimeConsumedDoesNotExceedLineDuration()
    {
        var sut = MakeInterpolator(gapMs: 50);
        var lineEnd = TimeSpan.FromSeconds(10);
        var words = sut.Interpolate("the quick brown fox jumps",
            TimeSpan.Zero, lineEnd);

        Assert.True(words[^1].End <= lineEnd,
            $"Last word ends at {words[^1].End} which exceeds line end {lineEnd}");
    }

    [Fact]
    public void GapNotAppliedAfterLastWord()
    {
        var sut = MakeInterpolator(gapMs: 500); // large gap to make it obvious
        var lineEnd = TimeSpan.FromSeconds(5);
        var words = sut.Interpolate("hello world",
            TimeSpan.Zero, lineEnd);

        // Last word should end AT or BEFORE lineEnd, not lineEnd - gap
        Assert.True(words[^1].End <= lineEnd,
            $"Last word end {words[^1].End} exceeds line end {lineEnd}");
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void SingleWordGetFullLineDuration()
    {
        var sut = MakeInterpolator();
        var lineStart = TimeSpan.FromSeconds(5);
        var lineEnd = TimeSpan.FromSeconds(10);

        var words = sut.Interpolate("supercalifragilistic", lineStart, lineEnd);

        Assert.Single(words);
        Assert.Equal(lineStart, words[0].Start);
        Assert.Equal(lineEnd, words[0].End);
    }

    [Fact]
    public void EmptyStringReturnsEmptyList()
    {
        var sut = MakeInterpolator();
        var words = sut.Interpolate("", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.Empty(words);
    }

    [Fact]
    public void WhitespaceOnlyReturnsEmptyList()
    {
        var sut = MakeInterpolator();
        var words = sut.Interpolate("   \t  ", TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.Empty(words);
    }

    [Fact]
    public void ZeroDurationLineReturnsWordsAtLineStart()
    {
        var sut = MakeInterpolator();
        var stamp = TimeSpan.FromSeconds(30);

        var words = sut.Interpolate("hello world", stamp, stamp);

        Assert.Equal(2, words.Count);
        Assert.All(words, w => Assert.Equal(stamp, w.Start));
    }

    [Fact]
    public void StartTimesAreStrictlyIncreasing()
    {
        var sut = MakeInterpolator(gapMs: 50);
        var words = sut.Interpolate("a bb ccc dddd eeeee",
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25));

        for (int i = 1; i < words.Count; i++)
        {
            Assert.True(words[i].Start > words[i - 1].Start,
                $"Word {i} start ({words[i].Start}) should be > word {i - 1} start ({words[i - 1].Start})");
        }
    }

    [Fact]
    public void WordTextsMatchInputTokens()
    {
        var sut = MakeInterpolator();
        var words = sut.Interpolate("the quick brown fox",
            TimeSpan.Zero, TimeSpan.FromSeconds(8));

        Assert.Equal(4, words.Count);
        Assert.Equal("the", words[0].Text);
        Assert.Equal("quick", words[1].Text);
        Assert.Equal("brown", words[2].Text);
        Assert.Equal("fox", words[3].Text);
    }

    [Fact]
    public void MultipleSpacesBetweenWordsAreCollapsed()
    {
        var sut = MakeInterpolator();
        var words = sut.Interpolate("one   two\tthree",
            TimeSpan.Zero, TimeSpan.FromSeconds(6));

        Assert.Equal(3, words.Count);
        Assert.Equal("one", words[0].Text);
        Assert.Equal("two", words[1].Text);
        Assert.Equal("three", words[2].Text);
    }
}
