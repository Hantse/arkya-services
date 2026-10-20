using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Admin.Inference;
using Arkya.ServiceDefaults.Inference;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Admin.Tests;

internal sealed record InferenceCall(HttpMethod Method, string Path, JsonElement? Body);

internal sealed class InferenceStub : HttpMessageHandler
{
    public const string AgentId = "cpu-bureau";
    public const string Token = "agent-enrollment-token-displayed-once-12345";
    public InferenceAgentSummary Agent { get; set; } = new(1, AgentId, "CPU Bureau", 1, 6, 4,
        DateTimeOffset.Parse("2026-10-20T10:00:00Z"), DateTimeOffset.Parse("2026-10-20T11:00:00Z"),
        true, "workstation", "1.0", 16, 32768, 8767, null,
        [new("worker-one", 4)], [new("worker-one", "ready", 1234, 11501, 4, null)]);
    public List<InferenceCall> Calls { get; } = [];
    public HttpStatusCode? ReadError { get; set; }
    public HttpStatusCode? ModelsError { get; set; }
    public HttpStatusCode? WriteError { get; set; }
    public bool InvalidJson { get; set; }
    public bool FailListAfterCreate { get; set; }
    public bool FailListAfterGrant { get; set; }
    public bool Empty { get; set; }
    public string EnrollmentToken { get; set; } = Token;
    public InferenceModelOption[] Models { get; set; } = [.. InferenceModels.Catalog];
    public DateTimeOffset GrantExpiresAt { get; set; } = DateTimeOffset.Parse("2026-10-20T12:15:00Z");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(InferenceFactory.AdminKey, Assert.Single(request.Headers.GetValues("X-Arkya-Admin-Key")));
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("X-Arkya-Service-Key"));
        var body = request.Content is null ? (JsonElement?)null
            : await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        Calls.Add(new(request.Method, path, body));
        if ((request.Method == HttpMethod.Get ? ReadError : WriteError) is { } error)
            return new(error) { Content = new StringContent("private upstream details " + InferenceFactory.AdminKey + " " + Token) };
        if (InvalidJson) return new(HttpStatusCode.OK) { Content = new StringContent("{ invalid " + InferenceFactory.AdminKey) };
        if (request.Method == HttpMethod.Get && path == "/admin/inference/models")
            return ModelsError is { } modelsError ? new(modelsError) : Json(Models);
        if (request.Method == HttpMethod.Post && path == "/admin/inference/enrollment-grants")
        {
            if (FailListAfterGrant) ReadError = HttpStatusCode.ServiceUnavailable;
            return Json(new InferenceEnrollmentGrantResponse(EnrollmentToken, GrantExpiresAt));
        }
        if (request.Method == HttpMethod.Get && path == "/admin/inference/agents")
            return Json(Empty ? Array.Empty<InferenceAgentSummary>() : [Agent]);
        if (request.Method == HttpMethod.Get && path == "/admin/inference/agents/" + Agent.AgentId)
            return Json(Agent);
        if (request.Method == HttpMethod.Post && path == "/admin/inference/agents")
        {
            var created = body!.Value.Deserialize<CreateInferenceAgentRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Agent = Agent with
            {
                AgentId = created.AgentId, DisplayName = created.DisplayName, MaxWorkers = created.MaxWorkers,
                ThreadsPerWorker = created.ThreadsPerWorker, LastSeenAt = null, Online = false,
                MachineName = null, LogicalProcessors = null, TotalMemoryMb = null, ObservedWorkers = [],
                DesiredWorkers = Enumerable.Range(1, created.InitialWorkers)
                    .Select(index => new InferenceWorkerDesired("worker-" + index, created.ThreadsPerWorker, created.ModelId)).ToArray()
            };
            Empty = false;
            if (FailListAfterCreate) ReadError = HttpStatusCode.ServiceUnavailable;
            return Json(new CreateInferenceAgentResponse(Agent, EnrollmentToken));
        }
        if (request.Method == HttpMethod.Post && path == "/admin/inference/agents/" + Agent.AgentId + "/workers")
        {
            var added = body!.Value.Deserialize<AddInferenceWorkerRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Agent = Agent with { DesiredWorkers = [.. Agent.DesiredWorkers, new("worker-added", added.Threads, added.ModelId)] };
            return new(HttpStatusCode.NoContent);
        }
        if (request.Method == HttpMethod.Patch && path.StartsWith("/admin/inference/agents/" + Agent.AgentId + "/workers/", StringComparison.Ordinal))
        {
            var workerId = Uri.UnescapeDataString(path.Split('/')[^1]);
            var update = body!.Value.Deserialize<UpdateInferenceWorkerRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Agent = Agent with { DesiredWorkers = Agent.DesiredWorkers.Select(worker => worker.WorkerId == workerId
                ? worker with { Threads = update.Threads, ModelId = update.ModelId } : worker).ToArray() };
            return Json(Agent);
        }
        if (request.Method == HttpMethod.Delete && path.StartsWith("/admin/inference/agents/" + Agent.AgentId + "/workers/", StringComparison.Ordinal))
        {
            var workerId = Uri.UnescapeDataString(path.Split('/')[^1]);
            Agent = Agent with { DesiredWorkers = Agent.DesiredWorkers.Where(worker => worker.WorkerId != workerId).ToArray() };
            return Json(Agent);
        }
        if (request.Method == HttpMethod.Delete && path == "/admin/inference/agents/" + Agent.AgentId)
        {
            if (Agent.DesiredWorkers.Length != 0 || Agent.ObservedWorkers.Length != 0) return new(HttpStatusCode.Conflict);
            Empty = true;
            return new(HttpStatusCode.NoContent);
        }
        return new(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

internal sealed class InferenceFactory : WebApplicationFactory<Arkya.Admin.Program>
{
    public const string AdminKey = "inference-operator-test-key-1234567890";
    public InferenceStub Inference { get; } = new();
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
                ["ContentApi:Enabled"] = "false",
                ["RealmsApi:Enabled"] = "false",
                ["ReportsApi:Enabled"] = "false",
                ["AdminMcp:Enabled"] = "false",
                ["InferenceApi:Enabled"] = "true",
                ["InferenceApi:BaseUrl"] = "https://inference.example/",
                ["InferenceApi:AdminKey"] = AdminKey
            });
            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureServices(services =>
            services.AddHttpClient<InferenceAdminClient>().ConfigurePrimaryHttpMessageHandler(() => Inference));
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    });
}
