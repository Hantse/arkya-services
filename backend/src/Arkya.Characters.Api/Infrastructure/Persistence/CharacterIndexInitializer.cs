namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class CharacterIndexInitializer(MongoCharacterRepository repository, MongoCharacterQuestRepository quests,
    MongoGameLeaseStore leases, RealmMongoDatabaseProvider storage) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await storage.EnsureActivatedAsync(cancellationToken);
        await repository.EnsureIndexesAsync(cancellationToken);
        await quests.EnsureIndexesAsync(cancellationToken);
        await leases.EnsureIndexesAsync(cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
