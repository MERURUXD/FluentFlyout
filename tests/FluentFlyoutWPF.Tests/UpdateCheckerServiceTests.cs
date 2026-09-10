using FluentFlyoutWPF.Classes.Downstream;
using FluentFlyoutWPF.Classes.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class UpdateCheckerServiceTests
{
    [Theory]
    [InlineData("development")]
    [InlineData("development+abcdef0")]
    [InlineData("dev+abcdef0")]
    public async Task NonStableCurrentIdentityDoesNotContactTheServer(string currentVersion)
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("The server must not be contacted.")));
        using var client = new HttpClient(handler);

        var result = await UpdateCheckerService.CheckDownstreamForUpdatesAsync(currentVersion, client);

        Assert.False(result.Success);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task NewerStableReleaseIsReportedAsAvailable()
    {
        var handler = JsonHandler("{\"tag_name\":\"v2.16.0\"}");
        using var client = new HttpClient(handler);

        var result = await UpdateCheckerService.CheckDownstreamForUpdatesAsync("v2.15.0", client);

        Assert.True(result.Success);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("v2.16.0", result.NewestVersion);
        Assert.Equal(ProductIdentity.LatestReleaseUrl, result.UpdateUrl);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(ProductIdentity.LatestReleaseApiUrl, handler.LastRequestUri?.ToString());
    }

    [Theory]
    [InlineData("v2.15.0")]
    [InlineData("v2.14.9")]
    public async Task SameOrOlderStableReleaseIsNotReportedAsAvailable(string newestVersion)
    {
        var handler = JsonHandler($"{{\"tag_name\":\"{newestVersion}\"}}");
        using var client = new HttpClient(handler);

        var result = await UpdateCheckerService.CheckDownstreamForUpdatesAsync("v2.15.0", client);

        Assert.True(result.Success);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task NotFoundIsHandledWithoutAFalseUpdate()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var client = new HttpClient(handler);

        var result = await UpdateCheckerService.CheckDownstreamForUpdatesAsync("v2.15.0", client);

        Assert.False(result.Success);
        Assert.False(result.IsUpdateAvailable);
        Assert.Empty(result.NewestVersion);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"dev+abcdef0\"}")]
    [InlineData("{\"tag_name\":\"v2.16.0-preview\"}")]
    [InlineData("{\"tag_name\":123}")]
    public async Task MalformedOrPrereleaseTagIsIgnored(string payload)
    {
        var handler = JsonHandler(payload);
        using var client = new HttpClient(handler);

        var result = await UpdateCheckerService.CheckDownstreamForUpdatesAsync("v2.15.0", client);

        Assert.False(result.Success);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task MalformedJsonIsHandledWithoutThrowing()
    {
        var handler = JsonHandler("{\"tag_name\":");
        using var client = new HttpClient(handler);

        var result = await UpdateCheckerService.CheckDownstreamForUpdatesAsync("v2.15.0", client);

        Assert.False(result.Success);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task NetworkFailureIsHandledWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));
        using var client = new HttpClient(handler);

        var result = await UpdateCheckerService.CheckDownstreamForUpdatesAsync("v2.15.0", client);

        Assert.False(result.Success);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task TimeoutCancellationIsHandledWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler(_ =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")));
        using var client = new HttpClient(handler);

        var result = await UpdateCheckerService.CheckDownstreamForUpdatesAsync("v2.15.0", client);

        Assert.False(result.Success);
        Assert.False(result.IsUpdateAvailable);
    }

    private static StubHttpMessageHandler JsonHandler(string payload) =>
        new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return responseFactory(request);
        }
    }
}
