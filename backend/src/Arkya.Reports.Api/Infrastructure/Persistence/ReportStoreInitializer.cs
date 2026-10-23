namespace Arkya.Reports.Api.Infrastructure.Persistence;

public sealed class ReportStoreInitializer(MongoReportRepository repository) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => repository.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
