using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Caching;

/// <summary>
/// Persists and retrieves <see cref="LyricDocument"/> objects as JSON files
/// under <c>%AppData%\VerciWin\lyrics\{normalizedKey}.json</c>.
/// <para>
/// Cache key: <c>artist|title</c> lowercased, trimmed, invalid path characters
/// replaced with underscores. This is deterministic and reversible for debugging.
/// </para>
/// </summary>
public sealed class LyricCacheStore
{
    private readonly string _cacheDir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Characters that are invalid in Windows file names.
    private static readonly Regex InvalidPathChars =
        new(@"[<>:""/\\|?*\x00-\x1F]", RegexOptions.Compiled);

    public LyricCacheStore(string? overrideCacheDir = null)
    {
        _cacheDir = overrideCacheDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VerciWin", "lyrics");
        Directory.CreateDirectory(_cacheDir);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a normalized cache key from artist and title.
    /// Deterministic: same inputs always produce the same key.
    /// </summary>
    public static string BuildKey(string artist, string title)
    {
        var raw = $"{artist.Trim().ToLowerInvariant()}|{title.Trim().ToLowerInvariant()}";
        return InvalidPathChars.Replace(raw, "_");
    }

    /// <summary>
    /// Reads a cached <see cref="LyricDocument"/> by key.
    /// Returns <c>null</c> on cache miss OR if the file is corrupt.
    /// Never throws.
    /// </summary>
    public async Task<LyricDocument?> ReadAsync(string key)
    {
        var path = FilePath(key);
        if (!File.Exists(path)) return null;

        try
        {
            await using var fs = File.OpenRead(path);
            var dto = await JsonSerializer.DeserializeAsync<LyricDocumentDto>(fs, JsonOpts);
            return dto?.ToDomain();
        }
        catch
        {
            // Corrupt JSON, partial write, etc. — treat as cache miss.
            return null;
        }
    }

    /// <summary>
    /// Writes a <see cref="LyricDocument"/> to the cache.
    /// Uses atomic write (temp file + rename) to avoid partial writes.
    /// </summary>
    public async Task WriteAsync(string key, LyricDocument document)
    {
        var finalPath = FilePath(key);
        var tempPath = finalPath + ".tmp";

        try
        {
            await using var fs = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 65536, useAsync: true);

            await JsonSerializer.SerializeAsync(fs, LyricDocumentDto.FromDomain(document), JsonOpts);
            await fs.FlushAsync();
        }
        catch
        {
            // Clean up temp file on failure.
            try { File.Delete(tempPath); } catch { /* ignore */ }
            throw;
        }

        // Atomic rename.
        File.Move(tempPath, finalPath, overwrite: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string FilePath(string key) => Path.Combine(_cacheDir, $"{key}.json");

    // ─────────────────────────────────────────────────────────────────────────
    // DTOs (flat, JSON-serialisable — avoids TimeSpan serialization quirks)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class LyricDocumentDto
    {
        public bool IsWordLevel { get; init; }
        public List<LyricLineDto> Lines { get; init; } = new();

        public static LyricDocumentDto FromDomain(LyricDocument d) => new()
        {
            IsWordLevel = d.IsWordLevel,
            Lines = d.Lines.Select(LyricLineDto.FromDomain).ToList(),
        };

        public LyricDocument ToDomain() => new()
        {
            IsWordLevel = IsWordLevel,
            Lines = Lines.Select(l => l.ToDomain()).ToList(),
        };
    }

    private sealed class LyricLineDto
    {
        public long StartMs { get; init; }
        public long EndMs { get; init; }
        public bool IsInterpolated { get; init; }
        public List<LyricWordDto> Words { get; init; } = new();

        public static LyricLineDto FromDomain(LyricLine l) => new()
        {
            StartMs = (long)l.Start.TotalMilliseconds,
            EndMs = (long)l.End.TotalMilliseconds,
            IsInterpolated = l.IsInterpolated,
            Words = l.Words.Select(LyricWordDto.FromDomain).ToList(),
        };

        public LyricLine ToDomain() => new()
        {
            Start = TimeSpan.FromMilliseconds(StartMs),
            End = TimeSpan.FromMilliseconds(EndMs),
            IsInterpolated = IsInterpolated,
            Words = Words.Select(w => w.ToDomain()).ToList(),
        };
    }

    private sealed class LyricWordDto
    {
        public string Text { get; init; } = string.Empty;
        public long StartMs { get; init; }
        public long EndMs { get; init; }

        public static LyricWordDto FromDomain(LyricWord w) => new()
        {
            Text = w.Text,
            StartMs = (long)w.Start.TotalMilliseconds,
            EndMs = (long)w.End.TotalMilliseconds,
        };

        public LyricWord ToDomain() => new()
        {
            Text = Text,
            Start = TimeSpan.FromMilliseconds(StartMs),
            End = TimeSpan.FromMilliseconds(EndMs),
        };
    }
}
