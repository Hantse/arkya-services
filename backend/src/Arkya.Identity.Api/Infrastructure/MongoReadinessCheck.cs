using Arkya.Identity.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Identity.Api.Infrastructure;

public sealed class MongoReadinessCheck(IMongoClient client, IOptions<MongoOptions> mongo,
    IOptions<ManagementOptions> management) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(management.Value.ReadinessTimeout);
        try
        {
            await client.GetDatabase(mongo.Value.DatabaseName)
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is MongoException or TimeoutException or OperationCanceledException)
        {
            // Probe responses and logs must not disclose connection strings or dependency details.
            return HealthCheckResult.Unhealthy("Identity persistence is unavailable.");
        }
    }
}
