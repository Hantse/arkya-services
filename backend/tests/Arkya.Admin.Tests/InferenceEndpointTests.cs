using System.Net;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class InferenceEndpointTests
{
    [Fact]
    public async Task PageRequiresAdminAuthentication()
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        var result = await browser.GetAsync("/Inference");
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/Login", new Uri(browser.BaseAddress!, result.Headers.Location!).AbsolutePath);
        Assert.Empty(factory.Inference.Calls);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("AddWorker")]
    [InlineData("DeleteWorker")]
    [InlineData("DeleteAgent")]
    public async Task EveryMutationRequiresAntiforgery(string handler)
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var result = await browser.PostAsync("/Inference?handler=" + handler, AdminFactory.Form(new()));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Empty(factory.Inference.Calls);
    }

    [Fact]
    public async Task DisabledPageHasNoActionsAndRejectsHandcraftedPosts()
    {
        await using var factory = new InferenceFactory();
        factory.Settings["InferenceApi:Enabled"] = "false";
        factory.Settings["InferenceApi:AdminKey"] = "";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Inference");
        Assert.Contains("La gestion de l’inférence est désactivée", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("handler=Create", html);
        var form = CreateForm(AdminFactory.Input(html, "__RequestVerificationToken"));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync("/Inference?handler=Create", AdminFactory.Form(form))).StatusCode);
        Assert.Empty(factory.Inference.Calls);
    }

    [Fact]
    public async Task CreateDisplaysCredentialOnlyInItsNoStorePostResponse()
    {
        await using var factory = new InferenceFactory();
        factory.Inference.EnrollmentToken = "single-use-display-<token>&123456789";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = CreateForm(await AdminFactory.Token(browser, "/Inference"));
        form["Input.DisplayName"] = "  <script>alert('x')</script>  ";
        form["Input.Model"] = "unapproved-model";
        using var result = await browser.PostAsync("/Inference?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.True(result.Headers.CacheControl?.NoStore);
        Assert.Null(result.Headers.Location);
        var html = await result.Content.ReadAsStringAsync();
        Assert.Contains(factory.Inference.EnrollmentToken, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(factory.Inference.EnrollmentToken, html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain(InferenceFactory.AdminKey, html);
        Assert.DoesNotContain(factory.Inference.EnrollmentToken, result.Headers.ToString());
        Assert.Contains("./Install-Agent.ps1 -BackendUrl 'https://api.arkya.online' -AgentId 'cpu-bureau'", WebUtility.HtmlDecode(html));
        var call = Assert.Single(factory.Inference.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal(5, call.Body!.Value.EnumerateObject().Count());
        Assert.Equal("<script>alert('x')</script>", call.Body.Value.GetProperty("displayName").GetString());
        var subsequent = await browser.GetStringAsync("/Inference");
        Assert.DoesNotContain(factory.Inference.EnrollmentToken, WebUtility.HtmlDecode(subsequent));
        Assert.DoesNotContain("id=\"agent-token\"", subsequent);
        Assert.DoesNotContain("<script>", subsequent);
    }

    [Fact]
    public async Task CreatedCredentialIsStillShownIfReloadingTheListFails()
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = CreateForm(await AdminFactory.Token(browser, "/Inference"));
        factory.Inference.FailListAfterCreate = true;
        var result = await browser.PostAsync("/Inference?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var html = await result.Content.ReadAsStringAsync();
        Assert.Contains(InferenceStub.Token, html);
        Assert.Contains("role=\"alert\"", html);
        Assert.DoesNotContain(InferenceFactory.AdminKey, html);
    }

    [Theory]
    [InlineData("Input.AgentId", "../other")]
    [InlineData("Input.AgentId", "UPPERCASE")]
    [InlineData("Input.DisplayName", "")]
    [InlineData("Input.DisplayName", "name\nline")]
    [InlineData("Input.InitialWorkers", "13")]
    [InlineData("Input.InitialWorkers", "7")]
    [InlineData("Input.ThreadsPerWorker", "25")]
    [InlineData("Input.MaxWorkers", "13")]
    public async Task InvalidCreationDoesNotReachTheWriteApi(string name, string value)
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = CreateForm(await AdminFactory.Token(browser, "/Inference"));
        form[name] = value;
        var result = await browser.PostAsync("/Inference?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("validation-summary-errors", await result.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Inference.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task TotalInitialThreadBudgetIsValidated()
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = CreateForm(await AdminFactory.Token(browser, "/Inference"));
        form["Input.InitialWorkers"] = "6";
        form["Input.ThreadsPerWorker"] = "24";
        var result = await browser.PostAsync("/Inference?handler=Create", AdminFactory.Form(form));
        Assert.Contains("96 threads", WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(factory.Inference.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task OfflineMachinesShowPendingCommandsAndEncodeTheirObservation()
    {
        await using var factory = new InferenceFactory();
        factory.Inference.Agent = factory.Inference.Agent with
        {
            Online = false, MachineName = "<script>machine</script>", LastError = "private upstream details " + InferenceFactory.AdminKey,
            DesiredWorkers = [new("worker-one", 4), new("worker-pending", 4)],
            ObservedWorkers = [new("worker-one", "ready", 1, 11501, 4, InferenceFactory.AdminKey),
                new("worker-removing", "ready", 2, 11502, 4, null)]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Inference");
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Hors ligne", decoded);
        Assert.Contains("Démarrage demandé", decoded);
        Assert.Contains("Arrêt demandé", decoded);
        Assert.Contains("2 observé(s) / 2 demandé(s)", decoded);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain(InferenceFactory.AdminKey, html);
        Assert.DoesNotContain("private upstream details", html);
        Assert.DoesNotContain("handler=DeleteAgent", html);
    }

    [Fact]
    public async Task WorkerFormsSendOnlyIdentityAndThreadCountThenRedirect()
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Inference");
        var add = await browser.PostAsync("/Inference?handler=AddWorker", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["agentId"] = InferenceStub.AgentId, ["threads"] = "5", ["model"] = "unapproved"
        }));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        var call = Assert.Single(factory.Inference.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal(5, call.Body!.Value.GetProperty("threads").GetInt32());
        Assert.Single(call.Body.Value.EnumerateObject());
        var remove = await browser.PostAsync("/Inference?handler=DeleteWorker", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["agentId"] = InferenceStub.AgentId, ["workerId"] = "worker-one"
        }));
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        var deletion = Assert.Single(factory.Inference.Calls, call => call.Method == HttpMethod.Delete);
        Assert.Equal("/admin/inference/agents/cpu-bureau/workers/worker-one", deletion.Path);
        Assert.Null(deletion.Body);
        var html = await browser.GetStringAsync("/Inference");
        Assert.Contains("Arrêt demandé", WebUtility.HtmlDecode(html));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("25")]
    [InlineData("invalid")]
    public async Task InvalidWorkerThreadCountNeverReachesTheService(string threads)
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Inference");
        var result = await browser.PostAsync("/Inference?handler=AddWorker", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["agentId"] = InferenceStub.AgentId, ["threads"] = threads
        }));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.DoesNotContain(factory.Inference.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task EmptyMachineCanBeRemovedButAConflictIsSafe()
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Inference");
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["agentId"] = InferenceStub.AgentId };
        var conflict = await browser.PostAsync("/Inference?handler=DeleteAgent", AdminFactory.Form(form));
        var html = WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync());
        Assert.Contains("une machine doit être vide", html);
        Assert.DoesNotContain(InferenceFactory.AdminKey, html);
        factory.Inference.Agent = factory.Inference.Agent with { DesiredWorkers = [], ObservedWorkers = [] };
        Assert.Contains("handler=DeleteAgent", await browser.GetStringAsync("/Inference"));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.PostAsync("/Inference?handler=DeleteAgent", AdminFactory.Form(form))).StatusCode);
        Assert.True(factory.Inference.Empty);
    }

    [Theory]
    [InlineData("InferenceApi:BaseUrl", "http://inference.example")]
    [InlineData("InferenceApi:BaseUrl", "https://token@inference.example")]
    [InlineData("InferenceApi:BaseUrl", "https://inference.example?token=secret")]
    [InlineData("InferenceApi:AdminKey", "short")]
    [InlineData("InferenceApi:CertificateAuthorityFile", "relative-ca.pem")]
    public void UnsafeClientConfigurationFailsAtStartup(string name, string value)
    {
        using var factory = new InferenceFactory();
        factory.Settings[name] = value;
        Assert.Throws<OptionsValidationException>(() => factory.Browser());
    }

    private static Dictionary<string, string> CreateForm(string token) => new()
    {
        ["__RequestVerificationToken"] = token, ["Input.AgentId"] = InferenceStub.AgentId,
        ["Input.DisplayName"] = "CPU Bureau", ["Input.InitialWorkers"] = "3",
        ["Input.ThreadsPerWorker"] = "4", ["Input.MaxWorkers"] = "6"
    };
}
