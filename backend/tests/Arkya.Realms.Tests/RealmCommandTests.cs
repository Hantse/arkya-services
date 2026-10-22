using System.Net;
using System.Net.Http.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Arkya.Realms.Tests;

public sealed class RealmCommandTests
{
    private const string Actor = "admin:operator";
    private static readonly string Instance = Guid.NewGuid().ToString("D");
    private static readonly PollRealmCommands Poll = new(Instance, 1, ["announce", "kick", "save"]);
    private static EnqueueRealmCommand Request(string type = "announce") => new(Guid.NewGuid().ToString("D"), 1, "area-west", type,
        type == "announce" ? "Maintenance dans 10 minutes." : "", type == "kick" ? Guid.NewGuid().ToString("D") : "", "Operator test");

    private static async Task<(RealmCommandService Service, MemoryRealmRepository Realms, MemoryRealmCommandRepository Store, TestClock Clock)> Fixture()
    {
        var realms = new MemoryRealmRepository();
        await realms.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        var store = new MemoryRealmCommandRepository();
        var clock = new TestClock();
        var service = new RealmCommandService(realms, store, Options.Create(new RealmRuntimeOptions()), clock);
        await service.PollAsync("eu-one", "area-west", Poll, default);
        return (service, realms, store, clock);
    }

