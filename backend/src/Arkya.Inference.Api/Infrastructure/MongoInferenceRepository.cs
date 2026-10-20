using Arkya.Inference.Api.Application;
using Arkya.Inference.Api.Domain;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Inference.Api.Infrastructure;

public sealed class MongoInferenceRepository(IMongoClient client, IOptions<MongoOptions> options) : IInferenceRepository
{
    internal const int MaxRetiredCredentials = 256;
    private readonly IMongoDatabase database = client.GetDatabase(options.Value.DatabaseName);
    private static FilterDefinition<InferenceAgentDocument> Active =>
        Builders<InferenceAgentDocument>.Filter.Ne(value => value.Deleted, true);
    private IMongoCollection<InferenceAgentDocument> Agents => database.GetCollection<InferenceAgentDocument>("agents")
        .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    public async Task InitializeAsync(CancellationToken cancellationToken) =>
        await Agents.Indexes.CreateOneAsync(new CreateIndexModel<InferenceAgentDocument>(
            Builders<InferenceAgentDocument>.IndexKeys.Ascending(value => value.LastSeenAt),
            new CreateIndexOptions { Name = "ix_agent_last_seen" }), cancellationToken: cancellationToken);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<InferenceAgentRecord>> ListAsync(CancellationToken cancellationToken) =>
        (await Agents.Find(Active).SortBy(value => value.AgentId)
            .ToListAsync(cancellationToken)).Select(value => value.ToRecord()).ToArray();

    public async Task<InferenceAgentRecord?> FindAsync(string agentId, CancellationToken cancellationToken) =>
        (await Agents.Find(Active & Builders<InferenceAgentDocument>.Filter.Eq(value => value.AgentId, agentId))
            .FirstOrDefaultAsync(cancellationToken))?.ToRecord();

