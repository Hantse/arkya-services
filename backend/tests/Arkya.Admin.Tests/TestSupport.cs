using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arkya.Admin.Content;
using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Admin.Tests;

internal sealed record ContentCall(HttpMethod Method, string Path, JsonElement? Body);

internal sealed class ContentStub : HttpMessageHandler
{
    public static readonly string Version = "95f1b50c-85ba-49a6-9b75-e12745472cf4";
    public ContentDraft Draft { get; set; } = new(3, [new("hunt-wolves", "La menace du bois", "Vaincre les ennemis.", "prototype-monster", 3)]);
    public ContentCatalog[] Catalogs { get; set; } = [new(1, Version,
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), [new("hunt-wolves", "La menace du bois", "Vaincre les ennemis.", "prototype-monster", 3)])];
    public List<ContentCall> Calls { get; } = [];
    public HttpStatusCode? WriteError { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(AdminFactory.AdminKey, Assert.Single(request.Headers.GetValues("X-Arkya-Admin-Key")));
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("X-Arkya-Service-Key"));
        JsonElement? body = request.Content is null ? null : await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var call = new ContentCall(request.Method, request.RequestUri!.AbsolutePath, body);
        Calls.Add(call);
        if (request.Method != HttpMethod.Get && WriteError is { } error)
            return new(error) { Content = new StringContent($"private upstream details {AdminFactory.AdminKey}") };
        if (request.Method == HttpMethod.Get && call.Path == "/admin/content/draft")
            return Json(Draft);
        if (request.Method == HttpMethod.Get && call.Path == "/admin/content/catalogs")
            return Json(Catalogs);
        if (request.Method == HttpMethod.Put && call.Path == "/admin/content/draft")
        {
            var submitted = body!.Value.Deserialize<ContentDraft>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Draft = submitted with { Revision = submitted.Revision + 1 };
            return Json(Draft);
        }
        if (request.Method == HttpMethod.Post && call.Path == "/admin/content/catalogs")
            return Json(Catalogs[0]);
        return new(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

internal sealed class AdminFactory : WebApplicationFactory<Arkya.Admin.Program>
{
    public const string Password = "admin-test-password-for-http-tests";
    public const string AdminKey = "content-outbound-test-admin-key-1234";
    public const string RealmsAdminKey = "realms-outbound-test-admin-key-12345";
    public ContentStub Content { get; } = new();
    public RealmsStub Realms { get; } = new();
    public Dictionary<string, string?> Settings { get; } = [];
    public string EnvironmentName { get; set; } = "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:Password"] = Password,
                ["ContentApi:BaseUrl"] = "https://content.example/",
                ["ContentApi:AdminKey"] = AdminKey,
                ["RealmsApi:BaseUrl"] = "https://realms.example/",
                ["RealmsApi:AdminKey"] = RealmsAdminKey
            });
            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<ContentAdminClient>().ConfigurePrimaryHttpMessageHandler(() => Content);
            services.AddHttpClient<RealmsAdminClient>().ConfigurePrimaryHttpMessageHandler(() => Realms);
        });
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    });

    public static async Task<string> Token(HttpClient browser, string path = "/")
    {
        using var response = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Input(await response.Content.ReadAsStringAsync(), "__RequestVerificationToken");
    }

    public static async Task Login(HttpClient browser)
    {
        var token = await Token(browser, "/Login");
        using var response = await browser.PostAsync("/Login", Form(new()
        {
            ["Password"] = Password, ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    public static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    public static Dictionary<string, string> QuestForm(string token, string title = "Une nouvelle aventure", string kills = "8", string revision = "3") => new()
    {
        ["__RequestVerificationToken"] = token, ["Revision"] = revision,
        ["Quests[0].QuestId"] = "new-adventure", ["Quests[0].Title"] = title,
        ["Quests[0].Description"] = "Une description conservée.", ["Quests[0].RequiredKills"] = kills
    };

    public static string Input(string html, string name)
    {
        var input = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(match => match.Value).First(value => Regex.IsMatch(value, $"\\bname=\"{Regex.Escape(name)}\""));
        var match = Regex.Match(input, "\\bvalue=\"([^\"]*)\"");
        Assert.True(match.Success, $"Expected a value for input {name}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static void NoSecrets(string response)
    {
        Assert.DoesNotContain(Password, response);
        Assert.DoesNotContain(AdminKey, response);
        Assert.DoesNotContain(RealmsAdminKey, response);
        Assert.DoesNotContain("private upstream details", response);
    }
}
