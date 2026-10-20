using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Arkya.Inference.Api.Endpoints;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Tests;

public sealed class InferenceEndpointTests
{
    private const string Agents = "/admin/inference/agents";
    private const string Heartbeat = "/agent/inference/heartbeat";

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    [InlineData("inference-administrator-test-key-123456780")]
    public async Task AllOperatorRoutesRequireTheCorrectAdminKey(string? key)
    {
        using var factory = new InferenceFactory();
        using var client = factory.Client(key);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Agents)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Agents + "/cpu-one")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Agents, Fixtures.Create())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Agents + "/cpu-one/workers", new AddInferenceWorkerRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(Agents + "/cpu-one/workers/" + Guid.NewGuid().ToString("N"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(Agents + "/cpu-one")).StatusCode);
        Assert.Empty(await factory.Repository.ListAsync(default));
    }

    [Fact]
    public async Task CreatesThreeWorkersAndReturnsCredentialOnlyOnce()
    {
        using var factory = new InferenceFactory();
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync(Agents, new { agentId = "cpu-one", displayName = "Machine CPU" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var created = (await response.Content.ReadFromJsonAsync<CreateInferenceAgentResponse>())!;
        Assert.Equal(3, created.Agent.DesiredWorkers.Length);
        Assert.Equal(3, created.Agent.DesiredWorkers.Select(value => value.WorkerId).Distinct().Count());
        Assert.All(created.Agent.DesiredWorkers, worker => Assert.Equal(4, worker.Threads));
        Assert.False(created.Agent.Online);
        Assert.Equal(64, created.AgentToken.Length);
        var stored = (await factory.Repository.FindAsync("cpu-one", default))!;
        Assert.Equal(AgentCredentials.Hash(created.AgentToken), stored.TokenHash);
        Assert.NotEqual(created.AgentToken, stored.TokenHash);
        foreach (var path in new[] { Agents, Agents + "/cpu-one" })
        {
            var body = await client.GetStringAsync(path);
            Assert.DoesNotContain(created.AgentToken, body);
            Assert.DoesNotContain(stored.TokenHash, body);
            Assert.DoesNotContain("agentToken", body);
            Assert.DoesNotContain("tokenHash", body);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Agents, Fixtures.Create())).StatusCode);
        Assert.Single(await factory.Repository.ListAsync(default));
    }

    [Theory]
    [InlineData("CPU", 3, 4, 6)]
    [InlineData("cpu-one\n", 3, 4, 6)]
    [InlineData("cpu-one-", 3, 4, 6)]
    [InlineData("1cpu", 3, 4, 6)]
    [InlineData("ab", 3, 4, 6)]
    [InlineData("cpu-one", -1, 4, 6)]
    [InlineData("cpu-one", 7, 4, 6)]
    [InlineData("cpu-one", 3, 0, 6)]
    [InlineData("cpu-one", 3, 25, 6)]
    [InlineData("cpu-one", 3, 4, 13)]
    [InlineData("cpu-one", 5, 24, 6)]
    [InlineData("cpu-one", int.MaxValue, int.MaxValue, int.MaxValue)]
    public async Task RejectsInvalidIdentityAndResourceLimits(string id, int workers, int threads, int maxWorkers)
    {
        using var factory = new InferenceFactory();
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync(Agents, new CreateInferenceAgentRequest(id, "CPU", workers, threads, maxWorkers));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await factory.Repository.ListAsync(default));
    }

    [Fact]
    public async Task RejectsUnknownFieldsMissingValuesControlCharactersAndOversizedBodies()
    {
        using var factory = new InferenceFactory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Agents, new
            { agentId = "cpu-one", displayName = "CPU", tokenHash = "client-controlled" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Agents, new { agentId = "cpu-one" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Agents, Fixtures.Create() with { DisplayName = "CPU\none" })).StatusCode);
        using var oversized = new StringContent(new string('x', 16385), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync(Agents, oversized)).StatusCode);
        Assert.Empty(await factory.Repository.ListAsync(default));
    }

    [Fact]
    public async Task HeartbeatUsesMachineCredentialAndNeverAuthorizesOperatorRoutes()
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        var first = await Create(admin);
        var second = await Create(admin, "cpu-two");
        using var agent = factory.Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one"))).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new("Basic", first.AgentToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one"))).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new("Bearer", second.AgentToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one"))).StatusCode);
        agent.DefaultRequestHeaders.Authorization = new("Bearer", first.AgentToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync(Agents)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one"))).StatusCode);
        Assert.Null((await factory.Repository.FindAsync("cpu-two", default))!.LastSeenAt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-two"))).StatusCode);
    }

    [Fact]
    public async Task HeartbeatUpdatesObservationsWithoutChangingDesiredRevisionAndOnlineExpires()
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        var created = await Create(admin);
        using var agent = AgentClient(factory, created);
        var observed = Fixtures.Observe(created.Agent.DesiredWorkers[0]) with { LastError = "credential " + created.AgentToken };
        var request = Fixtures.Heartbeat("cpu-one", observed) with
            { LastError = created.AgentToken, AppliedRevision = created.Agent.Revision };
        var response = await agent.PostAsJsonAsync(Heartbeat, request);
        var desired = (await response.Content.ReadFromJsonAsync<InferenceHeartbeatResponse>())!;
        Assert.Equal(1, desired.Revision);
        Assert.Equal(5, desired.HeartbeatSeconds);
        Assert.Equal(created.Agent.DesiredWorkers, desired.Workers);
        var summary = (await admin.GetFromJsonAsync<InferenceAgentSummary>(Agents + "/cpu-one"))!;
        Assert.True(summary.Online);
        Assert.Equal(24, summary.LogicalProcessors);
        Assert.Equal(131072, summary.TotalMemoryMb);
        Assert.Single(summary.ObservedWorkers);
        Assert.Equal("[redacted]", summary.LastError);
        Assert.Equal("credential [redacted]", summary.ObservedWorkers[0].LastError);
        Assert.Equal(2, (await factory.Repository.FindAsync("cpu-one", default))!.Version);
        Assert.Equal(created.Agent.Revision, (await factory.Repository.FindAsync("cpu-one", default))!.ObservedRevision);
        factory.Clock.Now += TimeSpan.FromSeconds(31);
        Assert.False((await admin.GetFromJsonAsync<InferenceAgentSummary>(Agents + "/cpu-one"))!.Online);
    }

    [Fact]
    public async Task RejectsForeignDuplicateOrUnboundedWorkerObservationsWithoutUpdatingLastSeen()
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        var created = await Create(admin);
        var other = await Create(admin, "cpu-two");
        using var agent = AgentClient(factory, created);
        var own = Fixtures.Observe(created.Agent.DesiredWorkers[0]);
        var invalid = new[]
        {
            Fixtures.Heartbeat("cpu-one", Fixtures.Observe(other.Agent.DesiredWorkers[0])),
            Fixtures.Heartbeat("cpu-one", own, own),
            Fixtures.Heartbeat("cpu-one", own with { State = "arbitrary-command" }),
            Fixtures.Heartbeat("cpu-one", own with { Port = 70000 }),
            Fixtures.Heartbeat("cpu-one", own with { Pid = -1 }),
            Fixtures.Heartbeat("cpu-one", own with { Threads = 25 }),
            Fixtures.Heartbeat("cpu-one", own with { LastError = new string('x', 513) }),
            Fixtures.Heartbeat("cpu-one", own, Fixtures.Observe(created.Agent.DesiredWorkers[1])),
            Fixtures.Heartbeat("cpu-one") with { Workers = null! },
            Fixtures.Heartbeat("cpu-one") with { LogicalProcessors = 0 },
            Fixtures.Heartbeat("cpu-one") with { TotalMemoryMb = -1 },
            Fixtures.Heartbeat("cpu-one") with { AppliedRevision = -1 },
            Fixtures.Heartbeat("cpu-one") with { AppliedRevision = created.Agent.Revision + 1 },
            Fixtures.Heartbeat("cpu-one") with { MachineName = "bad\nmachine" },
            Fixtures.Heartbeat("cpu-one") with { Version = new string('x', 65) }
        };
        foreach (var request in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync(Heartbeat, request)).StatusCode);
        Assert.Null((await factory.Repository.FindAsync("cpu-one", default))!.LastSeenAt);
    }

    [Fact]
    public async Task RemovingWorkerIsIdempotentAndCapacityIsReleasedOnlyAfterObservedStop()
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        var created = await Create(admin, maxWorkers: 3);
        using var agent = AgentClient(factory, created);
        var worker = created.Agent.DesiredWorkers[0];
        var path = Agents + "/cpu-one/workers/" + worker.WorkerId;
        var first = (await (await admin.DeleteAsync(path)).Content.ReadFromJsonAsync<InferenceAgentSummary>())!;
        var retry = (await (await admin.DeleteAsync(path)).Content.ReadFromJsonAsync<InferenceAgentSummary>())!;
        Assert.Equal(2, first.Revision);
        Assert.Equal(first.Revision, retry.Revision);
        Assert.Equal(2, retry.DesiredWorkers.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Agents + "/cpu-one/workers", new AddInferenceWorkerRequest())).StatusCode);
        var response = await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one", Fixtures.Observe(worker)));
        var desired = (await response.Content.ReadFromJsonAsync<InferenceHeartbeatResponse>())!;
        Assert.DoesNotContain(desired.Workers, value => value.WorkerId == worker.WorkerId);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Agents + "/cpu-one/workers", new AddInferenceWorkerRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync(Heartbeat,
            Fixtures.Heartbeat("cpu-one") with { AppliedRevision = desired.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(Agents + "/cpu-one/workers", new AddInferenceWorkerRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one", Fixtures.Observe(worker)))).StatusCode);
    }

    [Fact]
    public async Task AgentRemovalRequiresFreshDrainAndRevokesTokenPermanently()
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        var created = await Create(admin);
        using var agent = AgentClient(factory, created);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);
        await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one") with { AppliedRevision = created.Agent.Revision });
        foreach (var worker in created.Agent.DesiredWorkers)
            Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync(Agents + "/cpu-one/workers/" + worker.WorkerId)).StatusCode);
        // Equal clock timestamps still cannot substitute for a heartbeat at the new revision.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);
        var drainedRevision = (await admin.GetFromJsonAsync<InferenceAgentSummary>(Agents + "/cpu-one"))!.Revision;
        await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one") with { AppliedRevision = drainedRevision });
        factory.Clock.Now += TimeSpan.FromSeconds(31);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);
        await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one") with { AppliedRevision = drainedRevision });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one"))).StatusCode);
        var replacement = await Create(admin);
        Assert.NotEqual(created.AgentToken, replacement.AgentToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync(Heartbeat, Fixtures.Heartbeat("cpu-one"))).StatusCode);
    }

    [Fact]
    public async Task OldEmptyHeartbeatCannotAcknowledgeRemovalOrForgetWorkersStillUsingOldConfiguration()
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        var created = await Create(admin, maxWorkers: 3);
        using var agent = AgentClient(factory, created);
        foreach (var worker in created.Agent.DesiredWorkers)
            await admin.DeleteAsync(Agents + "/cpu-one/workers/" + worker.WorkerId);

        var oldEmpty = Fixtures.Heartbeat("cpu-one") with { AppliedRevision = created.Agent.Revision };
        var response = await agent.PostAsJsonAsync(Heartbeat, oldEmpty);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var desired = (await response.Content.ReadFromJsonAsync<InferenceHeartbeatResponse>())!;
        Assert.Empty(desired.Workers);
        Assert.True(desired.Revision > oldEmpty.AppliedRevision);
        var current = (await factory.Repository.FindAsync("cpu-one", default))!;
        Assert.Equal(oldEmpty.AppliedRevision, current.ObservedRevision);
        Assert.Equal(3, current.RetiredWorkers.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync(Agents + "/cpu-one/workers", new AddInferenceWorkerRequest())).StatusCode);

        var stillRunning = Fixtures.Heartbeat("cpu-one", Fixtures.Observe(created.Agent.DesiredWorkers[0])) with
            { AppliedRevision = created.Agent.Revision };
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync(Heartbeat, stillRunning)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);
        // Acknowledging the revision alone does not prove that its processes have stopped.
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync(Heartbeat,
            stillRunning with { AppliedRevision = desired.Revision })).StatusCode);
        Assert.Single((await factory.Repository.FindAsync("cpu-one", default))!.RetiredWorkers);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync(Heartbeat,
            Fixtures.Heartbeat("cpu-one") with { AppliedRevision = desired.Revision })).StatusCode);
        Assert.Empty((await factory.Repository.FindAsync("cpu-one", default))!.RetiredWorkers);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Agents + "/cpu-one")).StatusCode);
    }

    [Fact]
    public async Task ProductionRequiresHttpsAndReadinessReportsStoreAvailability()
    {
        using var factory = new InferenceFactory(production: true);
        using var insecure = factory.Client(scheme: "http");
        insecure.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.BadRequest, (await insecure.GetAsync(Agents)).StatusCode);
        using var secure = factory.Client();
        Assert.Equal(HttpStatusCode.OK, (await secure.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secure.GetAsync("/health/ready")).StatusCode);
        factory.Repository.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await secure.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public void MissingAdminCredentialFailsStartup()
    {
        using var factory = new InferenceFactory();
        factory.Settings["InferenceAdmin:Key"] = "";
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }

    private static async Task<CreateInferenceAgentResponse> Create(HttpClient client, string id = "cpu-one", int maxWorkers = 6)
    {
        var response = await client.PostAsJsonAsync(Agents, Fixtures.Create(id, maxWorkers: maxWorkers));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateInferenceAgentResponse>())!;
    }

    private static HttpClient AgentClient(InferenceFactory factory, CreateInferenceAgentResponse created)
    {
        var client = factory.Client(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.AgentToken);
        return client;
    }
}
