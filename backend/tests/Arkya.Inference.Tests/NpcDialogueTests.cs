using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Arkya.ServiceDefaults.Inference;

namespace Arkya.Inference.Tests;

public sealed class NpcDialogueTests
{
    private const string ServiceKey = "npc-dialogue-dedicated-test-key-12345678";
    private const string Route = "/internal/inference/dialogues";
    private const string ClaimRoute = "/agent/inference/dialogues/claim";
    private const string CompleteRoute = "/agent/inference/dialogues/complete";

    [Fact]
    public async Task DedicatedDialogueTraversesAuthenticatedWorkerAndReturnsOnlyReply()
    {
        using var factory = Enabled();
        using var dedicated = Dedicated(factory);
        var machine = await Machine(factory);
        using var agent = machine.Client;
        var request = Request();
        var accepted = await dedicated.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("pending", (await accepted.Content.ReadFromJsonAsync<NpcDialogueResponse>())!.Status);
        var job = (await Claim(agent, machine.Agent))!;
        Assert.Equal(request, job.Request);
        Assert.Equal(factory.Clock.Now.AddSeconds(45), job.ExpiresAt);
        Assert.Equal(job, await Claim(agent, machine.Agent));
        var complete = Completion(machine.Agent, job);
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync(CompleteRoute, complete)).StatusCode);
        // Completion and request retries preserve the first answer.
        var replay = await agent.PostAsJsonAsync(CompleteRoute, complete with { Reply = "Replacement rejected" });
        Assert.Equal(complete.Reply, (await replay.Content.ReadFromJsonAsync<NpcDialogueResponse>())!.Reply);
        var result = (await dedicated.GetFromJsonAsync<NpcDialogueResponse>(Route + "/" + request.RequestId))!;
        Assert.Equal(new(request.RequestId, "completed", complete.Reply, null), result);
        Assert.Equal(HttpStatusCode.OK, (await dedicated.PostAsJsonAsync(Route, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await dedicated.PostAsJsonAsync(Route,
            request with { PlayerMessage = "Changed request" })).StatusCode);
        Assert.Null(await Claim(agent, machine.Agent));
    }

    [Fact]
    public async Task EachWorkerReceivesOneExclusiveLeaseDuringConcurrentClaims()
    {
        using var factory = Enabled();
        using var dedicated = Dedicated(factory);
        var one = await Machine(factory, "cpu-one");
        var two = await Machine(factory, "cpu-two");
        using var first = one.Client;
        using var second = two.Client;
        await dedicated.PostAsJsonAsync(Route, Request());
        await dedicated.PostAsJsonAsync(Route, Request());
        var claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            i % 2 == 0 ? Claim(first, one.Agent) : Claim(second, two.Agent)));
        Assert.Equal(2, claims.Select(job => job!.RequestId).Distinct().Count());
        Assert.Single(claims.Where((_, i) => i % 2 == 0).Select(job => job!.LeaseToken).Distinct());
        Assert.Single(claims.Where((_, i) => i % 2 != 0).Select(job => job!.LeaseToken).Distinct());
        var wrong = Completion(two.Agent, claims[0]!);
        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync(CompleteRoute, wrong)).StatusCode);
    }

    [Fact]
    public async Task QueueCapacityExpiryAndLateCompletionStayBounded()
    {
        using var factory = Enabled();
        factory.Settings["NpcDialogue:Capacity"] = "1";
        using var dedicated = Dedicated(factory);
        var machine = await Machine(factory);
        using var agent = machine.Client;
        var request = Request();
        await dedicated.PostAsJsonAsync(Route, request);
        var job = (await Claim(agent, machine.Agent))!;
        Assert.Equal(HttpStatusCode.TooManyRequests, (await dedicated.PostAsJsonAsync(Route, Request())).StatusCode);
        factory.Clock.Now += TimeSpan.FromSeconds(46);
        var result = (await dedicated.GetFromJsonAsync<NpcDialogueResponse>(Route + "/" + request.RequestId))!;
        Assert.Equal("failed", result.Status);
        Assert.Equal("inference_timeout", result.Error);
        var late = await agent.PostAsJsonAsync(CompleteRoute, Completion(machine.Agent, job));
        Assert.Equal(result, await late.Content.ReadFromJsonAsync<NpcDialogueResponse>());
        factory.Clock.Now += TimeSpan.FromSeconds(75);
        Assert.Equal(HttpStatusCode.NotFound, (await dedicated.GetAsync(Route + "/" + request.RequestId)).StatusCode);
        await Heartbeat(agent, machine.Agent);
        Assert.Equal(HttpStatusCode.Accepted, (await dedicated.PostAsJsonAsync(Route, Request())).StatusCode);
    }

    [Fact]
    public async Task NoRecentCompatibleWorkerReturnsImmediateFallbackSignal()
    {
        using var factory = Enabled();
        using var dedicated = Dedicated(factory);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await dedicated.PostAsJsonAsync(Route, Request())).StatusCode);
        var machine = await Machine(factory);
        using var agent = machine.Client;
        var old = Fixtures.Heartbeat(machine.Agent.AgentId, Fixtures.Observe(machine.Agent.DesiredWorkers[0])) with { Version = "1.1.1" };
        await agent.PostAsJsonAsync("/agent/inference/heartbeat", old);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await dedicated.PostAsJsonAsync(Route, Request())).StatusCode);
        await Heartbeat(agent, machine.Agent);
        factory.Clock.Now += TimeSpan.FromSeconds(31);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await dedicated.PostAsJsonAsync(Route, Request())).StatusCode);
    }

    [Fact]
    public async Task DedicatedAndMachineCredentialsCannotBeSubstituted()
    {
        using var factory = Enabled();
        using var admin = factory.Client();
        using var dedicated = Dedicated(factory);
        var machine = await Machine(factory);
        using var agent = machine.Client;
        var request = Request();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(Route, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync(Route, request)).StatusCode);
        var claim = new ClaimNpcDialogueRequest(machine.Agent.AgentId, machine.Agent.DesiredWorkers[0].WorkerId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await dedicated.PostAsJsonAsync(ClaimRoute, claim)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync(ClaimRoute,
            claim with { WorkerId = Guid.NewGuid().ToString("N") })).StatusCode);
        await dedicated.PostAsJsonAsync(Route, request);
        var job = (await Claim(agent, machine.Agent))!;
        var entry = (await factory.Repository.FindAsync(machine.Agent.AgentId, default))!;
        await factory.Repository.TryDeleteAsync(entry.AgentId, entry.Version, entry.TokenHash, default);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsJsonAsync(CompleteRoute,
            Completion(machine.Agent, job))).StatusCode);
    }

    [Fact]
    public async Task BoundsAndUnexpectedModelActionsAreRejected()
    {
        using var factory = Enabled();
        using var dedicated = Dedicated(factory);
        var machine = await Machine(factory);
        using var agent = machine.Client;
        var request = Request();
        foreach (var invalid in new[] { request with { PlayerMessage = new string('x', 257) },
            request with { NpcId = "../other" }, request with { Personality = new string('x', 801) },
            request with { QuestContext = null! }, request with { DisplayName = "unsafe\nname" },
            request with { RequestId = Guid.NewGuid().ToString() } })
            Assert.Equal(HttpStatusCode.BadRequest, (await dedicated.PostAsJsonAsync(Route, invalid)).StatusCode);
        await dedicated.PostAsJsonAsync(Route, request);
        var completion = Completion(machine.Agent, (await Claim(agent, machine.Agent))!);
        foreach (var invalid in new[] { completion with { Reply = new string('x', 513) },
            completion with { Reply = "\0bad" }, completion with { Reply = "" },
            completion with { Reply = null, Error = "grant_gold" }, completion with { Error = "inference_failed" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync(CompleteRoute, invalid)).StatusCode);
        var extra = new { completion.AgentId, completion.WorkerId, completion.RequestId,
            completion.LeaseToken, completion.Reply, completion.Error, grantGold = 10 };
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.PostAsJsonAsync(CompleteRoute, extra)).StatusCode);
        var failed = await agent.PostAsJsonAsync(CompleteRoute, completion with { Reply = null, Error = "inference_failed" });
        Assert.Equal("failed", (await failed.Content.ReadFromJsonAsync<NpcDialogueResponse>())!.Status);
    }

    [Fact]
    public async Task DisabledDialogueKeepsExistingAgentsCompatible()
    {
        using var factory = new InferenceFactory();
        using var dedicated = Dedicated(factory);
        var machine = await Machine(factory);
        using var agent = machine.Client;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await dedicated.PostAsJsonAsync(Route, Request())).StatusCode);
        Assert.Null(await Claim(agent, machine.Agent));
    }

    private static InferenceFactory Enabled()
    {
        var factory = new InferenceFactory();
        factory.Settings["NpcDialogue:Enabled"] = "true";
        factory.Settings["NpcDialogue:ServiceKey"] = ServiceKey;
        return factory;
    }

    private static HttpClient Dedicated(InferenceFactory factory)
    {
        var client = factory.Client(null);
        client.DefaultRequestHeaders.Add("X-Arkya-Service-Key", ServiceKey);
        return client;
    }

    private static async Task<(HttpClient Client, InferenceAgentSummary Agent)> Machine(InferenceFactory factory, string id = "cpu-one")
    {
        using var admin = factory.Client();
        var created = await admin.PostAsJsonAsync("/admin/inference/agents", Fixtures.Create(id, workers: 1));
        var data = (await created.Content.ReadFromJsonAsync<CreateInferenceAgentResponse>())!;
        var client = factory.Client(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.AgentToken);
        await Heartbeat(client, data.Agent);
        return (client, data.Agent);
    }

    private static async Task Heartbeat(HttpClient client, InferenceAgentSummary agent) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/agent/inference/heartbeat",
            Fixtures.Heartbeat(agent.AgentId, Fixtures.Observe(agent.DesiredWorkers[0])) with
                { Version = "1.2.0", AppliedRevision = agent.Revision })).StatusCode);

    private static async Task<NpcDialogueWork?> Claim(HttpClient client, InferenceAgentSummary agent)
    {
        var response = await client.PostAsJsonAsync(ClaimRoute, new ClaimNpcDialogueRequest(agent.AgentId, agent.DesiredWorkers[0].WorkerId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimNpcDialogueResponse>())!.Job;
    }

    private static NpcDialogueRequest Request() => new(Guid.NewGuid().ToString("N"), "aubefont-guard", "Aline",
        "Garde", "Une garde attentive à Aubefont.", "Bonjour !", "");
    private static CompleteNpcDialogueRequest Completion(InferenceAgentSummary agent, NpcDialogueWork job) =>
        new(agent.AgentId, agent.DesiredWorkers[0].WorkerId, job.RequestId, job.LeaseToken, "Bienvenue à Aubefont.", null);
}
