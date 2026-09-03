using VerciWin.Core.Lyrics;
using Xunit;

namespace VerciWin.Core.Tests;

/// <summary>Unit tests for <see cref="LrcParser"/>.</summary>
public sealed class LrcParserTests
{
    private readonly LrcParser _sut = new();

    // ── Line-level LRC ────────────────────────────────────────────────────────

    [Fact]
    public void ParsesLineLevelTimestamps()
    {
        const string lrc = "[00:07.13] Caught in a landslide\n[00:14.77] Open your eyes";

        var doc = _sut.Parse(lrc);

        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(7.13), doc.Lines[0].Start, precision: 2);
        Assert.Equal(TimeSpan.FromSeconds(14.77), doc.Lines[1].Start, precision: 2);
    }

    [Fact]
    public void LineLevelTextIsTrimmed()
    {
        var doc = _sut.Parse("[01:00.00]   Hello world   ");
        Assert.Equal("Hello world", doc.Lines[0].Words[0].Text);
    }

    [Fact]
    public void EndTimeOfLineEqualsStartOfNextLine()
    {
        const string lrc = "[00:05.00] First\n[00:10.00] Second\n[00:20.00] Third";

        var doc = _sut.Parse(lrc);

        Assert.Equal(TimeSpan.FromSeconds(10), doc.Lines[0].End);
        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[1].End);
    }

    [Fact]
    public void EmptyLinesArePreservedAsBreaks()
    {
        // Empty/blank lines signal instrumental breaks.
        const string lrc = "[01:00.00] Verse\n[01:10.00] \n[01:30.00] Chorus";

        var doc = _sut.Parse(lrc);

        Assert.Equal(3, doc.Lines.Count);
        Assert.True(doc.Lines[1].IsEmpty, "Blank timestamped line should be an empty/break line");
    }

    [Fact]
    public void MetadataTagsAreIgnored()
    {
        const string lrc = "[ar:Queen]\n[ti:Bohemian Rhapsody]\n[00:05.00] Is this the real life?";

        var doc = _sut.Parse(lrc);

        Assert.Single(doc.Lines);
        Assert.Contains("Is this the real life?", doc.Lines[0].Words[0].Text);
    }

    [Fact]
    public void ParsesThreeDigitMilliseconds()
    {
        var doc = _sut.Parse("[00:07.130] Three digit ms");
        Assert.Equal(TimeSpan.FromSeconds(7.130), doc.Lines[0].Start, precision: 2);
    }

    [Fact]
    public void CommaDecimalSeparatorIsNormalized()
    {
        // Some LRC variants use comma instead of dot.
        var doc = _sut.Parse("[01:23,45] Comma variant");
        Assert.Equal(TimeSpan.FromSeconds(83.45), doc.Lines[0].Start, precision: 2);
    }

    [Fact]
    public void IsWordLevelFalseForLineLevelLrc()
    {
        var doc = _sut.Parse("[00:00.00] Hello world");
        Assert.False(doc.IsWordLevel);
    }

    [Fact]
    public void NullOrWhitespaceInputReturnsEmptyDocument()
    {
        Assert.Empty(_sut.Parse(null).Lines);
        Assert.Empty(_sut.Parse("").Lines);
        Assert.Empty(_sut.Parse("   ").Lines);
    }

    [Fact]
    public void LinesAreSortedByTimestamp()
    {
        // Out-of-order input (rare but possible).
        const string lrc = "[00:20.00] Second\n[00:05.00] First";
        var doc = _sut.Parse(lrc);
        Assert.True(doc.Lines[0].Start < doc.Lines[1].Start);
    }

    // ── A2 / Word-level LRC ───────────────────────────────────────────────────

    [Fact]
    public void ParsesWordLevelInlineTags()
    {
        const string lrc = "[01:00.00] <01:00.00>Hello <01:00.50>world <01:01.20>how";

        var doc = _sut.Parse(lrc);

        Assert.True(doc.IsWordLevel);
        Assert.Single(doc.Lines);
        var words = doc.Lines[0].Words;
        Assert.Equal(3, words.Count);
        Assert.Equal("Hello", words[0].Text);
        Assert.Equal("world", words[1].Text);
        Assert.Equal("how", words[2].Text);
    }

    [Fact]
    public void WordLevelStartTimesAreCorrect()
    {
        const string lrc = "[01:00.00] <01:00.00>one <01:00.80>two";

        var doc = _sut.Parse(lrc);
        var words = doc.Lines[0].Words;

        Assert.Equal(TimeSpan.FromSeconds(60.00), words[0].Start, precision: 2);
        Assert.Equal(TimeSpan.FromSeconds(60.80), words[1].Start, precision: 2);
    }

    [Fact]
    public void WordEndTimeIsNextWordStart()
    {
        const string lrc = "[00:05.00] <00:05.00>alpha <00:05.50>beta <00:06.00>gamma";

        var doc = _sut.Parse(lrc);
        var words = doc.Lines[0].Words;

        Assert.Equal(words[1].Start, words[0].End);
        Assert.Equal(words[2].Start, words[1].End);
    }

    // ── ParsePlain ────────────────────────────────────────────────────────────

    [Fact]
    public void ParsePlainCreatesLinesWithNoTiming()
    {
        var doc = _sut.ParsePlain("First line\nSecond line\nThird line");

        Assert.Equal(3, doc.Lines.Count);
        Assert.All(doc.Lines, l => Assert.Equal(TimeSpan.Zero, l.Start));
        Assert.False(doc.IsWordLevel);
    }
}

// TimeSpan comparison helper extension used with Assert.Equal overload accepting precision
public static class TimeSpanExtensions
{
    // xunit doesn't have built-in TimeSpan precision, so we use InRange in tests.
    // This attribute extension is a placeholder to document the pattern.
}
