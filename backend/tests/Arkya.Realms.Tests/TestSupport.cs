using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class MemoryRealmRepository : IRealmRepository
{
    private readonly Dictionary<string, RealmRecord> realms = [];
    private readonly object gate = new();
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<IReadOnlyList<RealmRecord>> ListAsync(CancellationToken cancellationToken)
    { lock (gate) return Task.FromResult<IReadOnlyList<RealmRecord>>(realms.Values.ToArray()); }
    public Task<RealmRecord?> FindAsync(string realmId, CancellationToken cancellationToken)
    { lock (gate) return Task.FromResult(realms.GetValueOrDefault(realmId)); }
    public Task<bool> TryCreateAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (realms.ContainsKey(realm.RealmId) || realms.Values.Any(value =>
                value.DeletedAt is null && (value.TargetId == realm.TargetId || value.Namespace == realm.Namespace))) return Task.FromResult(false);
            realms.Add(realm.RealmId, realm);
            return Task.FromResult(true);
        }
    }
    public Task<bool> TryReplaceAsync(RealmRecord realm, long expectedVersion, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!realms.TryGetValue(realm.RealmId, out var current) || current.Version != expectedVersion) return Task.FromResult(false);
            realms[realm.RealmId] = realm;
            return Task.FromResult(true);
        }
    }
}

internal sealed class StubDeployer : IRealmDeployer
{
    public ServerObservation[] Observations { get; set; } = [new("area-west", 1, "192.0.2.1")];
    public Exception? Error { get; set; }
    public int Calls { get; private set; }
    public int DeleteCalls { get; private set; }
    public bool DeletionComplete { get; set; } = true;
    public Task<bool> DeleteAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        DeleteCalls++;
        return Error is null ? Task.FromResult(DeletionComplete) : Task.FromException<bool>(Error);
    }
    public Task<ServerObservation[]> ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        Calls++;
        return Error is null ? Task.FromResult(Observations) : Task.FromException<ServerObservation[]>(Error);
    }
}

internal static class Fixtures
{
    public const string Image = "registry.example/arkya/dedicated@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public static DeploymentOptions Options(bool enabled = true) => new()
    {
        Enabled = enabled,
        Targets = [new() { Id = "realm-eu-one", DisplayName = "EU one", Namespace = "arkya-realm-eu-one" }],
        Profiles = [new() { Id = "island-v1", DisplayName = "Island", Image = Image,
            Servers = [new() { Id = "area-west", Port = 17843, Args = ["-batchmode", "-nographics"] }] }]
    };
    public static RealmRecord Realm(string status = RealmStatus.Pending) => new("eu-one", "Europe one", "realm-eu-one",
        "arkya-realm-eu-one", Options().Profiles[0].Snapshot(), "0123456789abcdef0123456789abcdef", 1, 1, 0,
        status, null, new TestClock().Now, new TestClock().Now, []);
}

internal sealed class RealmFactory(bool enabled = true, bool production = false) : WebApplicationFactory<Arkya.Realms.Api.Program>
{
    public const string AdminKey = "realm-administrator-test-key-123456789";
    public MemoryRealmRepository Repository { get; } = new();
    public Dictionary<string, string?> Settings { get; } = [];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(production ? "Production" : "Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RealmsAdmin:Key"] = AdminKey,
                ["Mongo:DatabaseName"] = "arkya_realms_tests"
            });
            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRealmRepository>();
            services.AddSingleton<IRealmRepository>(Repository);
            services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(RealmDeploymentWorker)));
            services.RemoveAll<IRealmDeployer>();
            services.AddSingleton<IRealmDeployer, StubDeployer>();
            services.RemoveAll<IOptions<DeploymentOptions>>();
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Fixtures.Options(enabled)));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, TestClock>();
        });
    }
    public HttpClient Client(string? key = AdminKey, string scheme = "https")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"{scheme}://localhost"), AllowAutoRedirect = false });
        if (key is not null) client.DefaultRequestHeaders.Add(RealmsAdminKeyValidator.HeaderName, key);
        return client;
    }
}
