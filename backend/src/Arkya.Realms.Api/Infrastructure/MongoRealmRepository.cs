using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Realms.Api.Infrastructure;

public sealed class MongoRealmRepository(IMongoClient client, IOptions<MongoOptions> options) : IRealmRepository
{
    private readonly IMongoDatabase database = client.GetDatabase(options.Value.DatabaseName);
    private IMongoCollection<RealmDocument> Realms => database.GetCollection<RealmDocument>("realms")
        .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    public async Task InitializeAsync(CancellationToken cancellationToken) => await Realms.Indexes.CreateManyAsync([
        new CreateIndexModel<RealmDocument>(Builders<RealmDocument>.IndexKeys.Ascending(value => value.TargetId),
            new CreateIndexOptions { Name = "ux_realm_target", Unique = true }),
        new CreateIndexModel<RealmDocument>(Builders<RealmDocument>.IndexKeys.Ascending(value => value.Namespace),
            new CreateIndexOptions { Name = "ux_realm_namespace", Unique = true }),
        new CreateIndexModel<RealmDocument>(Builders<RealmDocument>.IndexKeys.Ascending(value => value.DeploymentIdentity),
            new CreateIndexOptions { Name = "ux_realm_deployment", Unique = true })
    ], cancellationToken);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RealmRecord>> ListAsync(CancellationToken cancellationToken) =>
        (await Realms.Find(FilterDefinition<RealmDocument>.Empty).SortBy(value => value.RealmId)
            .ToListAsync(cancellationToken)).Select(value => value.ToRecord()).ToArray();

    public async Task<RealmRecord?> FindAsync(string realmId, CancellationToken cancellationToken) =>
        (await Realms.Find(value => value.RealmId == realmId).FirstOrDefaultAsync(cancellationToken))?.ToRecord();

    public async Task<bool> TryCreateAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        try
        {
            await Realms.InsertOneAsync(RealmDocument.From(realm), cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { return false; }
    }

    public async Task<bool> TryReplaceAsync(RealmRecord realm, long expectedVersion, CancellationToken cancellationToken)
    {
        var result = await Realms.ReplaceOneAsync(value => value.RealmId == realm.RealmId && value.Version == expectedVersion,
            RealmDocument.From(realm), cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }
}

internal sealed class RealmDocument
{
    [BsonId] public string RealmId { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string DisplayName { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Namespace { get; set; } = "";
    public ProfileSnapshot Profile { get; set; } = null!;
    public string DeploymentIdentity { get; set; } = "";
    public long Revision { get; set; }
    public long Version { get; set; }
    public long? RequestedFromRevision { get; set; }
    public string Status { get; set; } = RealmStatus.Draft;
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeploymentStartedAt { get; set; }
    public ServerObservation[] Servers { get; set; } = [];
    public RealmImageTransition? ImageRelease { get; set; }

    public RealmRecord ToRecord()
    {
        if (SchemaVersion != 1) throw new InvalidOperationException("Unsupported realm schema version.");
        return new(RealmId, DisplayName, TargetId, Namespace, Profile, DeploymentIdentity, Revision, Version,
            RequestedFromRevision, Status, LastError, new DateTimeOffset(UpdatedAt, TimeSpan.Zero),
            DeploymentStartedAt is { } started ? new DateTimeOffset(started, TimeSpan.Zero) : null, Servers)
            { ImageRelease = ImageRelease };
    }

    public static RealmDocument From(RealmRecord realm) => new()
    {
        RealmId = realm.RealmId, DisplayName = realm.DisplayName, TargetId = realm.TargetId, Namespace = realm.Namespace,
        Profile = realm.Profile, DeploymentIdentity = realm.DeploymentIdentity, Revision = realm.Revision, Version = realm.Version,
        RequestedFromRevision = realm.RequestedFromRevision, Status = realm.Status, LastError = realm.LastError,
        UpdatedAt = realm.UpdatedAt.UtcDateTime, DeploymentStartedAt = realm.DeploymentStartedAt?.UtcDateTime, Servers = realm.Servers,
        ImageRelease = realm.ImageRelease
    };
}

public sealed class RealmStoreInitializer(IRealmRepository repository) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await repository.InitializeAsync(timeout.Token); }
        catch (Exception) { throw new InvalidOperationException("Realm database initialization failed."); }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
