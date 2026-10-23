namespace Arkya.Content.Api.Infrastructure.Persistence;

public sealed class ItemContentStoreInitializer(MongoItemContentRepository repository) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => repository.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
