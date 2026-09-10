using System.Net;
using System.Text;
using Lyrx.Core.Lyrics;
using Xunit;

namespace Lyrx.Core.Tests;

public class LrcLibProviderTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private static LrcLibProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var client = new HttpClient(new MockHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://lrclib.net")
        };
        var parser = new LrcParser();
        return new LrcLibProvider(client, parser);
    }

    [Fact]
    public async Task GetLyricsAsync_HandlesNullDurationInSearchResponse_WithoutThrowing()
    {
        // LRCLIB API returns duration: null for certain search results
        string searchJson = @"[
            {
                ""id"": 101,
                ""trackName"": ""Starboy"",
                ""artistName"": ""The Weeknd"",
                ""albumName"": ""Starboy"",
                ""duration"": null,
                ""instrumental"": false,
                ""syncedLyrics"": ""[00:01.00] I'm tryna put you in the worst mood, ah\n[00:04.00] P1 cleaner than your church shoes, ah"",
                ""plainLyrics"": ""I'm tryna put you in the worst mood""
            }
        ]";

        var provider = CreateProvider(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/get"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(searchJson, Encoding.UTF8, "application/json")
            };
        });

        var result = await provider.GetLyricsAsync("Starboy (feat. Daft Punk)", "The Weeknd", "", TimeSpan.FromSeconds(230));

        Assert.NotNull(result);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal("I'm tryna put you in the worst mood, ah", result.Lines[0].Words[0].Text);
    }

    [Fact]
    public async Task GetLyricsAsync_FallsBackToSearch_WhenExactGetReturns404()
    {
        string searchJson = @"[
            {
                ""id"": 202,
                ""trackName"": ""Bohemian Rhapsody"",
                ""artistName"": ""Queen"",
                ""albumName"": ""A Night at the Opera"",
                ""duration"": 355.0,
                ""instrumental"": false,
                ""syncedLyrics"": ""[00:00.15] Is this the real life?\n[00:07.13] Caught in a landslide"",
                ""plainLyrics"": ""Is this the real life?""
            }
        ]";

        var provider = CreateProvider(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/get"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(searchJson, Encoding.UTF8, "application/json")
            };
        });

        var result = await provider.GetLyricsAsync("Bohemian Rhapsody", "Queen", "Wrong Album", TimeSpan.FromSeconds(355));

        Assert.NotNull(result);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public async Task GetLyricsAsync_CleansRemasteredTags_WhenSearching()
    {
        string searchJson = @"[
            {
                ""id"": 303,
                ""trackName"": ""Yesterday"",
                ""artistName"": ""The Beatles"",
                ""albumName"": ""Help!"",
                ""duration"": 125.0,
                ""instrumental"": false,
                ""syncedLyrics"": ""[00:01.00] Yesterday, all my troubles seemed so far away"",
                ""plainLyrics"": ""Yesterday""
            }
        ]";

        bool cleanedSearchAttempted = false;

        var provider = CreateProvider(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/get"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var query = req.RequestUri.Query;
            if (query.Contains("Yesterday") && !query.Contains("Remastered"))
            {
                cleanedSearchAttempted = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searchJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await provider.GetLyricsAsync("Yesterday - Remastered 2009", "The Beatles", "Help!", TimeSpan.FromSeconds(125));

        Assert.True(cleanedSearchAttempted);
        Assert.NotNull(result);
    }
}
