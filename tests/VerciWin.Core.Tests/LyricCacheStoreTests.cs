using System.IO;
using VerciWin.Core.Caching;
using VerciWin.Core.Lyrics;
using VerciWin.Core.Lyrics.Models;
using Xunit;

namespace VerciWin.Core.Tests;

/// <summary>
/// Integration-style tests for <see cref="LyricCacheStore"/>.
/// Uses a temp directory so tests do not pollute %AppData%.
/// </summary>
public sealed class LyricCacheStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LyricCacheStore _sut;

    public LyricCacheStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VerciWinTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new LyricCacheStore(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAndReadRoundTrip_PreservesAllFields()
    {
        var doc = MakeDocument();
        var key = LyricCacheStore.BuildKey("The Beatles", "Hey Jude");

        await _sut.WriteAsync(key, doc);
        var result = await _sut.ReadAsync(key);

        Assert.NotNull(result);
        Assert.Equal(doc.IsWordLevel, result.IsWordLevel);
        Assert.Equal(doc.Lines.Count, result.Lines.Count);

        var origWord = doc.Lines[0].Words[0];
        var readWord = result.Lines[0].Words[0];
        Assert.Equal(origWord.Text, readWord.Text);
        Assert.Equal(origWord.Start, readWord.Start);
        Assert.Equal(origWord.End, readWord.End);
    }

    [Fact]
    public async Task WriteAndReadRoundTrip_PreservesIsInterpolated()
    {
        var doc = MakeDocument(isInterpolated: true);
        var key = "test_interpolated";

        await _sut.WriteAsync(key, doc);
        var result = await _sut.ReadAsync(key);

        Assert.NotNull(result);
        Assert.True(result.Lines[0].IsInterpolated);
    }

    [Fact]
    public async Task WriteAndReadRoundTrip_EmptyLinesPreserved()
    {
        var doc = new LyricDocument
        {
            Lines = new List<LyricLine>
            {
                new() { Words = new(), Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(10) },
            }
        };
        var key = "test_empty_line";

        await _sut.WriteAsync(key, doc);
        var result = await _sut.ReadAsync(key);

        Assert.NotNull(result);
        Assert.True(result.Lines[0].IsEmpty);
    }

    // ── Cache miss ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_ReturnNullOnMiss()
    {
        var result = await _sut.ReadAsync("nonexistent_key");
        Assert.Null(result);
    }

    // ── Corrupt file ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_ReturnsNullOnCorruptJson()
    {
        var key = "corrupt_test";
        var path = Path.Combine(_tempDir, $"{key}.json");
        await File.WriteAllTextAsync(path, "{ this is not valid json %%%");

        var result = await _sut.ReadAsync(key);
        Assert.Null(result);
    }

    // ── Key normalization ─────────────────────────────────────────────────────

    [Fact]
    public void BuildKey_NormalizesArtistAndTitle()
    {
        var key = LyricCacheStore.BuildKey("  The Beatles  ", "  Hey Jude  ");
        Assert.Equal("the beatles|hey jude", key);
    }

    [Fact]
    public void BuildKey_ReplacesInvalidPathChars()
    {
        var key = LyricCacheStore.BuildKey("AC/DC", "Back in Black");
        // '/' is invalid in file names and must be replaced.
        Assert.DoesNotContain("/", key);
        Assert.DoesNotContain("\\", key);
    }

    [Fact]
    public void BuildKey_IsDeterministic()
    {
        var key1 = LyricCacheStore.BuildKey("Queen", "Bohemian Rhapsody");
        var key2 = LyricCacheStore.BuildKey("Queen", "Bohemian Rhapsody");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildKey_IsCaseInsensitive()
    {
        var key1 = LyricCacheStore.BuildKey("QUEEN", "BOHEMIAN RHAPSODY");
        var key2 = LyricCacheStore.BuildKey("queen", "bohemian rhapsody");
        Assert.Equal(key1, key2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LyricDocument MakeDocument(bool isInterpolated = false)
    {
        return new LyricDocument
        {
            IsWordLevel = false,
            Lines = new List<LyricLine>
            {
                new()
                {
                    Start = TimeSpan.FromSeconds(5),
                    End = TimeSpan.FromSeconds(10),
                    IsInterpolated = isInterpolated,
                    Words = new List<LyricWord>
                    {
                        new() { Text = "Hello", Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(7) },
                        new() { Text = "World", Start = TimeSpan.FromSeconds(7), End = TimeSpan.FromSeconds(10) },
                    }
                }
            }
        };
    }
}
