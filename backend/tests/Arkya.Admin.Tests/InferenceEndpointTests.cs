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
    [InlineData("PrepareInstallation")]
    [InlineData("AddWorker")]
    [InlineData("UpdateWorker")]
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
        Assert.Contains("Inference management is disabled", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("handler=Create", html);
        Assert.DoesNotContain("handler=PrepareInstallation", html);
        var form = CreateForm(AdminFactory.Input(html, "__RequestVerificationToken"));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync("/Inference?handler=Create", AdminFactory.Form(form))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync("/Inference?handler=PrepareInstallation", AdminFactory.Form(form))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync("/Inference?handler=UpdateWorker", AdminFactory.Form(form))).StatusCode);
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
        form["Input.ModelId"] = "qwen35-08b";
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
        Assert.Contains("./Install-Agent.ps1 -BackendUrl 'https://api.arkya.online' -AgentId 'cpu-bureau' -AgentToken (Read-Host 'Agent token' -AsSecureString)", WebUtility.HtmlDecode(html));
        var call = Assert.Single(factory.Inference.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal(6, call.Body!.Value.EnumerateObject().Count());
        Assert.Equal("<script>alert('x')</script>", call.Body.Value.GetProperty("displayName").GetString());
        Assert.Equal("qwen35-08b", call.Body.Value.GetProperty("modelId").GetString());
        Assert.All(factory.Inference.Agent.DesiredWorkers, worker => Assert.Equal("qwen35-08b", worker.ModelId));
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallationGrantIsOnlyDisplayedInItsNoStorePostWithoutCreatingAnAgent(bool reloadFails)
    {
        await using var factory = new InferenceFactory();
        factory.Inference.EnrollmentToken = "installation-<script>secret</script>&12345";
        factory.Inference.FailListAfterGrant = reloadFails;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Inference");
        var originalAgent = factory.Inference.Agent;
        using var result = await browser.PostAsync("/Inference?handler=PrepareInstallation", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["Input.DisplayName"] = "", ["Input.InitialWorkers"] = "invalid"
        }));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.True(result.Headers.CacheControl?.NoStore);
        Assert.Null(result.Headers.Location);
        var html = await result.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains(factory.Inference.EnrollmentToken, decoded);
        Assert.DoesNotContain(factory.Inference.EnrollmentToken, html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("name=\"installation-token\"", html);
        Assert.Contains("20/10/2026 at 12:15:00 UTC", decoded);
        Assert.Contains(".\\Install.cmd -BackendUrl https://api.arkya.online", decoded);
        Assert.DoesNotContain("-AgentId", decoded);
        Assert.DoesNotContain("validation-summary-errors", html);
        Assert.DoesNotContain(factory.Inference.EnrollmentToken, result.Headers.ToString());
        Assert.DoesNotContain(InferenceFactory.AdminKey, html);
        var issuance = Assert.Single(factory.Inference.Calls, call => call.Method != HttpMethod.Get);
        Assert.Equal(HttpMethod.Post, issuance.Method);
        Assert.Equal("/admin/inference/enrollment-grants", issuance.Path);
        Assert.Null(issuance.Body);
        Assert.Equal(originalAgent, factory.Inference.Agent);
        factory.Inference.ReadError = null;
        var subsequent = await browser.GetStringAsync("/Inference");
        Assert.DoesNotContain(factory.Inference.EnrollmentToken, WebUtility.HtmlDecode(subsequent));
        Assert.DoesNotContain("id=\"installation-token\"", subsequent);
    }

    [Fact]
    public async Task UpdateValidatesItsOwnFieldsAndShowsPendingModelAndThreadChanges()
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Inference");
        var result = await browser.PostAsync("/Inference?handler=UpdateWorker", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["agentId"] = InferenceStub.AgentId,
            ["workerId"] = "worker-one", ["threads"] = "8", ["modelId"] = "qwen35-08b",
            ["Input.DisplayName"] = "", ["Input.ThreadsPerWorker"] = "invalid", ["Input.ModelId"] = "unknown"
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var call = Assert.Single(factory.Inference.Calls, call => call.Method != HttpMethod.Get);
        Assert.Equal(HttpMethod.Patch, call.Method);
        Assert.Equal("/admin/inference/agents/cpu-bureau/workers/worker-one", call.Path);
        Assert.Equal(2, call.Body!.Value.EnumerateObject().Count());
        Assert.Equal(8, call.Body.Value.GetProperty("threads").GetInt32());
        Assert.Equal("qwen35-08b", call.Body.Value.GetProperty("modelId").GetString());
        var desired = Assert.Single(factory.Inference.Agent.DesiredWorkers);
        Assert.Equal("worker-one", desired.WorkerId);
        Assert.Equal("qwen35-08b", desired.ModelId);
        var html = await browser.GetStringAsync("/Inference");
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Restart requested", decoded);
        Assert.Contains("8 requested threads", decoded);
        Assert.Contains("4 observed threads", decoded);
        Assert.Contains("Qwen3.5", decoded);
        Assert.Contains("LFM2.5", decoded);
        factory.Inference.Agent = factory.Inference.Agent with
        {
            ObservedWorkers = [factory.Inference.Agent.ObservedWorkers[0] with { Threads = 8, ModelId = "qwen35-08b" }]
        };
        Assert.DoesNotContain("Restart requested", WebUtility.HtmlDecode(await browser.GetStringAsync("/Inference")));
    }

    [Theory]
    [InlineData("AddWorker", "modelId", "")]
    [InlineData("AddWorker", "modelId", "unknown")]
    [InlineData("UpdateWorker", "modelId", "unknown")]
    [InlineData("UpdateWorker", "threads", "0")]
    [InlineData("UpdateWorker", "threads", "25")]
    [InlineData("UpdateWorker", "threads", "invalid")]
    [InlineData("UpdateWorker", "workerId", "../elsewhere")]
    public async Task InvalidWorkerEditsNeverReachTheWriteApi(string handler, string name, string value)
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await AdminFactory.Token(browser, "/Inference"),
            ["agentId"] = InferenceStub.AgentId, ["workerId"] = "worker-one", ["threads"] = "4", ["modelId"] = "lfm"
        };
        form[name] = value;
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/Inference?handler=" + handler, AdminFactory.Form(form))).StatusCode);
        Assert.DoesNotContain(factory.Inference.Calls, call => call.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("AddWorker")]
    [InlineData("UpdateWorker")]
    public async Task UnavailableModelCatalogKeepsObservationsButPreventsModelWrites(string handler)
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Inference");
        factory.Inference.ModelsError = HttpStatusCode.ServiceUnavailable;
        var form = CreateForm(token);
        form["agentId"] = InferenceStub.AgentId;
        form["workerId"] = "worker-one";
        form["threads"] = "4";
        form["modelId"] = "lfm";
        var result = await browser.PostAsync("/Inference?handler=" + handler, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var html = await result.Content.ReadAsStringAsync();
        Assert.Contains("The model catalogue is unavailable", WebUtility.HtmlDecode(html));
        Assert.Contains("CPU Bureau", html);
        Assert.Contains("handler=DeleteWorker", html);
        Assert.Contains("name=\"modelId\" required disabled=\"disabled\"", html);
        Assert.DoesNotContain(factory.Inference.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task EmptyModelCatalogIsUnavailableAndModelLabelsAreHtmlEncoded()
    {
        await using var factory = new InferenceFactory();
        factory.Inference.Models = [new("lfm", "<script>catalogue</script>", "private-runtime-name")];
        factory.Inference.Agent = factory.Inference.Agent with
        {
            ObservedWorkers = [factory.Inference.Agent.ObservedWorkers[0] with { ModelId = "<script>observed</script>" }]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Inference");
        Assert.Contains("<script>catalogue</script>", WebUtility.HtmlDecode(html));
        Assert.Contains("<script>observed</script>", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script>", html);
        factory.Inference.Models = [];
        Assert.Contains("The model catalogue is unavailable", WebUtility.HtmlDecode(await browser.GetStringAsync("/Inference")));
    }

    [Theory]
    [InlineData("Create", HttpStatusCode.OK)]
    [InlineData("AddWorker", HttpStatusCode.BadRequest)]
    [InlineData("UpdateWorker", HttpStatusCode.BadRequest)]
    public async Task SelectedModelMustBelongToTheLiveServiceCatalog(string handler, HttpStatusCode expected)
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = CreateForm(await AdminFactory.Token(browser, "/Inference"));
        factory.Inference.Models = [new("lfm", "LFM uniquement", "lfm-runtime")];
        form["Input.ModelId"] = "qwen35-08b";
        form["agentId"] = InferenceStub.AgentId;
        form["workerId"] = "worker-one";
        form["threads"] = "4";
        form["modelId"] = "qwen35-08b";
        Assert.Equal(expected, (await browser.PostAsync("/Inference?handler=" + handler, AdminFactory.Form(form))).StatusCode);
        Assert.DoesNotContain(factory.Inference.Calls, call => call.Method != HttpMethod.Get);
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
    [InlineData("Input.ModelId", "unknown")]
    [InlineData("Input.ModelId", "")]
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
        Assert.Contains("Offline", decoded);
        Assert.Contains("Startup requested", decoded);
        Assert.Contains("Stop requested", decoded);
        Assert.Contains("2 observed / 2 requested", decoded);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain(InferenceFactory.AdminKey, html);
        Assert.DoesNotContain("private upstream details", html);
        Assert.DoesNotContain("handler=DeleteAgent", html);
    }

    [Fact]
    public async Task WorkerFormsSendSelectedModelAndThreadCountThenRedirect()
    {
        await using var factory = new InferenceFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Inference");
        var add = await browser.PostAsync("/Inference?handler=AddWorker", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["agentId"] = InferenceStub.AgentId, ["threads"] = "5", ["modelId"] = "qwen35-08b", ["model"] = "unapproved"
        }));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        var call = Assert.Single(factory.Inference.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal(5, call.Body!.Value.GetProperty("threads").GetInt32());
        Assert.Equal(2, call.Body.Value.EnumerateObject().Count());
        Assert.Equal("qwen35-08b", call.Body.Value.GetProperty("modelId").GetString());
        var remove = await browser.PostAsync("/Inference?handler=DeleteWorker", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["agentId"] = InferenceStub.AgentId, ["workerId"] = "worker-one"
        }));
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        var deletion = Assert.Single(factory.Inference.Calls, call => call.Method == HttpMethod.Delete);
        Assert.Equal("/admin/inference/agents/cpu-bureau/workers/worker-one", deletion.Path);
        Assert.Null(deletion.Body);
        var html = await browser.GetStringAsync("/Inference");
        Assert.Contains("Stop requested", WebUtility.HtmlDecode(html));
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
        Assert.Contains("a machine must be empty", html);
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
        ["Input.ThreadsPerWorker"] = "4", ["Input.MaxWorkers"] = "6", ["Input.ModelId"] = "lfm"
    };
}
