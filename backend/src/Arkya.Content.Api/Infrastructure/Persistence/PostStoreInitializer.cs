namespace Arkya.Content.Api.Infrastructure.Persistence;

public sealed class PostStoreInitializer(MongoPostRepository repository) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => repository.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
