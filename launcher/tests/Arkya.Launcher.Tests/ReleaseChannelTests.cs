using System.Net;
using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class ReleaseChannelTests
{
    private static readonly GameManifest Manifest = new(1, "fixture-1", "win-x64",
        "https://releases.example.test/game.zip", 1, 1, new string('a', 64), "Arkya.exe");

    [Theory]
    [InlineData(ReleaseChannel.Beta, "https://arkya.online/downloads/windows/latest-win-x64.json")]
    [InlineData(ReleaseChannel.Stable, "https://arkya.online/downloads/windows/latest-stable-win-x64.json")]
    public async Task ReleaseChecksOnlyTheFixedOfficialChannelAddress(ReleaseChannel channel, string expected)
    {
        var requested = new List<string>();
        using var http = Client(JsonSerializer.Serialize(Manifest, GameManifest.JsonOptions), requested);
        var manifest = await new ReleaseClient(http).GetChannelReleaseAsync(channel, CancellationToken.None);
        Assert.Equal(Manifest, manifest);
        Assert.Equal(new[] { expected }, requested);
    }

    [Fact]
    public async Task UnpublishedStableReturnsNoReleaseWithoutCheckingOrFallingBackToBeta()
    {
        var requested = new List<string>();
        using var http = Client("{\"schemaVersion\":1,\"channel\":\"stable\",\"available\":false}", requested);
        Assert.Null(await new ReleaseClient(http).GetChannelReleaseAsync(ReleaseChannel.Stable, CancellationToken.None));
        Assert.Equal(new[] { ReleaseChannels.GetManifestUrl(ReleaseChannel.Stable) }, requested);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":1,\"channel\":\"beta\",\"available\":false}")]
    [InlineData("{\"schemaVersion\":1,\"available\":false}")]
    [InlineData("{\"schemaVersion\":2,\"channel\":\"stable\",\"available\":false}")]
    [InlineData("{\"schemaVersion\":\"1\",\"channel\":\"stable\",\"available\":false}")]
    [InlineData("{\"schemaVersion\":1,\"channel\":\"stable\",\"available\":true}")]
    [InlineData("{\"schemaVersion\":1,\"channel\":\"stable\",\"available\":null}")]
    [InlineData("{\"schemaVersion\":1,\"channel\":null,\"available\":false}")]
    [InlineData("{\"schemaVersion\":1,\"channel\":\"stable\",\"available\":false,\"packageUrl\":\"https://example.test/game.zip\"}")]
    public async Task InvalidOrMismatchedUnpublishedMarkersAreErrorsWithoutFallback(string json)
    {
        var requested = new List<string>();
        using var http = Client(json, requested);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseClient(http).GetChannelReleaseAsync(ReleaseChannel.Stable, CancellationToken.None));
        Assert.Single(requested);
    }

    [Fact]
    public async Task OptionalManifestChannelMustMatchTheSelectedChannel()
    {
        var node = JsonSerializer.SerializeToNode(Manifest, GameManifest.JsonOptions)!;
        node["channel"] = "beta";
        using var http = Client(node.ToJsonString(), []);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseClient(http).GetChannelReleaseAsync(ReleaseChannel.Stable, CancellationToken.None));
    }

    [Fact]
    public async Task MissingStablePublicationIsAnErrorWithoutFallbackToBeta()
    {
        var requested = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            requested.Add(request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.NotFound);
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new ReleaseClient(http).GetChannelReleaseAsync(ReleaseChannel.Stable, CancellationToken.None));
        Assert.Equal(new[] { ReleaseChannels.GetManifestUrl(ReleaseChannel.Stable) }, requested);
    }

    [Fact]
    public async Task UnknownChannelDoesNotSendAnyRequest()
    {
        var requested = new List<string>();
        using var http = Client("{}", requested);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseClient(http).GetChannelReleaseAsync((ReleaseChannel)42, CancellationToken.None));
        Assert.Empty(requested);
    }

    private static HttpClient Client(string json, List<string> requested) => new(new Handler(request =>
    {
        requested.Add(request.RequestUri!.AbsoluteUri);
        return new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
