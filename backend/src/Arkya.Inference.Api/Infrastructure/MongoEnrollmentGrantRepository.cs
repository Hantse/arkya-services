using Arkya.Inference.Api.Application;
using Arkya.Inference.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Inference.Api.Infrastructure;

public sealed class MongoEnrollmentGrantRepository(IMongoClient client, IOptions<MongoOptions> options)
    : IEnrollmentGrantRepository
{
    private IMongoCollection<EnrollmentGrantDocument> Grants => client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<EnrollmentGrantDocument>("enrollment_grants")
        .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    public async Task InitializeAsync(CancellationToken cancellationToken) =>
        await Grants.Indexes.CreateManyAsync([
            new(Builders<EnrollmentGrantDocument>.IndexKeys.Ascending(value => value.ExpiresAt),
                new CreateIndexOptions { Name = "ttl_enrollment_expiry", ExpireAfter = TimeSpan.Zero }),
            new(Builders<EnrollmentGrantDocument>.IndexKeys.Ascending(value => value.BindingAgentId),
                new CreateIndexOptions { Name = "ix_enrollment_agent" })
        ], cancellationToken);

    public async Task<EnrollmentGrantRecord?> FindAsync(string tokenHash, CancellationToken cancellationToken) =>
        (await Grants.Find(value => value.TokenHash == tokenHash).FirstOrDefaultAsync(cancellationToken))?.ToRecord();

    public async Task<bool> TryCreateAsync(EnrollmentGrantRecord grant, CancellationToken cancellationToken)
    {
        try
        {
            await Grants.InsertOneAsync(EnrollmentGrantDocument.From(grant), cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { return false; }
    }

    public async Task<bool> TryBindAsync(string tokenHash, string agentId, string agentTokenHash, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var result = await Grants.UpdateOneAsync(value => value.TokenHash == tokenHash && !value.Revoked &&
            value.ExpiresAt > now.UtcDateTime &&
            ((value.BindingAgentId == null && value.BindingTokenHash == null) ||
             (value.BindingAgentId == agentId && value.BindingTokenHash == agentTokenHash)),
            Builders<EnrollmentGrantDocument>.Update.Set(value => value.BindingAgentId, agentId)
                .Set(value => value.BindingTokenHash, agentTokenHash), cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task RevokeForAgentAsync(string agentId, string agentTokenHash, CancellationToken cancellationToken) =>
        await Grants.UpdateManyAsync(value => value.BindingAgentId == agentId && value.BindingTokenHash == agentTokenHash,
            Builders<EnrollmentGrantDocument>.Update.Set(value => value.Revoked, true), cancellationToken: cancellationToken);
}

internal sealed class EnrollmentGrantDocument
{
    [BsonId] public string TokenHash { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? BindingAgentId { get; set; }
    public string? BindingTokenHash { get; set; }
    public bool Revoked { get; set; }

    public EnrollmentGrantRecord ToRecord()
    {
        if (SchemaVersion != 1) throw new InvalidOperationException("Unsupported enrollment grant schema version.");
        return new(TokenHash, new(CreatedAt, TimeSpan.Zero), new(ExpiresAt, TimeSpan.Zero), BindingAgentId, BindingTokenHash, Revoked);
    }

    public static EnrollmentGrantDocument From(EnrollmentGrantRecord grant) => new()
    {
        TokenHash = grant.TokenHash, CreatedAt = grant.CreatedAt.UtcDateTime, ExpiresAt = grant.ExpiresAt.UtcDateTime,
        BindingAgentId = grant.BindingAgentId, BindingTokenHash = grant.BindingTokenHash, Revoked = grant.Revoked
    };
}