    [Theory]
    [InlineData("announce", "announced")]
    [InlineData("kick", "kicked")]
    [InlineData("save", "saved")]
    public async Task CommandsAreDeliveredOnceAndCompletionIsIdempotent(string type, string code)
    {
        var (service, _, _, _) = await Fixture();
        var request = Request(type);
        var accepted = (await service.EnqueueAsync("eu-one", request, Actor, default)).Value!;
        Assert.Equal("Pending", accepted.Status);
        var polls = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.PollAsync("eu-one", "area-west", Poll, default)));
        Assert.Single(polls.SelectMany(result => result.Value?.Commands ?? []));
        Assert.Empty((await service.PollAsync("eu-one", "area-west", Poll with { InstanceId = Guid.NewGuid().ToString("D") }, default)).Value!.Commands);
        var completion = new CompleteRealmCommand(Instance, true, code, 1);
        Assert.Equal("completion_conflict", (await service.CompleteAsync("eu-one", "area-west", request.RequestId,
            completion with { InstanceId = Guid.NewGuid().ToString("D") }, default)).Error);
        var completed = (await service.CompleteAsync("eu-one", "area-west", request.RequestId, completion, default)).Value!;
        Assert.Equal("Succeeded", completed.Status);
        Assert.Null(completed.InstanceId);
        Assert.Equal(completed, (await service.CompleteAsync("eu-one", "area-west", request.RequestId, completion, default)).Value);
        Assert.Equal("completion_conflict", (await service.CompleteAsync("eu-one", "area-west", request.RequestId,
            completion with { AffectedPlayers = 2 }, default)).Error);
        Assert.Equal(completed, (await service.EnqueueAsync("eu-one", request, Actor, default)).Value);
        Assert.Equal("request_conflict", (await service.EnqueueAsync("eu-one", request with { Reason = "Different" }, Actor, default)).Error);
    }

    [Fact]
    public async Task StaleRevisionOfflineRuntimeAndUnsupportedCapabilityCannotQueue()
    {
        var (service, realms, _, clock) = await Fixture();
        Assert.Equal("realm_changed", (await service.EnqueueAsync("eu-one", Request() with { ExpectedRevision = 0 }, Actor, default)).Error);
        await service.PollAsync("eu-one", "area-west", Poll with { SupportedCommands = ["save"] }, default);
        Assert.Equal("runtime_unavailable", (await service.EnqueueAsync("eu-one", Request(), Actor, default)).Error);
        clock.Now += TimeSpan.FromSeconds(31);
        Assert.Equal("runtime_unavailable", (await service.EnqueueAsync("eu-one", Request("save"), Actor, default)).Error);
        Assert.False((await service.GetAsync("eu-one", default))!.Servers[0].Available);
        var realm = (await realms.FindAsync("eu-one", default))!;
        await realms.TryReplaceAsync(realm with { Status = RealmStatus.Pending, Version = 2 }, 1, default);
        Assert.Equal("realm_changed", (await service.EnqueueAsync("eu-one", Request(), Actor, default)).Error);
    }

    [Fact]
    public async Task PendingCommandsCannotRunAfterRealmRestartOrUpdate()
    {
        var (service, realms, _, _) = await Fixture();
        await service.EnqueueAsync("eu-one", Request(), Actor, default);
        var realm = (await realms.FindAsync("eu-one", default))!;
        await realms.TryReplaceAsync(realm with { Revision = 2, Version = 2, Status = RealmStatus.Ready }, 1, default);
        Assert.Empty((await service.PollAsync("eu-one", "area-west", Poll, default)).Value!.Commands);
        var command = Assert.Single((await service.GetAsync("eu-one", default))!.Commands);
        Assert.Equal("Expired", command.Status);
        Assert.Equal("realm_changed", command.ResultCode);
    }

    [Fact]
    public async Task LostDeliveryResponseExpiresToUnknownAndPendingCommandExpiresWithoutDelivery()
    {
        var (service, _, _, clock) = await Fixture();
        var dispatched = Request();
        var pending = Request();
        await service.EnqueueAsync("eu-one", dispatched, Actor, default);
        await service.PollAsync("eu-one", "area-west", Poll, default);
        await service.EnqueueAsync("eu-one", pending, Actor, default);
        clock.Now += TimeSpan.FromMinutes(3);
        Assert.Empty((await service.PollAsync("eu-one", "area-west", Poll, default)).Value!.Commands);
        var overview = (await service.GetAsync("eu-one", default))!;
        Assert.Equal("Unknown", overview.Commands.Single(item => item.Id == dispatched.RequestId).Status);
        Assert.Equal("Expired", overview.Commands.Single(item => item.Id == pending.RequestId).Status);
        Assert.Equal("Succeeded", (await service.CompleteAsync("eu-one", "area-west", dispatched.RequestId,
            new(Instance, true, "announced", 1), default)).Value!.Status);
    }

    [Fact]
    public async Task InputsAreBoundedAndNoArbitraryCommandCanBeQueued()
    {
        var (service, _, _, _) = await Fixture();
        var invalid = new[]
        {
            Request() with { Type = "shell" }, Request() with { RequestId = Guid.Empty.ToString("D") },
            Request() with { Message = "test\ncontrol" }, Request() with { Message = new string('é', 201) },
            Request() with { Message = "" }, Request() with { Reason = "" }, Request() with { ServerId = "../secret" },
            Request("kick") with { CharacterId = "everyone" }, Request("save") with { Message = "execute" },
            Request() with { CharacterId = Guid.NewGuid().ToString("D") }
        };
        foreach (var request in invalid)
            Assert.Equal("invalid_command", (await service.EnqueueAsync("eu-one", request, Actor, default)).Error);
        Assert.Equal("invalid_command", (await service.EnqueueAsync("eu-one", Request(), "bad\nactor", default)).Error);
        Assert.Equal("invalid_poll", (await service.PollAsync("eu-one", "area-west", Poll with { SupportedCommands = ["shell"] }, default)).Error);
    }

    [Fact]
    public async Task ConcurrentRequestsRespectQueueBoundAndIdempotency()
    {
        var (service, _, _, _) = await Fixture();
        var request = Request();
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.EnqueueAsync("eu-one", request, Actor, default)));
        Assert.All(duplicates, result => Assert.Equal(request.RequestId, result.Value!.Id));
        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => service.EnqueueAsync("eu-one", Request(), Actor, default)));
        Assert.Equal(19, results.Count(result => result.Value is not null));
        Assert.Equal(20, (await service.GetAsync("eu-one", default))!.Commands.Length);
    }

    [Fact]
    public async Task HistoryIsBoundedAndPreservesPendingEntries()
    {
        var (service, _, store, clock) = await Fixture();
        var waiting = Request("save");
        await service.EnqueueAsync("eu-one", waiting, Actor, default);
        var announceOnly = Poll with { SupportedCommands = ["announce"] };
        for (var index = 0; index < 110; index++)
        {
            clock.Now += TimeSpan.FromMilliseconds(1);
            var request = Request();
            await service.EnqueueAsync("eu-one", request, Actor, default);
            await service.PollAsync("eu-one", "area-west", announceOnly, default);
            await service.CompleteAsync("eu-one", "area-west", request.RequestId, new(Instance, true, "announced", 0), default);
        }
        Assert.Equal(100, (await store.FindAsync("eu-one", "area-west", default))!.Commands.Length);
        Assert.Equal("Pending", (await store.FindAsync("eu-one", "area-west", default))!.Commands.Single(command => command.Id == waiting.RequestId).Status);
        Assert.Equal(50, (await service.GetAsync("eu-one", default))!.Commands.Length);
    }

    [Fact]
    public async Task HttpApiSeparatesAdminAndDedicatedAuthorizationAndValidatesPayloads()
    {
        await using var factory = new CommandFactory();
        await factory.Realms.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        const string adminPath = "/admin/realms/eu-one/commands";
        const string runtimePath = "/internal/realms/eu-one/servers/area-west/commands";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(adminPath)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", RealmFactory.AdminKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(runtimePath + "/poll", Poll)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(adminPath, Request())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", Actor);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(adminPath, Request())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(CommandFactory.Seed, "eu-one", "area-west"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(runtimePath + "/poll", Poll)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/internal/realms/eu-two/servers/area-west/commands/poll", Poll)).StatusCode);
        var request = Request("save");
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(adminPath, request)).StatusCode);
        var delivery = await (await client.PostAsJsonAsync(runtimePath + "/poll", Poll)).Content.ReadFromJsonAsync<RealmCommandPollResponse>();
        Assert.Equal(request.RequestId, Assert.Single(delivery!.Commands).Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(runtimePath + "/" + request.RequestId + "/complete",
            new CompleteRealmCommand(Instance, true, "saved", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(adminPath, new { requestId = Guid.NewGuid().ToString("D") })).StatusCode);
        var response = await client.GetStringAsync(adminPath);
        Assert.DoesNotContain(CommandFactory.Seed, response);
        Assert.DoesNotContain(Instance, response);
    }

    [Fact]
    public void CommandDocumentRoundTripsBson()
    {
        var state = new RealmCommandServer("eu-one", "area-west", 3, Instance, new TestClock().Now, ["announce"],
            [new(Guid.NewGuid().ToString("D"), "area-west", "announce", "Bonjour", "", "Pending", Actor, "Test", new TestClock().Now, new TestClock().Now.AddMinutes(2))]);
        var document = new RealmCommandDocument { Id = "eu-one/area-west", State = state };
        var restored = BsonSerializer.Deserialize<RealmCommandDocument>(document.ToBson());
        Assert.Equal(document.Id, restored.Id);
        Assert.Equal(state.LastSeenAt, restored.State.LastSeenAt);
        Assert.Equal(state.Commands, restored.State.Commands);
    }

    [MongoFact]
    public async Task MongoCompareAndSwapClaimsDeliveryAcrossServiceInstances()
    {
        var database = "arkya_commands_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var store = new MongoRealmCommandRepository(client, options);
            var realmStore = new MemoryRealmRepository();
            await realmStore.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
            var first = new RealmCommandService(realmStore, store, Options.Create(new RealmRuntimeOptions()), new TestClock());
            var second = new RealmCommandService(realmStore, new MongoRealmCommandRepository(client, options), Options.Create(new RealmRuntimeOptions()), new TestClock());
            await first.PollAsync("eu-one", "area-west", Poll, default);
            Assert.NotNull((await first.EnqueueAsync("eu-one", Request(), Actor, default)).Value);
            var responses = await Task.WhenAll(first.PollAsync("eu-one", "area-west", Poll, default), second.PollAsync("eu-one", "area-west", Poll, default));
            Assert.Single(responses.SelectMany(response => response.Value!.Commands));
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}

