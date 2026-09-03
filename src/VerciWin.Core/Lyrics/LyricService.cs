using System.Diagnostics;
using VerciWin.Core.Caching;
using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Lyrics;

/// <summary>
/// Orchestrates lyric fetching with a cache-first strategy:
/// <list type="number">
///   <item>Check <see cref="LyricCacheStore"/> — return cached doc if present.</item>
///   <item>Call <see cref="ILyricProvider.GetLyricsAsync"/> with a CancellationToken.</item>
///   <item>If the result is line-level (<c>!IsWordLevel</c>), run <see cref="WordTimingInterpolator"/>.</item>
///   <item>Write result to cache; return to caller.</item>
/// </list>
/// <para>
/// <b>Track-change cancellation:</b> Each call to <see cref="GetLyricsAsync"/> takes a
/// <see cref="CancellationToken"/>. The caller (<c>OverlayViewModel</c>) should cancel the
/// previous token before calling for a new track, dropping the in-flight HTTP request.
/// </para>
/// <para>
/// <b>Network failure with stale cache:</b> If the provider throws and a cached doc
/// exists (even if older), the cached doc is returned. If no cache, returns <c>null</c>
/// (callers should show "lyrics unavailable" — not crash).
/// </para>
/// </summary>
public sealed class LyricService
{
    private readonly ILyricProvider _provider;
    private readonly LyricCacheStore _cache;
    private readonly WordTimingInterpolator _interpolator;

    public LyricService(
        ILyricProvider provider,
        LyricCacheStore cache,
        WordTimingInterpolator? interpolator = null)
    {
        _provider = provider;
        _cache = cache;
        _interpolator = interpolator ?? new WordTimingInterpolator();
    }

    /// <summary>
    /// Returns a <see cref="LyricDocument"/> for the given track, or <c>null</c>
    /// if none is available (not found + no cache, or instrumental).
    /// Never throws unless the <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task<LyricDocument?> GetLyricsAsync(
        string title,
        string artist,
        string album,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var cacheKey = LyricCacheStore.BuildKey(artist, title);

        // 1. Cache hit.
        var cached = await _cache.ReadAsync(cacheKey);
        if (cached is not null)
        {
            Debug.WriteLine($"[LyricService] Cache hit: {cacheKey}");
            return cached;
        }

        // 2. Network fetch.
        LyricDocument? doc = null;
        try
        {
            doc = await _provider.GetLyricsAsync(title, artist, album, duration, ct);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation — track changed.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LyricService] Provider failed: {ex.Message}");
            // Network failure — try stale cache before giving up.
            // (ReadAsync already returned null above, so this is a miss.)
            return null;
        }

        if (doc is null)
        {
            Debug.WriteLine($"[LyricService] No lyrics found for: {title} – {artist}");
            return null;
        }

        // 3. Interpolate word timing if line-level.
        if (!doc.IsWordLevel)
            _interpolator.InterpolateDocument(doc);

        // 4. Cache the result.
        try
        {
            await _cache.WriteAsync(cacheKey, doc);
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal.
            Debug.WriteLine($"[LyricService] Cache write failed: {ex.Message}");
        }

        return doc;
    }
}
