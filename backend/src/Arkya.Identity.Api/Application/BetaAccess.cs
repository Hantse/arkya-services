using Arkya.Identity.Api.Domain;
using Arkya.ServiceDefaults.Accounts;

namespace Arkya.Identity.Api.Application;

public interface IBetaAccessStore
{
    Task<Account?> RegisterBetaAsync(string accountId, DateTime now, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Account> Items, long Total)> ListBetaRegistrationsAsync(string? search, string status,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<Account?> GrantBetaAsync(string accountId, long expectedRevision, string reason, string actor,
        DateTime now, CancellationToken cancellationToken);
    Task<BetaRandomGrantResponse?> GrantRandomBetaAsync(Guid requestId, int count, string reason, string actor,
        DateTime now, CancellationToken cancellationToken);
    Task RecoverBetaGrantsAsync(CancellationToken cancellationToken);
}

public sealed class BetaAccessService(IIdentityStore identityStore, IBetaAccessStore store, TimeProvider clock)
{
    public async Task<BetaRegistrationResponse?> GetAsync(string accountId, CancellationToken ct)
    {
        var account = await identityStore.GetAccountAsync(accountId, ct);
        return account is null || account.IsBanned ? null : Self(account);
    }

    public async Task<BetaRegistrationResponse?> RegisterAsync(string accountId, CancellationToken ct)
    {
        var account = await store.RegisterBetaAsync(accountId, clock.GetUtcNow().UtcDateTime, ct);
        return account is null ? null : Self(account);
    }

    public async Task<BetaRegistrationPage> ListAsync(string? search, string status, int page, int pageSize, CancellationToken ct)
    {
        var result = await store.ListBetaRegistrationsAsync(search?.Trim(), status, page, pageSize, ct);
        return new(result.Items.Select(Summary).ToArray(), result.Total, page, pageSize);
    }

    public async Task<(string Status, BetaRegistrationSummary? Registration)> GrantAsync(string accountId,
        long expectedRevision, string reason, string actor, CancellationToken ct)
    {
        var account = await store.GrantBetaAsync(accountId, expectedRevision, reason.Trim(), actor, clock.GetUtcNow().UtcDateTime, ct);
        if (account is not null) return ("updated", Summary(account));
        var current = await identityStore.GetAccountAsync(accountId, ct);
        return (current?.BetaRegisteredAt is null ? "not_found" : "conflict", null);
    }

    public Task<BetaRandomGrantResponse?> GrantRandomAsync(BetaRandomGrantRequest request, string actor, CancellationToken ct) =>
        store.GrantRandomBetaAsync(request.RequestId, request.Count, request.Reason.Trim(), actor, clock.GetUtcNow().UtcDateTime, ct);

    private static BetaRegistrationResponse Self(Account account) =>
        new(account.BetaRegisteredAt.HasValue, account.BetaRegisteredAt is { } at ? Utc(at) : null, account.HasBetaAccess);
    private static BetaRegistrationSummary Summary(Account account) =>
        new(account.Id, account.DisplayName, account.DiscordId, Utc(account.BetaRegisteredAt!.Value),
            account.HasBetaAccess, account.IsBanned, account.Revision);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

public sealed class BetaGrantRecoveryService(IServiceScopeFactory scopes, ILogger<BetaGrantRecoveryService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IBetaAccessStore>().RecoverBetaGrantsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { log.LogError(exception, "Could not recover pending beta grants."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
