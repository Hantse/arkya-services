using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Arkya.Gateway.Tests;

public sealed class GatewayProxyTests
{
    [Theory]
    [InlineData("GET", "/v1/realms")]
    [InlineData("POST", "/v1/characters/ab1890c5-9930-4ac0-bafd-cd15a48bceca/admissions")]
    public async Task Game_routes_forward_to_characters_without_dedicated_credentials(string method, string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var characters = await Downstream.StartAsync("characters");
        await using var factory = new GatewayFactory(identity.Address, characters.Address);
        using var client = factory.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Arkya-Dedicated-Key", "must-not-reach-public-downstream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "player-session");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(characters.Requests);
        Assert.False(received.Headers.ContainsKey("X-Arkya-Dedicated-Key"));
        Assert.Equal("Bearer player-session", received.Headers["Authorization"]);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/internal/game/admissions/consume", null)).StatusCode);
    }

    [Theory]
    [InlineData("POST", "/v1/auth/discord/challenges")]
    [InlineData("GET", "/v1/auth/discord/authorize/0123456789abcdef0123456789abcdef")]
    [InlineData("GET", "/v1/auth/discord/callback")]
    [InlineData("POST", "/v1/auth/discord/exchange")]
    [InlineData("GET", "/v1/auth/me")]
    [InlineData("POST", "/v1/auth/logout")]
    public async Task Public_identity_routes_preserve_method_path_query_body_and_bearer(string method, string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var factory = new GatewayFactory(identity.Address);
        using var client = factory.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), path + "?code=secret%2Bcode&state=opaque-state");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "opaque-session-token");
        request.Headers.Add("Cookie", "correlation=opaque-cookie");
        request.Content = new StringContent("{\"challengeId\":\"challenge\"}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(identity.Requests);
        Assert.Equal(method, received.Method);
        Assert.Equal(path, received.Path);
        Assert.Equal("?code=secret%2Bcode&state=opaque-state", received.Query);
        Assert.Equal("{\"challengeId\":\"challenge\"}", received.Body);
        Assert.Equal("Bearer opaque-session-token", received.Headers["Authorization"]);
        Assert.Equal("correlation=opaque-cookie", received.Headers["Cookie"]);
        Assert.Equal("api.arkya.online", received.Headers["Host"]);
        Assert.DoesNotContain(factory.Logs.Messages, message => message.Contains("secret%2Bcode")
            || message.Contains("opaque-session-token") || message.Contains("opaque-cookie"));
    }

    [Fact]
    public async Task Trusted_proxy_scheme_and_client_ip_replace_all_untrusted_forwarding_headers()
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var factory = new GatewayFactory(identity.Address);
        using var client = factory.Client("http");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/me");
        request.Headers.Add("X-Forwarded-Proto", "http, https");
        request.Headers.Add("X-Forwarded-For", "198.51.100.66, 203.0.113.7");
        request.Headers.Add("X-Forwarded-Host", "attacker.example");
        request.Headers.Add("X-Forwarded-Prefix", "/internal");
        request.Headers.Add("X-Forwarded-Custom", "forged");
        request.Headers.Add("X-Original-Proto", "http");
        request.Headers.Add("X-Original-Host", "attacker.example");
        request.Headers.Add("Forwarded", "for=198.51.100.66;proto=http;host=attacker.example");
        request.Headers.Add("X-Arkya-Service-Key", "must-not-reach-public-downstream");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(identity.Requests);
        Assert.Equal("https", received.Headers["X-Forwarded-Proto"]);
        Assert.Equal("203.0.113.7", received.Headers["X-Forwarded-For"]);
        Assert.Equal("api.arkya.online", received.Headers["Host"]);
        Assert.DoesNotContain(received.Headers.Keys, key => key.StartsWith("X-Original-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("X-Forwarded-Host", received.Headers.Keys);
        Assert.DoesNotContain("X-Forwarded-Prefix", received.Headers.Keys);
        Assert.DoesNotContain("X-Forwarded-Custom", received.Headers.Keys);
        Assert.DoesNotContain("Forwarded", received.Headers.Keys);
        Assert.DoesNotContain("X-Arkya-Service-Key", received.Headers.Keys);
    }

    [Fact]
    public async Task Untrusted_forwarded_https_does_not_bypass_production_https_requirement()
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var factory = new GatewayFactory(identity.Address, remoteIp: "203.0.113.99");
        using var client = factory.Client("http");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        using var response = await client.GetAsync("/v1/auth/me");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(identity.Requests);
    }

    [Theory]
    [InlineData("/internal/sessions/introspect")]
    [InlineData("/internal/sessions/introspect-reference")]
    [InlineData("/internal/characters/11111111-1111-1111-1111-111111111111/resolve")]
    [InlineData("/admin")]
    [InlineData("/dev/auth/login")]
    [InlineData("/v1/auth/discord/social/config")]
    [InlineData("/v1/auth/discord/social/exchange")]
    [InlineData("/v1/auth/discord/social/refresh")]
    [InlineData("/v1/auth/new-route")]
    [InlineData("/v1/characters/11111111-1111-1111-1111-111111111111/quests")]
    [InlineData("/v1/characters/not-a-guid")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Non_public_routes_never_reach_downstream(string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var characters = await Downstream.StartAsync("characters");
        await using var factory = new GatewayFactory(identity.Address, characters.Address);
        using var client = factory.Client();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(identity.Requests);
        Assert.Empty(characters.Requests);
    }

    [Theory]
    [InlineData("DELETE", "/v1/auth/me")]
    [InlineData("GET", "/v1/auth/discord/challenges")]
    [InlineData("POST", "/v1/auth/discord/callback")]
    [InlineData("DELETE", "/v1/characters")]
    [InlineData("POST", "/v1/characters/11111111-1111-1111-1111-111111111111")]
    public async Task Disallowed_methods_never_reach_downstream(string method, string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var factory = new GatewayFactory(identity.Address, identity.Address);
        using var client = factory.Client();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task OAuth_redirect_cookie_and_location_remain_unchanged()
    {
        const string location = "https://discord.com/oauth2/authorize?client_id=123&state=opaque-state";
        const string cookie = "arkya-correlation=opaque; Path=/v1/auth/discord/callback; Secure; HttpOnly; SameSite=Lax";
        await using var identity = await Downstream.StartAsync("identity", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = location;
            context.Response.Headers.SetCookie = cookie;
            await context.Response.WriteAsync("redirect body");
        });
        await using var factory = new GatewayFactory(identity.Address);
        using var client = factory.Client();
        using var response = await client.GetAsync("/v1/auth/discord/authorize/0123456789abcdef0123456789abcdef");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(location, response.Headers.Location!.OriginalString);
        Assert.Equal(cookie, Assert.Single(response.Headers.GetValues("Set-Cookie")));
        Assert.Equal("redirect body", await response.Content.ReadAsStringAsync());
        Assert.Single(identity.Requests);
    }

    [Theory]
    [InlineData(202)]
    [InlineData(401)]
    [InlineData(410)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Downstream_status_body_and_retry_after_are_preserved(int status)
    {
        await using var identity = await Downstream.StartAsync("identity", async context =>
        {
            context.Response.StatusCode = status;
            context.Response.Headers.RetryAfter = "5";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"status\":\"upstream-result\"}");
        });
        await using var factory = new GatewayFactory(identity.Address);
        using var client = factory.Client();
        using var response = await client.PostAsync("/v1/auth/discord/exchange", new StringContent("{}"));

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter!.Delta);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("{\"status\":\"upstream-result\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cors_preflight_and_response_are_owned_by_identity()
    {
        const string origin = "https://arkya-atlas.easykrill33.chatgpt.site";
        await using var identity = await Downstream.StartAsync("identity", context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.AccessControlAllowMethods = "POST";
            context.Response.Headers.AccessControlAllowHeaders = "content-type,authorization";
            return Task.CompletedTask;
        });
        await using var factory = new GatewayFactory(identity.Address);
        using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/auth/discord/challenges");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var received = Assert.Single(identity.Requests);
        Assert.Equal("OPTIONS", received.Method);
        Assert.Equal(origin, received.Headers["Origin"]);
        Assert.Equal("POST", received.Headers["Access-Control-Request-Method"]);
    }

    [Theory]
    [InlineData("GET", "/v1/characters")]
    [InlineData("POST", "/v1/characters")]
    [InlineData("GET", "/v1/characters/11111111-1111-1111-1111-111111111111")]
    [InlineData("GET", "/v1/characters/appearance-options")]
    public async Task Character_routes_target_only_characters_when_enabled(string method, string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var characters = await Downstream.StartAsync("characters");
        await using var factory = new GatewayFactory(identity.Address, characters.Address);
        using var client = factory.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "character-session");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(identity.Requests);
        var received = Assert.Single(characters.Requests);
        Assert.Equal(path, received.Path);
        Assert.Equal("Bearer character-session", received.Headers["Authorization"]);
    }

    [Fact]
    public async Task Characters_disabled_by_default_has_no_public_routes()
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var factory = new GatewayFactory(identity.Address);
        using var client = factory.Client();
        using var response = await client.GetAsync("/v1/characters");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Unavailable_identity_does_not_remove_character_routes_or_log_callback_secrets()
    {
        await using var characters = await Downstream.StartAsync("characters");
        await using var factory = new GatewayFactory("http://127.0.0.1:1/", characters.Address);
        using var client = factory.Client();
        using var failure = await client.GetAsync("/v1/auth/discord/callback?code=private-oauth-code&state=private-oauth-state");
        using var success = await client.GetAsync("/v1/characters/appearance-options");

        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Single(characters.Requests);
        Assert.DoesNotContain(factory.Logs.Messages, message => message.Contains("private-oauth-code")
            || message.Contains("private-oauth-state"));
    }

    [Fact]
    public async Task Unknown_public_host_is_rejected_before_proxying()
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var factory = new GatewayFactory(identity.Address);
        using var client = factory.Client();
        client.DefaultRequestHeaders.Host = "attacker.example";
        using var response = await client.GetAsync("/v1/auth/me");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Health_is_local_and_does_not_depend_on_downstream_availability()
    {
        await using var factory = new GatewayFactory("http://127.0.0.1:1/");
        using var client = factory.Client();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("arkya-gateway", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health/live", "live")]
    [InlineData("/health/ready", "ready")]
    public async Task Management_probe_uses_listener_port_and_does_not_call_unavailable_services(string path, string status)
    {
        await using var factory = new GatewayFactory("http://127.0.0.1:1/", localPort: 8081);
        using var client = factory.Client("http");
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(status, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Management_port_does_not_proxy_and_host_port_cannot_expose_management_on_public_listener()
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using (var management = new GatewayFactory(identity.Address, localPort: 8081))
        {
            using var client = management.Client("http");
            using var response = await client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        await using (var gateway = new GatewayFactory(identity.Address, localPort: 8080))
        {
            using var client = gateway.Client();
            client.DefaultRequestHeaders.Host = "api.arkya.online:8081";
            using var response = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Empty(identity.Requests);
    }
}
