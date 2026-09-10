using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Lyrics;

/// <summary>
/// Fetches synced lyrics from the LRCLIB public API (https://lrclib.net).
/// <para>
/// API contract:
/// <list type="bullet">
///   <item><c>GET /api/get</c> — required params: <c>track_name</c>, <c>artist_name</c>;
///         optional: <c>album_name</c>, <c>duration</c> (seconds).</item>
///   <item><c>GET /api/search</c> — params: <c>track_name</c>, <c>artist_name</c> or <c>q</c>.</item>
///   <item>Response field names: <c>trackName</c>, <c>artistName</c>, <c>albumName</c>,
///         <c>duration</c> (can be null), <c>syncedLyrics</c>, <c>plainLyrics</c>, <c>instrumental</c>.</item>
///   <item>Auth: none. Required header: <c>User-Agent</c>.</item>
///   <item>HTTP 404 = no match. HTTP 429 = rate limited; honour <c>Retry-After</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class LrcLibProvider : ILyricProvider
{
    private readonly HttpClient _http;
    private readonly LrcParser _parser;

    // Self-imposed 300 ms minimum between calls
    private DateTime _lastCallUtc = DateTime.MinValue;
    private static readonly TimeSpan MinCallInterval = TimeSpan.FromMilliseconds(300);

    private static readonly Regex CleanTitleRegex = new(
        @"\s*(\([^\)]*(?:feat\.|featuring|remaster|live|official|deluxe|version|edit|bonus|from\s+)[^\)]*\)|\[[^\]]*(?:feat\.|featuring|remaster|live|official|deluxe|version|edit|bonus|from\s+)[^\]]*\]|\-\s*(?:remaster|live|deluxe|bonus|radio edit|edit|single|version).*$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public LrcLibProvider(HttpClient httpClient, LrcParser parser)
    {
        _http = httpClient;
        _parser = parser;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ILyricProvider
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<LyricDocument?> GetLyricsAsync(
        string title, string artist, string album,
        TimeSpan duration, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;

        // Tier 1: Exact match with album and duration (if available)
        var doc = await TryGetExactAsync(title, artist, album, duration, ct);
        if (doc is not null) return doc;

        // Tier 2: Relaxed Get (track_name and artist_name only)
        if (!string.IsNullOrWhiteSpace(album) || duration.TotalSeconds > 0)
        {
            doc = await TryGetRelaxedAsync(title, artist, ct);
            if (doc is not null) return doc;
        }

        // Tier 3: Structured Search (track_name + artist_name)
        doc = await TrySearchStructuredAsync(title, artist, duration, ct);
        if (doc is not null) return doc;

        // Tier 4: Fuzzy Search with cleaned title and q parameter
        return await TrySearchFuzzyAsync(title, artist, duration, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<LyricDocument?> TryGetExactAsync(
        string title, string artist, string album,
        TimeSpan duration, CancellationToken ct)
    {
        var url = $"/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";

        if (!string.IsNullOrWhiteSpace(album))
        {
            url += $"&album_name={Uri.EscapeDataString(album)}";
        }

        if (duration.TotalSeconds > 0)
        {
            url += $"&duration={duration.TotalSeconds:F0}";
        }

        var dto = await CallApiAsync<LrcLibResponse>(url, ct);
        return MapResponse(dto);
    }

    private async Task<LyricDocument?> TryGetRelaxedAsync(
        string title, string artist, CancellationToken ct)
    {
        var url = $"/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var dto = await CallApiAsync<LrcLibResponse>(url, ct);
        return MapResponse(dto);
    }

    private async Task<LyricDocument?> TrySearchStructuredAsync(
        string title, string artist, TimeSpan duration, CancellationToken ct)
    {
        var url = $"/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var results = await CallApiAsync<LrcLibResponse[]>(url, ct);
        return SelectBestResult(results, duration);
    }

    private async Task<LyricDocument?> TrySearchFuzzyAsync(
        string title, string artist, TimeSpan duration, CancellationToken ct)
    {
        string cleanTitle = CleanTitleRegex.Replace(title, "").Trim();
        if (string.IsNullOrWhiteSpace(cleanTitle))
            cleanTitle = title.Trim();

        // 1. Try search with cleaned track name
        if (!string.Equals(cleanTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            var urlStructured = $"/api/search?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(artist)}";
            var resultsStructured = await CallApiAsync<LrcLibResponse[]>(urlStructured, ct);
            var doc = SelectBestResult(resultsStructured, duration);
            if (doc is not null) return doc;
        }

        // 2. Try general search query 'q'
        var query = $"{cleanTitle} {artist}".Trim();
        var urlQ = $"/api/search?q={Uri.EscapeDataString(query)}";
        var resultsQ = await CallApiAsync<LrcLibResponse[]>(urlQ, ct);
        return SelectBestResult(resultsQ, duration);
    }

    private LyricDocument? SelectBestResult(LrcLibResponse[]? results, TimeSpan duration)
    {
        if (results is null || results.Length == 0) return null;

        // Filter out instrumentals and entries with no lyrics
        var candidates = results
            .Where(r => r.Instrumental != true && (!string.IsNullOrWhiteSpace(r.SyncedLyrics) || !string.IsNullOrWhiteSpace(r.PlainLyrics)))
            .ToList();

        if (candidates.Count == 0) return null;

        // 1. Find synced lyrics closest to duration (if duration provided)
        if (duration.TotalSeconds > 0)
        {
            var syncedWithDuration = candidates
                .Where(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics) && r.Duration.HasValue && r.Duration.Value > 0)
                .OrderBy(r => Math.Abs(r.Duration!.Value - duration.TotalSeconds))
                .FirstOrDefault();

            if (syncedWithDuration != null && Math.Abs(syncedWithDuration.Duration!.Value - duration.TotalSeconds) <= 15.0)
            {
                return MapResponse(syncedWithDuration);
            }
        }

        // 2. Pick first synced lyrics
        var firstSynced = candidates.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics));
        if (firstSynced != null)
        {
            return MapResponse(firstSynced);
        }

        // 3. Fallback to first candidate with plain lyrics
        return MapResponse(candidates[0]);
    }

    private async Task<T?> CallApiAsync<T>(string relativeUrl, CancellationToken ct)
    {
        // Self-imposed throttle
        var elapsed = DateTime.UtcNow - _lastCallUtc;
        if (elapsed < MinCallInterval)
            await Task.Delay(MinCallInterval - elapsed, ct);
        _lastCallUtc = DateTime.UtcNow;

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(relativeUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[LrcLibProvider] HTTP request failed for {relativeUrl}: {ex.Message}");
            throw;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            int delaySeconds = 5;
            if (response.Headers.TryGetValues("Retry-After", out var values)
                && int.TryParse(values.FirstOrDefault(), out int parsed))
            {
                delaySeconds = parsed;
            }
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            _lastCallUtc = DateTime.UtcNow;
            response = await _http.GetAsync(relativeUrl, ct);
        }

        if (!response.IsSuccessStatusCode)
            return default;

        try
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException jex)
        {
            Debug.WriteLine($"[LrcLibProvider] JSON deserialization failed for {relativeUrl}: {jex.Message}");
            return default;
        }
    }

    private LyricDocument? MapResponse(LrcLibResponse? dto)
    {
        if (dto is null) return null;
        if (dto.Instrumental == true) return null;

        if (!string.IsNullOrWhiteSpace(dto.SyncedLyrics))
            return _parser.Parse(dto.SyncedLyrics);

        if (!string.IsNullOrWhiteSpace(dto.PlainLyrics))
            return _parser.ParsePlain(dto.PlainLyrics);

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DTO
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class LrcLibResponse
    {
        [JsonPropertyName("id")]           public long? Id { get; init; }
        [JsonPropertyName("name")]         public string? Name { get; init; }
        [JsonPropertyName("trackName")]     public string? TrackName { get; init; }
        [JsonPropertyName("artistName")]    public string? ArtistName { get; init; }
        [JsonPropertyName("albumName")]     public string? AlbumName { get; init; }
        [JsonPropertyName("duration")]      public double? Duration { get; init; }
        [JsonPropertyName("instrumental")]  public bool? Instrumental { get; init; }
        [JsonPropertyName("syncedLyrics")]  public string? SyncedLyrics { get; init; }
        [JsonPropertyName("plainLyrics")]   public string? PlainLyrics { get; init; }
    }
}
