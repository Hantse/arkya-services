using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Admin.Accounts;
using Arkya.ServiceDefaults.Accounts;
using Arkya.ServiceDefaults.Characters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Admin.Tests;

internal sealed record PlayerAdminCall(HttpMethod Method, Uri Uri, JsonElement? Body, string? Actor);

internal sealed class AccountsStub : HttpMessageHandler
{
    public const string Key = "accounts-private-outbound-admin-key-123456";
    public static readonly Guid Id = Guid.Parse("2f3b0e58-3fb1-4d9c-b2e8-dceba2dc92cf");
    public static readonly Guid CharacterId = Guid.Parse("0ed086a0-9eb0-4ffb-9511-427657401d48");
    public AdminAccountSummary Account { get; set; } = new(Id.ToString("D"), "Aelys", "123456789012345678", false, null, false, 1,
        DateTimeOffset.Parse("2026-10-20T10:00:00Z"), DateTimeOffset.Parse("2026-10-20T10:00:00Z"));
    public List<PlayerAdminCall> Calls { get; } = [];
    public long Total { get; set; } = 80;
    public bool Empty { get; set; }
    public bool InvalidJson { get; set; }
    public HttpStatusCode? ReadError { get; set; }
    public HttpStatusCode? WriteError { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Assert.Equal(Key, Assert.Single(request.Headers.GetValues("X-Arkya-Admin-Key")));
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("X-Arkya-Service-Key"));
        var body = request.Content is null ? (JsonElement?)null : await request.Content.ReadFromJsonAsync<JsonElement>(ct);
        var actor = request.Headers.TryGetValues("X-Arkya-Admin-Actor", out var values) ? Assert.Single(values) : null;
        var uri = request.RequestUri!;
        Calls.Add(new(request.Method, uri, body, actor));
        if ((request.Method == HttpMethod.Get ? ReadError : WriteError) is { } error)
            return new(error) { Content = new StringContent($"private upstream details {Key}") };
        if (InvalidJson) return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        if (request.Method != HttpMethod.Get)
        {
            Assert.Equal("admin:operator", actor);
            var submitted = body!.Value;
            Assert.Equal(Account.Revision, submitted.GetProperty("expectedRevision").GetInt64());
            Account = Account with
            {
                Revision = Account.Revision + 1,
                IsBanned = uri.AbsolutePath.EndsWith("/ban") || !uri.AbsolutePath.EndsWith("/unban") && Account.IsBanned,
                HasBetaAccess = uri.AbsolutePath.EndsWith("/beta-access") ? submitted.GetProperty("hasBetaAccess").GetBoolean() : Account.HasBetaAccess
            };
            return Json(Account);
        }
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        var page = query.TryGetValue("page", out var pageValue) ? int.Parse(pageValue.ToString()) : 1;
        var pageSize = query.TryGetValue("pageSize", out var sizeValue) ? int.Parse(sizeValue.ToString()) : 25;
        if (uri.AbsolutePath == "/admin/accounts") return Json(new AdminAccountPage(Empty ? [] : [Account], Total, page, pageSize));
        if (uri.AbsolutePath.StartsWith("/admin/accounts/")) return Json(Account);
        if (uri.AbsolutePath == "/admin/characters/overview") return Json(new AdminCharacterOverview(Total, 1, [new("beta", 1)]));
        if (uri.AbsolutePath == "/admin/characters") return Json(new AdminCharacterPage(Empty ? [] :
            [new(CharacterId.ToString("D"), Id.ToString("D"), "<script>character</script>", "beta", "human", "a", DateTimeOffset.UtcNow, true, DateTimeOffset.UtcNow.AddSeconds(60))], Total, page, pageSize));
        return new(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

internal sealed class AccountsFactory : WebApplicationFactory<Arkya.Admin.Program>
{
    public AccountsStub Upstream { get; } = new();
    public Dictionary<string, string?> Settings { get; } = [];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:Password"] = AdminFactory.Password,
                ["Admin:DiscordEnabled"] = "false",
                ["ContentApi:Enabled"] = "false", ["RealmsApi:Enabled"] = "false",
                ["ReportsApi:Enabled"] = "false", ["InferenceApi:Enabled"] = "false",
                ["IdentityAdminApi:Enabled"] = "true", ["IdentityAdminApi:BaseUrl"] = "https://identity.example/", ["IdentityAdminApi:AdminKey"] = AccountsStub.Key,
                ["CharactersAdminApi:Enabled"] = "true", ["CharactersAdminApi:BaseUrl"] = "https://characters.example/", ["CharactersAdminApi:AdminKey"] = AccountsStub.Key
            });
            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureServices(services =>
        {
            services.AddTestSessions();
            services.AddHttpClient<IdentityAdminClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);
            services.AddHttpClient<CharactersAdminClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);
        });
    }
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    });
}
