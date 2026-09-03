using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using VerciWin.Core.Lyrics.Models;

namespace VerciWin.Core.Lyrics;

/// <summary>
/// Fetches synced lyrics from the LRCLIB public API (https://lrclib.net).
/// <para>
/// API contract (verified via live call 2026-09-03):
/// <list type="bullet">
///   <item><c>GET /api/get</c> — required params: <c>track_name</c>, <c>artist_name</c>;
///         recommended: <c>album_name</c>, <c>duration</c> (float seconds).</item>
///   <item>Response field names: <c>trackName</c>, <c>artistName</c>, <c>albumName</c>,
///         <c>syncedLyrics</c>, <c>plainLyrics</c>, <c>instrumental</c>.</item>
///   <item>Auth: none. Required header: <c>User-Agent</c>.</item>
///   <item>HTTP 404 = no match. HTTP 429 = rate limited; honour <c>Retry-After</c>.</item>
/// </list>
/// </para>
/// <para>
/// Rate limiting: A self-imposed minimum of 300 ms between outbound requests is used
/// as a conservative default — this is NOT a published LRCLIB constraint.
/// The <c>Retry-After</c> header on HTTP 429 IS a confirmed API behaviour and is honoured.
/// </para>
/// </summary>
public sealed class LrcLibProvider : ILyricProvider
{
    private readonly HttpClient _http;
    private readonly LrcParser _parser;

    // Self-imposed 300 ms minimum between calls (conservative default, not LRCLIB policy).
    private DateTime _lastCallUtc = DateTime.MinValue;
    private static readonly TimeSpan MinCallInterval = TimeSpan.FromMilliseconds(300);

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
        // Primary: /api/get (exact match lookup).
        var doc = await TryGetExactAsync(title, artist, album, duration, ct);
        if (doc is not null) return doc;

        // Fallback: /api/search (fuzzy search).
        return await TrySearchAsync(title, artist, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<LyricDocument?> TryGetExactAsync(
        string title, string artist, string album,
        TimeSpan duration, CancellationToken ct)
    {
        var url = "/api/get" +
            $"?track_name={Uri.EscapeDataString(title)}" +
            $"&artist_name={Uri.EscapeDataString(artist)}" +
            $"&album_name={Uri.EscapeDataString(album)}" +
            $"&duration={duration.TotalSeconds:F1}";

        var dto = await CallApiAsync<LrcLibResponse>(url, ct);
        return MapResponse(dto);
    }

    private async Task<LyricDocument?> TrySearchAsync(
        string title, string artist, CancellationToken ct)
    {
        var url = "/api/search" +
            $"?track_name={Uri.EscapeDataString(title)}" +
            $"&artist_name={Uri.EscapeDataString(artist)}";

        var results = await CallApiAsync<LrcLibResponse[]>(url, ct);
        if (results is null || results.Length == 0) return null;

        // Pick the first result with synced lyrics, otherwise first result at all.
        var best = results.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
                   ?? results[0];
        return MapResponse(best);
    }

    private async Task<T?> CallApiAsync<T>(string relativeUrl, CancellationToken ct)
    {
        // Self-imposed throttle.
        var elapsed = DateTime.UtcNow - _lastCallUtc;
        if (elapsed < MinCallInterval)
            await Task.Delay(MinCallInterval - elapsed, ct);
        _lastCallUtc = DateTime.UtcNow;

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(relativeUrl, ct);
        }
        catch (HttpRequestException)
        {
            // Caller (LyricService) decides what to do with network failures.
            throw;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // LRCLIB confirmed behaviour: honour Retry-After on 429.
            int delaySeconds = 5; // fallback
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

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    private LyricDocument? MapResponse(LrcLibResponse? dto)
    {
        if (dto is null) return null;
        if (dto.Instrumental == true) return null; // No lyrics for instrumentals.

        if (!string.IsNullOrWhiteSpace(dto.SyncedLyrics))
            return _parser.Parse(dto.SyncedLyrics);

        if (!string.IsNullOrWhiteSpace(dto.PlainLyrics))
            return _parser.ParsePlain(dto.PlainLyrics);

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DTO (field names confirmed from live LRCLIB API call, 2026-09-03)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class LrcLibResponse
    {
        [JsonPropertyName("id")]       public int Id { get; init; }
        [JsonPropertyName("trackName")] public string? TrackName { get; init; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; init; }
        [JsonPropertyName("albumName")] public string? AlbumName { get; init; }
        [JsonPropertyName("duration")]  public double Duration { get; init; }
        [JsonPropertyName("instrumental")] public bool? Instrumental { get; init; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; init; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; init; }
    }
}