    public async Task<bool> TryCreateAsync(InferenceAgentRecord agent, CancellationToken cancellationToken)
    {
        try
        {
            await Agents.InsertOneAsync(InferenceAgentDocument.From(agent), cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { }

        var previous = await Agents.Find(value => value.AgentId == agent.AgentId).FirstOrDefaultAsync(cancellationToken);
        if (previous is null || !previous.Deleted || previous.RevokedTokenHashes.Length >= MaxRetiredCredentials ||
            previous.RevokedTokenHashes.Contains(agent.TokenHash, StringComparer.Ordinal)) return false;
        var replacement = InferenceAgentDocument.From(agent);
        replacement.RevokedTokenHashes = previous.RevokedTokenHashes;
        return (await Agents.ReplaceOneAsync(value => value.AgentId == agent.AgentId && value.Deleted &&
            value.Version == previous.Version && value.TokenHash == previous.TokenHash,
            replacement, cancellationToken: cancellationToken)).ModifiedCount == 1;
    }

    public async Task<bool> TryReplaceAsync(InferenceAgentRecord agent, long expectedVersion, CancellationToken cancellationToken)
    {
        var replacement = InferenceAgentDocument.From(agent).ToBsonDocument();
        replacement.Remove("_id");
        replacement.Remove(nameof(InferenceAgentDocument.Deleted));
        replacement.Remove(nameof(InferenceAgentDocument.RevokedTokenHashes));
        // Keep revocation history outside the mutable application snapshot.
        var result = await Agents.UpdateOneAsync(Active & Builders<InferenceAgentDocument>.Filter.Where(value =>
            value.AgentId == agent.AgentId && value.Version == expectedVersion && value.TokenHash == agent.TokenHash),
            new BsonDocument("$set", replacement), cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryDeleteAsync(string agentId, long expectedVersion, string expectedTokenHash, CancellationToken cancellationToken) =>
        (await Agents.UpdateOneAsync(Active & Builders<InferenceAgentDocument>.Filter.Where(value =>
            value.AgentId == agentId && value.Version == expectedVersion && value.TokenHash == expectedTokenHash),
            Builders<InferenceAgentDocument>.Update.Set(value => value.Deleted, true)
                .AddToSet(value => value.RevokedTokenHashes, expectedTokenHash).Inc(value => value.Version, 1),
            cancellationToken: cancellationToken)).ModifiedCount == 1;
}

internal sealed class InferenceAgentDocument
{
    [BsonId] public string AgentId { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public bool Deleted { get; set; }
    public string[] RevokedTokenHashes { get; set; } = [];
    public string DisplayName { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public long Revision { get; set; }
    public long Version { get; set; }
    public long ObservedRevision { get; set; }
    public int MaxWorkers { get; set; }
    public int ThreadsPerWorker { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime DesiredUpdatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? MachineName { get; set; }
    public string? AgentVersion { get; set; }
    public int? LogicalProcessors { get; set; }
    public long? TotalMemoryMb { get; set; }
    public int? DemoPort { get; set; }
    public string? LastError { get; set; }
    public InferenceWorkerDesired[] DesiredWorkers { get; set; } = [];
    public InferenceWorkerObserved[] ObservedWorkers { get; set; } = [];
    public InferenceWorkerDesired[] RetiredWorkers { get; set; } = [];
    public InferenceWorkerThreadReservation[] ThreadReservations { get; set; } = [];

    public InferenceAgentRecord ToRecord()
    {
        if (SchemaVersion != 1) throw new InvalidOperationException("Unsupported inference agent schema version.");
        return new(AgentId, DisplayName, TokenHash, Revision, Version, ObservedRevision, MaxWorkers, ThreadsPerWorker,
            new(CreatedAt, TimeSpan.Zero), new(DesiredUpdatedAt, TimeSpan.Zero),
            LastSeenAt is { } seen ? new DateTimeOffset(seen, TimeSpan.Zero) : null,
            MachineName, AgentVersion, LogicalProcessors, TotalMemoryMb, DemoPort, LastError,
            DesiredWorkers.Select(Normalize).ToArray(),
            ObservedWorkers.Select(value => value with { ModelId = value.ModelId ?? InferenceModels.DefaultModelId }).ToArray(),
            RetiredWorkers.Select(Normalize).ToArray(), ThreadReservations);
    }

    public static InferenceAgentDocument From(InferenceAgentRecord agent) => new()
    {
        AgentId = agent.AgentId, DisplayName = agent.DisplayName, TokenHash = agent.TokenHash,
        Revision = agent.Revision, Version = agent.Version, ObservedRevision = agent.ObservedRevision, MaxWorkers = agent.MaxWorkers,
        ThreadsPerWorker = agent.ThreadsPerWorker, CreatedAt = agent.CreatedAt.UtcDateTime,
        DesiredUpdatedAt = agent.DesiredUpdatedAt.UtcDateTime, LastSeenAt = agent.LastSeenAt?.UtcDateTime,
        MachineName = agent.MachineName, AgentVersion = agent.AgentVersion, LogicalProcessors = agent.LogicalProcessors,
        TotalMemoryMb = agent.TotalMemoryMb, DemoPort = agent.DemoPort, LastError = agent.LastError,
        DesiredWorkers = agent.DesiredWorkers, ObservedWorkers = agent.ObservedWorkers, RetiredWorkers = agent.RetiredWorkers,
        ThreadReservations = agent.ThreadReservations
    };

    private static InferenceWorkerDesired Normalize(InferenceWorkerDesired value) =>
        value with { ModelId = value.ModelId ?? InferenceModels.DefaultModelId };
}

public sealed class InferenceStoreInitializer(IInferenceRepository repository, IEnrollmentGrantRepository grants) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await Task.WhenAll(repository.InitializeAsync(timeout.Token), grants.InitializeAsync(timeout.Token)); }
        catch (Exception) { throw new InvalidOperationException("Inference database initialization failed."); }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