internal sealed class MemoryRealmCommandRepository : IRealmCommandRepository
{
    private readonly Dictionary<string, RealmCommandServer> servers = [];
    private readonly object gate = new();
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<RealmCommandServer?> FindAsync(string realmId, string serverId, CancellationToken ct)
    { lock (gate) return Task.FromResult(servers.GetValueOrDefault(realmId + "/" + serverId)); }
    public Task<bool> TryWriteAsync(RealmCommandServer server, long? expectedVersion, CancellationToken ct)
    {
        lock (gate)
        {
            var id = server.RealmId + "/" + server.ServerId;
            if (servers.GetValueOrDefault(id)?.Version != expectedVersion) return Task.FromResult(false);
            servers[id] = server;
            return Task.FromResult(true);
        }
    }
}

internal sealed class CommandFactory : WebApplicationFactory<Arkya.Realms.Api.Program>
{
    public const string Seed = "dedicated-command-test-seed-1234567890";
    public MemoryRealmRepository Realms { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RealmsAdmin:Key"] = RealmFactory.AdminKey,
            ["Mongo:DatabaseName"] = "arkya_command_http_tests",
            ["RealmRuntime:DedicatedKeySeed"] = Seed,
            ["RealmRuntime:CharactersBaseUrl"] = "https://characters.example/"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRealmRepository>();
            services.AddSingleton<IRealmRepository>(Realms);
            services.RemoveAll<IRealmCommandRepository>();
            services.AddSingleton<IRealmCommandRepository, MemoryRealmCommandRepository>();
            services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(RealmDeploymentWorker)));
            services.RemoveAll<IOptions<DeploymentOptions>>();
            services.AddSingleton(Options.Create(Fixtures.Options()));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, TestClock>();
        });
    }
}
