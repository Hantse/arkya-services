using System.Net;
using System.Net.Http.Json;
using Arkya.Inference.Api.Application;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Tests;

public sealed class EnrollmentTests
{
    private const string Enroll = "/agent/inference/enroll";
    private const string GrantPath = "/admin/inference/enrollment-grants";

    [Fact]
    public async Task AdminIssuesThirtyMinuteGrantAndMachineRegistersWithoutAnAdminCredential()
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        using var device = factory.Client(null);
        Assert.Equal(HttpStatusCode.Forbidden, (await device.PostAsync(GrantPath, null)).StatusCode);
        var grant = (await (await admin.PostAsync(GrantPath, null)).Content.ReadFromJsonAsync<InferenceEnrollmentGrantResponse>())!;
        Assert.Equal(factory.Clock.Now.AddMinutes(30), grant.ExpiresAt);
        var record = (await factory.Grants.FindAsync(AgentCredentials.Hash(grant.Token), default))!;
        Assert.Null(record.BindingAgentId);
        var request = Register();
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.PostAsJsonAsync(Enroll, request)).StatusCode);
        device.DefaultRequestHeaders.Authorization = new("Basic", grant.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.PostAsJsonAsync(Enroll, request)).StatusCode);
        device.DefaultRequestHeaders.Authorization = new("Bearer", grant.Token);
        var response = await device.PostAsJsonAsync(Enroll, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(request.AgentToken, body);
        Assert.DoesNotContain(grant.Token, body);
        Assert.DoesNotContain("tokenHash", body);
        var summary = (await response.Content.ReadFromJsonAsync<InferenceAgentSummary>())!;
        Assert.Equal(3, summary.DesiredWorkers.Length);
        Assert.All(summary.DesiredWorkers, value => Assert.Equal("lfm", value.ModelId));
        var stored = (await factory.Repository.FindAsync(request.AgentId, default))!;
        Assert.Equal(AgentCredentials.Hash(request.AgentToken), stored.TokenHash);
        record = (await factory.Grants.FindAsync(AgentCredentials.Hash(grant.Token), default))!;
        Assert.Equal(request.AgentId, record.BindingAgentId);
        Assert.Equal(stored.TokenHash, record.BindingTokenHash);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await device.PostAsJsonAsync("/agent/inference/heartbeat", Fixtures.Heartbeat(request.AgentId))).StatusCode);
        device.DefaultRequestHeaders.Authorization = new("Bearer", request.AgentToken);
        Assert.Equal(HttpStatusCode.OK,
            (await device.PostAsJsonAsync("/agent/inference/heartbeat", Fixtures.Heartbeat(request.AgentId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync("/admin/inference/models")).StatusCode);
    }

    [Fact]
    public async Task EnrollmentRetryUsesDeviceCredentialWithoutGrantAndNeverReplacesOperatorChanges()
    {
        var (service, inference, agents, _, clock) = Services();
        var grant = await service.CreateGrantAsync(default);
        var request = Register();
        var original = (await service.RegisterAsync(request, grant.Token, default)).Value!;
        await inference.UpdateWorkerAsync(request.AgentId, original.DesiredWorkers[0].WorkerId, new(2, "qwen35-08b"), default);
        clock.Now = grant.ExpiresAt.AddDays(1);
        var retry = await service.RegisterAsync(request with { DisplayName = "Changed", InitialWorkers = 0, ModelId = "qwen35-08b" }, null, default);
        Assert.Null(retry.Error);
        Assert.Equal(original.DisplayName, retry.Value!.DisplayName);
        Assert.Equal(2, retry.Value.Revision);
        Assert.Equal(3, retry.Value.DesiredWorkers.Length);
        Assert.Equal("qwen35-08b", retry.Value.DesiredWorkers[0].ModelId);
        Assert.Equal(2, retry.Value.DesiredWorkers[0].Threads);
        Assert.Equal("agent_exists", (await service.RegisterAsync(request with { AgentToken = AgentCredentials.Create() }, grant.Token, default)).Error);
        Assert.Equal(AgentCredentials.Hash(request.AgentToken), (await agents.FindAsync(request.AgentId, default))!.TokenHash);
    }

    [Fact]
    public async Task EnrollmentCredentialsAreNotPersistedInTheMachineLabel()
    {
        var (service, _, agents, _, _) = Services();
        var grant = await service.CreateGrantAsync(default);
        var request = Register();
        var result = await service.RegisterAsync(request with { DisplayName = request.AgentToken }, grant.Token, default);
        Assert.Equal("[redacted]", result.Value!.DisplayName);
        Assert.Equal("[redacted]", (await agents.FindAsync(request.AgentId, default))!.DisplayName);
        grant = await service.CreateGrantAsync(default);
        result = await service.RegisterAsync(Register("cpu-two") with { DisplayName = grant.Token }, grant.Token, default);
        Assert.Equal("[redacted]", result.Value!.DisplayName);
    }

    [Fact]
    public async Task GrantCannotBeReusedForAnotherAgentOrDeviceAndInvalidRequestsDoNotConsumeIt()
    {
        var (service, _, agents, grants, _) = Services();
        var grant = await service.CreateGrantAsync(default);
        var request = Register();
        foreach (var invalid in new[] { request with { AgentToken = "short" }, request with { ModelId = "http://arbitrary" }, request with { ThreadsPerWorker = 25 } })
            Assert.Equal("invalid_agent", (await service.RegisterAsync(invalid, grant.Token, default)).Error);
        Assert.Null((await grants.FindAsync(AgentCredentials.Hash(grant.Token), default))!.BindingAgentId);
        Assert.Null((await service.RegisterAsync(request, grant.Token, default)).Error);
        Assert.Equal("unauthorized_enrollment", (await service.RegisterAsync(request with { AgentId = "cpu-other" }, grant.Token, default)).Error);
        Assert.Equal("agent_exists", (await service.RegisterAsync(request with { AgentToken = AgentCredentials.Create() }, grant.Token, default)).Error);
        Assert.Single(await agents.ListAsync(default));
    }

    [Fact]
    public async Task ExpiredGrantIsRejectedEvenBeforeMongoTtlCleanup()
    {
        var (service, _, agents, grants, clock) = Services();
        var grant = await service.CreateGrantAsync(default);
        clock.Now = grant.ExpiresAt;
        Assert.NotNull(await grants.FindAsync(AgentCredentials.Hash(grant.Token), default));
        Assert.Equal("unauthorized_enrollment", (await service.RegisterAsync(Register(), grant.Token, default)).Error);
        Assert.Empty(await agents.ListAsync(default));
    }

    [Fact]
    public async Task InterruptedEnrollmentKeepsTheBindingAndSameDeviceCanResume()
    {
        var (service, _, agents, grants, _) = Services();
        var grant = await service.CreateGrantAsync(default);
        var request = Register();
        agents.CreationFailuresRemaining = 1;
        await Assert.ThrowsAsync<TimeoutException>(() => service.RegisterAsync(request, grant.Token, default));
        Assert.Equal(request.AgentId, (await grants.FindAsync(AgentCredentials.Hash(grant.Token), default))!.BindingAgentId);
        Assert.Equal("unauthorized_enrollment", (await service.RegisterAsync(request with { AgentToken = AgentCredentials.Create() }, grant.Token, default)).Error);
        Assert.Null((await service.RegisterAsync(request, grant.Token, default)).Error);
        Assert.Single(await agents.ListAsync(default));
    }

    [Fact]
    public async Task ConcurrentDifferentDevicesConsumeOneGrantOnlyOnce()
    {
        var (service, _, agents, grants, _) = Services();
        var grant = await service.CreateGrantAsync(default);
        var requests = Enumerable.Range(0, 12).Select(index => Register($"cpu-{index}")).ToArray();
        var results = await Task.WhenAll(requests.Select(request => service.RegisterAsync(request, grant.Token, default)));
        var winner = Assert.Single(results, result => result.Value is not null).Value!;
        Assert.All(results.Where(result => result.Value is null), result => Assert.Equal("unauthorized_enrollment", result.Error));
        Assert.Single(await agents.ListAsync(default));
        Assert.Equal(winner.AgentId, (await grants.FindAsync(AgentCredentials.Hash(grant.Token), default))!.BindingAgentId);
    }

    [Fact]
    public async Task ConcurrentSameDeviceRetriesReturnTheSameInitialWorkerIdentities()
    {
        var (service, _, agents, _, _) = Services();
        var grant = await service.CreateGrantAsync(default);
        var request = Register();
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.RegisterAsync(request, grant.Token, default)));
        var stored = Assert.Single(await agents.ListAsync(default));
        Assert.All(results, result =>
        {
            Assert.Null(result.Error);
            Assert.Equal(stored.DesiredWorkers, result.Value!.DesiredWorkers);
            Assert.Equal(1, result.Value.Revision);
        });
    }

    [Fact]
    public async Task DeletionRevokesBoundGrantAndFailsClosedIfRevocationCannotBeSaved()
    {
        var (service, inference, agents, grants, _) = Services();
        var grant = await service.CreateGrantAsync(default);
        var request = Register() with { InitialWorkers = 0 };
        Assert.Null((await service.RegisterAsync(request, grant.Token, default)).Error);
        grants.RevokeUnavailable = true;
        await Assert.ThrowsAsync<TimeoutException>(() => inference.DeleteAsync(request.AgentId, default));
        Assert.NotNull(await agents.FindAsync(request.AgentId, default));
        grants.RevokeUnavailable = false;
        Assert.Null(await inference.DeleteAsync(request.AgentId, default));
        Assert.True((await grants.FindAsync(AgentCredentials.Hash(grant.Token), default))!.Revoked);
        Assert.Equal("unauthorized_enrollment", (await service.RegisterAsync(request, grant.Token, default)).Error);
        Assert.Equal("unauthorized_enrollment", (await service.RegisterAsync(request, null, default)).Error);
        Assert.Empty(await agents.ListAsync(default));
        var replacementGrant = await service.CreateGrantAsync(default);
        Assert.Null((await service.RegisterAsync(request with { AgentToken = AgentCredentials.Create() }, replacementGrant.Token, default)).Error);
    }

    [Fact]
    public async Task EnrollmentAlreadyBoundBeforeDeletionCannotResurrectAnyRetiredCredential()
    {
        await AssertDelayedCreationCannotResurrect(new MemoryInferenceRepository(), new MemoryEnrollmentGrantRepository(), new TestClock());
    }

    [Fact]
    public async Task ExhaustedIdentityHistoryRefusesReuseWithoutForgettingAnyRevokedCredential()
    {
        var (enrollment, service, repository, _, _) = Services();
        var first = (await service.CreateAsync(Fixtures.Create(workers: 0), default)).Value!;
        Assert.Null(await service.DeleteAsync("cpu-one", default));
        for (var cycle = 1; cycle < MongoInferenceRepository.MaxRetiredCredentials; cycle++)
        {
            Assert.Null((await service.CreateAsync(Fixtures.Create(workers: 0), default)).Error);
            Assert.Null(await service.DeleteAsync("cpu-one", default));
        }
        Assert.Equal("agent_exists", (await service.CreateAsync(Fixtures.Create(workers: 0), default)).Error);
        var grant = await enrollment.CreateGrantAsync(default);
        Assert.Equal("agent_identity_unavailable", (await enrollment.RegisterAsync(Register() with { AgentToken = first.AgentToken }, grant.Token, default)).Error);
        Assert.Empty(await repository.ListAsync(default));
    }

    internal static async Task AssertDelayedCreationCannotResurrect(IInferenceRepository repository,
        IEnrollmentGrantRepository grants, TestClock clock)
    {
        var normal = new InferenceService(repository, Options.Create(new InferenceOptions()), clock, grants);
        var enrollment = new EnrollmentService(grants, repository, normal, clock);
        var pausedRepository = new PausedCreationRepository(repository);
        var pausedService = new InferenceService(pausedRepository, Options.Create(new InferenceOptions()), clock, grants);
        var pausedEnrollment = new EnrollmentService(grants, pausedRepository, pausedService, clock);
        var request = Register() with { InitialWorkers = 0 };
        var grant = await enrollment.CreateGrantAsync(default);
        var inFlight = pausedEnrollment.RegisterAsync(request, grant.Token, default);
        try
        {
            await pausedRepository.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null((await enrollment.RegisterAsync(request, grant.Token, default)).Error);
            Assert.Null(await normal.DeleteAsync(request.AgentId, default));
            for (var cycle = 0; cycle < 2; cycle++)
            {
                var replacement = (await normal.CreateAsync(Fixtures.Create(workers: 0), default)).Value!;
                // Ordinary snapshot writes must preserve the previous generations' revocations.
                Assert.Null((await normal.HeartbeatAsync(Fixtures.Heartbeat(request.AgentId), replacement.AgentToken, default)).Error);
                Assert.Null((await normal.HeartbeatAsync(Fixtures.Heartbeat(request.AgentId) with { AppliedRevision = 1 }, replacement.AgentToken, default)).Error);
                Assert.Null(await normal.DeleteAsync(request.AgentId, default));
            }
        }
        finally { pausedRepository.Resume.TrySetResult(); }
        Assert.Equal("agent_identity_unavailable", (await inFlight).Error);
        Assert.Null(await repository.FindAsync(request.AgentId, default));
        Assert.Empty(await repository.ListAsync(default));
        var newGrant = await enrollment.CreateGrantAsync(default);
        Assert.Equal("agent_identity_unavailable", (await enrollment.RegisterAsync(request, newGrant.Token, default)).Error);
        var freshGrant = await enrollment.CreateGrantAsync(default);
        Assert.Null((await enrollment.RegisterAsync(request with { AgentToken = AgentCredentials.Create() }, freshGrant.Token, default)).Error);
    }

    private static RegisterInferenceAgentRequest Register(string id = "cpu-one") => new(id, "CPU machine", AgentCredentials.Create());

    private static (EnrollmentService Enrollment, InferenceService Inference, MemoryInferenceRepository Agents,
        MemoryEnrollmentGrantRepository Grants, TestClock Clock) Services()
    {
        var agents = new MemoryInferenceRepository();
        var grants = new MemoryEnrollmentGrantRepository();
        var clock = new TestClock();
        var inference = new InferenceService(agents, Options.Create(new InferenceOptions()), clock, grants);
        return (new(grants, agents, inference, clock), inference, agents, grants, clock);
    }
}
