using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Admin.Authentication;

public sealed record AdminSession(
    [property: BsonId] string Id,
    string RefreshTokenHash,
    string Provider,
    string Subject,
    string Name,
    string CredentialVersion,
    DateTime ExpiresAt,
    DateTime RotatedAt,
    string[] UsedTokenHashes,
    int SchemaVersion = 1)
{
    public const int MaximumRotations = 65536;
}

public interface IAdminSessionStore
{
    Task CreateAsync(AdminSession session, CancellationToken cancellationToken);
    Task<AdminSession?> GetAsync(string id, CancellationToken cancellationToken);
    Task<bool> RotateAsync(string id, string expectedHash, string nextHash, DateTime now, CancellationToken cancellationToken);
    Task RevokeAsync(string id, CancellationToken cancellationToken);
}

public sealed class MongoAdminSessionStore : IAdminSessionStore
{
    private readonly IMongoCollection<AdminSession> sessions;

    public MongoAdminSessionStore(IMongoClient client, IOptions<MongoOptions> options) =>
        sessions = client.GetDatabase(options.Value.DatabaseName).GetCollection<AdminSession>("admin_sessions");

    public Task InitializeAsync(CancellationToken cancellationToken) => sessions.Indexes.CreateOneAsync(
        new CreateIndexModel<AdminSession>(Builders<AdminSession>.IndexKeys.Ascending(x => x.ExpiresAt),
            new CreateIndexOptions { Name = "admin_session_expiry", ExpireAfter = TimeSpan.Zero }),
        cancellationToken: cancellationToken);

    public Task CreateAsync(AdminSession session, CancellationToken cancellationToken) =>
        sessions.InsertOneAsync(session, cancellationToken: cancellationToken);

    public Task<AdminSession?> GetAsync(string id, CancellationToken cancellationToken) =>
        sessions.Find(x => x.Id == id).FirstOrDefaultAsync(cancellationToken)!;

    public async Task<bool> RotateAsync(string id, string expectedHash, string nextHash, DateTime now,
        CancellationToken cancellationToken)
    {
        var update = Builders<AdminSession>.Update.Set(x => x.RefreshTokenHash, nextHash)
            .Set(x => x.RotatedAt, now).Push(x => x.UsedTokenHashes, expectedHash);
        var filters = Builders<AdminSession>.Filter;
        var result = await sessions.UpdateOneAsync(filters.Eq(x => x.Id, id) & filters.Eq(x => x.RefreshTokenHash, expectedHash) &
            filters.Gt(x => x.ExpiresAt, now) & filters.Eq(x => x.SchemaVersion, 1) &
            filters.SizeLt(x => x.UsedTokenHashes, AdminSession.MaximumRotations),
            update, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public Task RevokeAsync(string id, CancellationToken cancellationToken) =>
        sessions.DeleteOneAsync(x => x.Id == id, cancellationToken);
}

public sealed class AdminSessionIndexInitializer(MongoAdminSessionStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
