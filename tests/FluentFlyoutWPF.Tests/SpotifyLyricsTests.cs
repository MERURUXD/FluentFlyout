using FluentFlyoutWPF.Classes.Downstream.Lyrics;
using System.Net;
using System.Net.Http;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class SpotifyLyricsTests
{
    private static readonly LyricsTrack Track = new("小雨", "黄龄", "花&色", 166000);
    private static readonly LyricsDocument Document = new("QQ", "1", [new("test", 0, 100, [])], []);

    [Theory]
    [InlineData("chrome.exe")]
    [InlineData("msedge.exe")]
    [InlineData("firefox.exe")]
    [InlineData("browser.spotify.com")]
    [InlineData("NotSpotify.exe")]
    [InlineData(null)]
    public async Task NonSpotifyCannotQueryEvenWithSpotifyMetadata(string? appId)
    {
        var provider = new FakeProvider();
        using var service = new SpotifyLyricsService(provider, provider);
        await service.SelectAsync(true, new object(), appId, Track with { Title = "Spotify" });
        Assert.Equal(0, provider.Searches);
        Assert.Null(service.Current);
    }

    [Theory]
    [InlineData("Spotify.exe")]
    [InlineData("Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    public void NativeSpotifyIdentitiesAreAccepted(string id) => Assert.True(LyricsMatchPolicy.IsSpotify(id));

    [Fact]
    public async Task NeverEnabledDoesNoWork()
    {
        var p = new FakeProvider();
        using var service = new SpotifyLyricsService(p, p);
        await service.SelectAsync(false, new object(), "Spotify.exe", Track);
        Assert.Equal(0, p.Searches);
    }

    [Fact]
    public async Task QqHitSkipsNeteaseAndCacheSurvivesOwnerChanges()
    {
        var qq = new FakeProvider();
        var netease = new FakeProvider();
        using var service = new SpotifyLyricsService(qq, netease);
        await service.SelectAsync(true, new object(), "Spotify.exe", Track);
        await service.SelectAsync(true, new object(), "Spotify.exe", Track);
        Assert.Same(Document, service.Current);
        Assert.Equal(1, qq.Searches);
        Assert.Equal(1, qq.Fetches);
        Assert.Equal(0, netease.Searches);
    }

    [Fact]
    public async Task EmptyCombinedQueryRetriesTitleBeforeNetease()
    {
        var qq = new FakeProvider { EmptyCombined = true };
        var netease = new FakeProvider();
        using var service = new SpotifyLyricsService(qq, netease);
        await service.SelectAsync(true, new object(), "Spotify.exe", Track);
        Assert.Equal(2, qq.Searches);
        Assert.Equal(0, netease.Searches);
        Assert.Same(Document, service.Current);
    }

    [Fact]
    public async Task QqFailureFallsBackAndMissingLyricsAreCached()
    {
        var qq = new FakeProvider { Fails = true };
        var netease = new FakeProvider();
        using var service = new SpotifyLyricsService(qq, netease);
        await service.SelectAsync(true, new object(), "Spotify.exe", Track);
        Assert.Equal(1, netease.Fetches);
        netease.Empty = true;
        var other = Track with { Album = "other" };
        await service.SelectAsync(true, new object(), "Spotify.exe", other);
        var calls = qq.Searches + netease.Searches;
        await service.SelectAsync(true, new object(), "Spotify.exe", other);
        Assert.Equal(calls, qq.Searches + netease.Searches);
        Assert.Null(service.Current);
    }

    [Theory]
    [InlineData("browser")]
    [InlineData("disable")]
    [InlineData("dispose")]
    [InlineData("restart")]
    [InlineData("track")]
    public async Task InvalidationCancelsAndRejectsLatePublication(string action)
    {
        var pending = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        var qq = new FakeProvider { Fetch = token => { captured = token; started.SetResult(); return pending.Task; } };
        var netease = new FakeProvider();
        using var service = new SpotifyLyricsService(qq, netease);
        var first = service.SelectAsync(true, new object(), "Spotify.exe", Track);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (action == "dispose") service.Dispose();
        else if (action is "restart" or "track")
        {
            qq.Fetch = _ => Task.FromResult<LyricsDocument?>(Document with { TrackId = "new" });
            await service.SelectAsync(true, new object(), "Spotify.exe",
                action == "track" ? Track with { Album = "another release" } : Track);
        }
        else await service.SelectAsync(action != "disable", new object(),
            action == "disable" ? "Spotify.exe" : "chrome.exe", Track);
        Assert.True(captured.IsCancellationRequested);
        pending.SetResult(Document);
        await first;
        if (action is "restart" or "track") Assert.Equal("new", service.Current?.TrackId);
        else Assert.Null(service.Current);
        Assert.Equal(0, netease.Searches);
    }

    [Fact]
    public void MatchingPreservesVersionAndArtistWhileNormalizingKnownMetadata()
    {
        Assert.True(LyricsMatchPolicy.Matches(new("玄鳥（《长月烬明》电视剧插曲）", "SAJI萨吉", "", 256000),
            new("玄鸟", "萨吉", "", 256000)));
        Assert.True(LyricsMatchPolicy.Matches(Track, Track with { DurationMs = 165000 }));
        Assert.False(LyricsMatchPolicy.Matches(Track, Track with { Artist = "翻唱歌手" }));
        Assert.False(LyricsMatchPolicy.Matches(Track, Track with { Title = "小雨 (Live)" }));
        Assert.False(LyricsMatchPolicy.Matches(Track, Track with { DurationMs = 197000 }));
        Assert.Null(LyricsMatchPolicy.Select(Track, [new("a", Track), new("b", Track)]));
    }

    [Fact]
    public async Task CacheExpiresWithoutStartingABackgroundTimer()
    {
        var clock = new Clock();
        var qq = new FakeProvider();
        using var service = new SpotifyLyricsService(qq, new FakeProvider(), clock);
        var owner = new object();
        await service.SelectAsync(true, owner, "Spotify.exe", Track);
        clock.Now += TimeSpan.FromHours(13);
        Assert.Equal(1, qq.Searches);
        await service.SelectAsync(true, owner, "Spotify.exe", Track);
        Assert.Equal(2, qq.Searches);
    }

    [Fact]
    public async Task SameOwnerAndTrackSharePendingRequest()
    {
        var pending = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var qq = new FakeProvider { Fetch = _ => pending.Task };
        using var service = new SpotifyLyricsService(qq, new FakeProvider());
        var owner = new object();
        var first = service.SelectAsync(true, owner, "Spotify.exe", Track);
        var second = service.SelectAsync(true, owner, "Spotify.exe", Track);
        Assert.Same(first, second);
        pending.SetResult(Document);
        await first;
        Assert.Equal(1, qq.Fetches);
    }

    [Fact]
    public async Task SearchTransportKeepsChineseAndParsesCandidateMetadata()
    {
        using var client = new HttpClient(new SearchHandler());
        var provider = new OnlineLyricsProvider(client, true);
        var results = await provider.SearchAsync("小雨 黄龄", CancellationToken.None);
        var match = Assert.Single(results);
        Assert.Equal("383142539", match.Id);
        Assert.True(LyricsMatchPolicy.Matches(Track, match.Track));
    }

    [Fact]
    public async Task HttpAdapterReadsQrcWordTimingsAndTranslation()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<root><miniversion=\"1\" /><content><![CDATA[[0,1000]你(0,400)好(400,600)]]></content><contentts><![CDATA[[00:00.00]Hello]]></contentts></root>")
        }));
        var provider = new OnlineLyricsProvider(client, true);
        var result = await provider.FetchAsync("1", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(2, result.Lines[0].Words.Count);
        Assert.Equal(400, result.Lines[0].Words[1].StartMs);
        Assert.Equal("Hello", result.Translation[0].Text);
    }

    [Fact]
    public async Task HttpCancellationReachesTransport()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CancelHandler(started);
        using var client = new HttpClient(handler);
        var provider = new OnlineLyricsProvider(client, true);
        using var cancellation = new CancellationTokenSource();
        var task = provider.SearchAsync("test", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(handler.Cancelled);
    }

    private sealed class FakeProvider : ILyricsProvider
    {
        public string Name => "fake";
        public int Searches;
        public int Fetches;
        public bool Empty;
        public bool EmptyCombined;
        public bool Fails;
        public Func<CancellationToken, Task<LyricsDocument?>> Fetch = _ => Task.FromResult<LyricsDocument?>(Document);
        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Searches++;
            if (Fails) throw new HttpRequestException("test");
            return Task.FromResult<IReadOnlyList<LyricsCandidate>>(Empty || (EmptyCombined && query.Contains(' ')) ? [] : [new("1", Track)]);
        }
        public Task<LyricsDocument?> FetchAsync(string id, CancellationToken cancellationToken)
        {
            Fetches++;
            return Fetch(cancellationToken);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(reply(request));
    }

    private sealed class SearchHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("小雨 黄龄", body);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"req_1":{"data":{"body":{"song":{"list":[{"id":383142539,"title":"小雨","interval":165,"singer":[{"name":"黄龄"}],"album":{"title":"花&色"}}]}}}}}
                    """)
            };
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CancelHandler(TaskCompletionSource started) : HttpMessageHandler
    {
        public bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            throw new InvalidOperationException();
        }
    }
}
