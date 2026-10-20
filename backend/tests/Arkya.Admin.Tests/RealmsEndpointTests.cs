using System.Net;
using System.Text.Json;
using Arkya.Admin.Realms;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class RealmsEndpointTests
{
    private static AdminFactory EnabledFactory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        return factory;
    }

    [Theory]
    [InlineData("/Realms")]
    [InlineData("/Realms/terres-aube")]
    public async Task RealmPagesRequireAuthentication(string path)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(path)).StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    [Theory]
    [InlineData("/Realms?handler=Create")]
    [InlineData("/Realms/terres-aube?handler=Deploy")]
    public async Task RealmMutationsRequireAntiforgery(string path)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync(path, AdminFactory.Form(new()))).StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    [Fact]
    public async Task RealmsAreDisabledByDefaultWithoutCallingTheirService()
    {
        await using var factory = new AdminFactory();
        factory.Settings["RealmsApi:AdminKey"] = "";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/Realms"));
        Assert.Contains("La gestion des realms est désactivée", html);
        var token = AdminFactory.Input(html, "__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync("/Realms?handler=Create", AdminFactory.Form(CreateForm(token)))).StatusCode);
        Assert.Empty(factory.Realms.Calls);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task ContentCanBeDisabledForARealmsOnlyInstallation()
    {
        await using var factory = EnabledFactory();
        factory.Settings["ContentApi:Enabled"] = "false";
        factory.Settings["ContentApi:AdminKey"] = "";
        factory.Settings["ContentApi:BaseUrl"] = "";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains("L'atelier de quêtes est désactivé", WebUtility.HtmlDecode(await browser.GetStringAsync("/Quests")));
        Assert.Empty(factory.Content.Calls);
        Assert.Contains("Mondes enregistrés", WebUtility.HtmlDecode(await browser.GetStringAsync("/Realms")));
    }

    [Fact]
    public async Task CreateSendsOnlyValidatedConfigurationAndRedirectsToTheRealm()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Realms");
        var form = CreateForm(token);
        form["Input.Image"] = "attacker.example/extra-image:latest";
        form["Input.DisplayName"] = "  Les Terres d'Aube  ";
        using var result = await browser.PostAsync("/Realms?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/Realms/terres-aube", result.Headers.Location!.OriginalString);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        var body = call.Body!.Value;
        Assert.Equal(4, body.EnumerateObject().Count());
        Assert.Equal(new CreateRealmRequest("terres-aube", "Les Terres d'Aube", "test-cluster", "island-test"),
            body.Deserialize<CreateRealmRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var html = await browser.GetStringAsync(result.Headers.Location);
        Assert.Contains("Realm créé", WebUtility.HtmlDecode(html));
        Assert.Contains("area-one", html);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("Input.RealmId", "aa")]
    [InlineData("Input.RealmId", "../secret")]
    [InlineData("Input.RealmId", "TEST-realm")]
    [InlineData("Input.DisplayName", "")]
    [InlineData("Input.DisplayName", "name\nline")]
    [InlineData("Input.TargetId", "unknown-cluster")]
    [InlineData("Input.ProfileId", "unknown-profile")]
    public async Task InvalidRealmDoesNotReachTheCreateApi(string field, string value)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Realms");
        var form = CreateForm(token);
        form[field] = value;
        using var result = await browser.PostAsync("/Realms?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var html = await result.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("Draft", "Déployer ce realm", 0)]
    [InlineData("Failed", "Relancer le déploiement", 7)]
    public async Task DeploymentPostsOnlyTheDisplayedRevision(string status, string button, long revision)
    {
        await using var factory = EnabledFactory();
        factory.Realms.Realm = factory.Realms.Realm with { Status = status, Revision = revision, LastError = "private upstream details " + AdminFactory.RealmsAdminKey };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Realms/terres-aube");
        Assert.Contains(button, WebUtility.HtmlDecode(html));
        Assert.Equal(revision.ToString(), AdminFactory.Input(html, "ExpectedRevision"));
        AdminFactory.NoSecrets(html);
        using var result = await browser.PostAsync("/Realms/terres-aube?handler=Deploy", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = AdminFactory.Input(html, "__RequestVerificationToken"), ["ExpectedRevision"] = revision.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal("/admin/realms/terres-aube/deploy", call.Path);
        Assert.Equal("expectedRevision", Assert.Single(call.Body!.Value.EnumerateObject()).Name);
        Assert.Equal(revision, call.Body.Value.GetProperty("expectedRevision").GetInt64());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("9223372036854775808")]
    public async Task MissingOrInvalidRevisionDoesNotDeploy(string? revision)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = await AdminFactory.Token(browser, "/Realms/terres-aube") };
        if (revision is not null) form["ExpectedRevision"] = revision;
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/Realms/terres-aube?handler=Deploy", AdminFactory.Form(form))).StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task ConflictReloadsCurrentRevisionAndShowsNoUpstreamDetails()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Realms/terres-aube");
        factory.Realms.WriteError = HttpStatusCode.Conflict;
        factory.Realms.Realm = factory.Realms.Realm with { Revision = 3 };
        using var result = await browser.PostAsync("/Realms/terres-aube?handler=Deploy", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["ExpectedRevision"] = "1"
        }));
        var html = await result.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("a changé", WebUtility.HtmlDecode(html));
        Assert.Equal("3", AdminFactory.Input(html, "ExpectedRevision"));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task DisabledDeploymentRejectsAHandcraftedPost()
    {
        await using var factory = EnabledFactory();
        factory.Realms.Options = factory.Realms.Options with { DeploymentEnabled = false };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Realms/terres-aube");
        using var result = await browser.PostAsync("/Realms/terres-aube?handler=Deploy", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["ExpectedRevision"] = "1"
        }));
        Assert.Contains("Le déploiement est désactivé", WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpstreamFailureProducesAnActionableSafePage(bool malformedJson)
    {
        await using var factory = EnabledFactory();
        factory.Realms.InvalidJson = malformedJson;
        if (!malformedJson) factory.Realms.ReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Realms");
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Réessayer", WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("Ready")]
    [InlineData("Pending")]
    [InlineData("Deploying")]
    public async Task ActiveDeploymentIsVisibleWithoutAnotherDeployButton(string status)
    {
        await using var factory = EnabledFactory();
        factory.Realms.Realm = factory.Realms.Realm with { Status = status };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Realms/terres-aube");
        Assert.DoesNotContain("handler=Deploy", html);
        Assert.Contains(RealmPresentation.StatusLabel(status), WebUtility.HtmlDecode(html));
        Assert.Contains("admission des joueurs", WebUtility.HtmlDecode(html));
        Assert.All(factory.Realms.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
    }

    [Theory]
    [InlineData("http://realms.example")]
    [InlineData("https://key@realms.example")]
    [InlineData("https://realms.example?key=secret")]
    public void UnsafeOutboundDestinationsFailStartup(string baseUrl)
    {
        using var factory = EnabledFactory();
        factory.Settings["RealmsApi:BaseUrl"] = baseUrl;
        Assert.Throws<OptionsValidationException>(() => factory.Browser());
    }

    private static Dictionary<string, string> CreateForm(string token) => new()
    {
        ["__RequestVerificationToken"] = token, ["Input.RealmId"] = "terres-aube", ["Input.DisplayName"] = "Les Terres d'Aube",
        ["Input.TargetId"] = "test-cluster", ["Input.ProfileId"] = "island-test"
    };

    [Theory]
    [InlineData("cluster_access_denied", "Le cluster a refusé l'accès")]
    [InlineData("resource_ownership_conflict", "appartient à un autre propriétaire")]
    [InlineData("deployment_timeout", "dans le délai prévu")]
    public async Task KnownFailureCodesShowSafeRecoveryGuidance(string code, string expected)
    {
        await using var factory = EnabledFactory();
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Failed", LastError = code };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Realms/terres-aube");
        Assert.Contains(expected, WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
    }
}
