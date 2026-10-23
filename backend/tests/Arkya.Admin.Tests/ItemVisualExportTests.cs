using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Admin.Tests;

public sealed class ItemVisualExportTests
{
    private const string Guid = ContentStub.AssetGuid;
    private const string Route = ItemVisualExport.PathPrefix + "/" + Guid;
    private static readonly ItemVisualRegistration Registration = new("E1-A", "Assets/Arkya/Items/Weapons/Prefabs/E1-A.prefab",
        "prefab", "abcdef1234567890abcdef1234567890", "a-png-payload-validated-by-content");

    [Fact]
    public async Task ExportIsDisabledByDefault()
    {
        await using var factory = new AdminFactory();
        using var client = factory.Browser();
        using var response = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData(AdminFactory.McpKey)]
    [InlineData(AdminFactory.ReportsAdminKey)]
    [InlineData(AdminFactory.RealmsAdminKey)]
    public async Task OnlyTheContentExportKeyAuthenticates(string? key)
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var request = Request(key: key);
        request.Headers.Authorization = new("Bearer", AdminFactory.McpKey);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(factory.Content.Calls);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DuplicateKeyIsRejectedBeforeReadingTheBody()
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var request = Request(content: new StringContent("invalid json"));
        request.Headers.TryAddWithoutValidation("X-Arkya-Admin-Key", AdminFactory.AdminKey);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task CookieAloneCannotExportAndValidExportDoesNotConsultOrRenewSessions()
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        await AdminFactory.Login(client);
        factory.Sessions.Unavailable = true;
        using var denied = await client.SendAsync(Request(key: null));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var accepted = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.False(accepted.Headers.Contains("Set-Cookie"));
        Assert.Single(factory.Content.Calls);
    }

    [Fact]
    public async Task ExportForwardsOnlyTheTypedRegistrationAndContentKey()
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var request = Request();
        request.Headers.TryAddWithoutValidation("Cookie", "Arkya.Admin=untrusted");
        request.Headers.TryAddWithoutValidation("X-Arkya-Service-Key", "untrusted-player-key");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(factory.Content.Calls);
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.Equal(Route, call.Path);
        var body = call.Body!.Value;
        Assert.Equal(Registration, body.Deserialize<ItemVisualRegistration>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(5, body.EnumerateObject().Count());
        Assert.Equal(Guid, (await response.Content.ReadFromJsonAsync<ItemVisualAsset>())!.Guid);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public async Task OnlyPutIsExposed(string method)
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var request = Request();
        request.Method = new(method);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("1234567890ABCDEF1234567890ABCDEF")]
    [InlineData("no-guid")]
    [InlineData(Guid + "/thumbnail")]
    public async Task InvalidOrNestedAssetRoutesAreNotProxied(string suffix)
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var response = await client.SendAsync(Request(route: ItemVisualExport.PathPrefix + "/" + suffix));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("https://localhost")]
    [InlineData("null")]
    public async Task BrowserOriginsAreRejected(string origin)
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var request = Request();
        request.Headers.TryAddWithoutValidation("Origin", origin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task HttpsIsRequiredEvenInDevelopment()
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var response = await client.SendAsync(Request(route: "http://localhost" + Route));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"name\":\"E1-A\",\"assetPath\":\"Assets/A.prefab\",\"kind\":\"prefab\",\"dependencyHash\":\"abcdef1234567890abcdef1234567890\",\"adminKey\":\"injected\"}")]
    public async Task MalformedOrUnknownJsonIsRejected(string json)
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var response = await client.SendAsync(Request(content: new StringContent(json, Encoding.UTF8, "application/json")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("application/json; charset=utf-16")]
    public async Task UnsupportedMediaTypesAreRejected(string media)
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        using var content = new StringContent("{}");
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(media);
        using var response = await client.SendAsync(Request(content: content));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedBodiesAreRejectedIncludingUnknownLengths(bool chunked)
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        var bytes = Encoding.UTF8.GetBytes(new string(' ', ItemVisualExport.MaximumRequestBytes + 1));
        using HttpContent content = chunked ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/json");
        using var response = await client.SendAsync(Request(content: content));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TemporaryRedirect, HttpStatusCode.BadGateway)]
    public async Task UpstreamErrorsAreSanitized(HttpStatusCode upstream, HttpStatusCode expected)
    {
        await using var factory = Enabled();
        factory.Content.WriteError = upstream;
        using var client = factory.Browser();
        using var response = await client.SendAsync(Request());
        Assert.Equal(expected, response.StatusCode);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("chunked")]
    [InlineData("malformed")]
    [InlineData("wrong-guid")]
    [InlineData("html")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task InvalidOrUnavailableUpstreamResponsesAreBoundedAndSanitized(string scenario)
    {
        await using var factory = Enabled();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient<ContentAdminClient>().ConfigurePrimaryHttpMessageHandler(() => new ResponseHandler(scenario))));
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.SendAsync(Request());
        Assert.Equal(scenario == "timeout" ? HttpStatusCode.GatewayTimeout : HttpStatusCode.BadGateway, response.StatusCode);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnauthenticatedRequestsHaveABoundedRateLimitPerClientAddress()
    {
        await using var factory = Enabled();
        using var client = factory.Browser();
        for (var i = 0; i < ItemVisualExport.RequestsPerMinute; i++)
        {
            using var response = await client.SendAsync(Request(key: null));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var limited = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task ExhaustingOneClientQuotaDoesNotBlockAnotherClientOrTrustSpoofedHeaders()
    {
        await using var factory = Enabled();
        var body = JsonSerializer.SerializeToUtf8Bytes(Registration, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        async Task<int> SendFromAsync(string source, bool authenticated, string? forwarded = null)
        {
            var result = await factory.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(source);
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost");
                context.Request.Method = "PUT";
                context.Request.Path = Route;
                context.Request.ContentType = "application/json";
                context.Request.Body = new MemoryStream(body);
                if (authenticated) context.Request.Headers["X-Arkya-Admin-Key"] = AdminFactory.AdminKey;
                if (forwarded is not null) context.Request.Headers["X-Forwarded-For"] = forwarded;
            });
            return result.Response.StatusCode;
        }
        for (var i = 0; i < ItemVisualExport.RequestsPerMinute; i++)
            Assert.Equal(401, await SendFromAsync("203.0.113.10", false));
        Assert.Equal(429, await SendFromAsync("203.0.113.10", true, "203.0.113.11"));
        Assert.Equal(200, await SendFromAsync("203.0.113.11", true));
        Assert.Single(factory.Content.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesToContent()
    {
        await using var factory = Enabled();
        var handler = new WaitingHandler();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient<ContentAdminClient>().ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var cancellation = new CancellationTokenSource();
        var pending = client.SendAsync(Request(), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static AdminFactory Enabled()
    {
        var factory = new AdminFactory();
        factory.Settings["ItemVisualExport:Enabled"] = "true";
        return factory;
    }

    private static HttpRequestMessage Request(string? key = AdminFactory.AdminKey, HttpContent? content = null, string route = Route)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, route) { Content = content ?? JsonContent.Create(Registration) };
        if (key is not null) request.Headers.TryAddWithoutValidation("X-Arkya-Admin-Key", key);
        return request;
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    private sealed class ResponseHandler(string scenario) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (scenario == "network") throw new HttpRequestException("private upstream details " + AdminFactory.AdminKey);
            if (scenario == "timeout") throw new TaskCanceledException("private upstream details " + AdminFactory.AdminKey);
            HttpContent content = scenario switch
            {
                "oversized" => new StringContent(new string(' ', 16385)),
                "chunked" => new UnknownLengthContent(Encoding.UTF8.GetBytes(new string(' ', 16385))),
                "wrong-guid" => JsonContent.Create(new ItemVisualAsset(ContentStub.OtherAssetGuid, "Sword", "Assets/Sword.prefab", "prefab", Guid, true, DateTimeOffset.UtcNow)),
                _ => new StringContent("private upstream details " + AdminFactory.AdminKey)
            };
            content.Headers.ContentType = new(scenario == "html" ? "text/html" : "application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Cancelled.TrySetResult(cancellationToken.IsCancellationRequested); }
            return new(HttpStatusCode.OK);
        }
    }
}
