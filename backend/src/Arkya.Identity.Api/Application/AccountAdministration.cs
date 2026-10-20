using System.Security.Cryptography;
using System.Text;
using Arkya.Identity.Api.Domain;
using Arkya.ServiceDefaults.Accounts;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Api.Application;

public sealed class AdministrationOptions
{
    public bool Enabled { get; set; }
    public string AdminKey { get; set; } = "";
    public bool AllowClusterHttp { get; set; }
    public bool IsValid() => !Enabled || AdminKey is { Length: >= 32 and <= 512 };
}

public sealed class AccountAdminKeyValidator(IOptions<AdministrationOptions> options)
{
    public const string HeaderName = "X-Arkya-Admin-Key";
    public const string ActorHeaderName = "X-Arkya-Admin-Actor";

    public bool IsValid(string? value) => options.Value.Enabled && value is { Length: >= 32 and <= 512 } &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value)),
            SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.AdminKey)));
}

public sealed class AccountAdministration(IIdentityStore store, TimeProvider clock)
{
    public static bool ValidActor(string? actor) => actor is { Length: >= 1 and <= 128 } &&
        actor == actor.Trim() && !actor.Any(char.IsControl);
    public static bool ValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 500 && !reason.Any(char.IsControl);

    public async Task<AdminAccountPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var result = await store.ListAccountsAsync(search?.Trim(), page, pageSize, cancellationToken);
        return new(result.Items.Select(ToResponse).ToArray(), result.Total, page, pageSize);
    }

    public async Task<AdminAccountSummary?> GetAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await store.GetAccountAsync(accountId, cancellationToken);
        return account is null ? null : ToResponse(account);
    }

    public async Task<(string Status, AdminAccountSummary? Account)> ChangeAsync(string accountId, long expectedRevision,
        string action, bool? hasBetaAccess, string reason, string actor, CancellationToken cancellationToken)
    {
        var changed = await store.AdministerAccountAsync(accountId, expectedRevision, action, hasBetaAccess,
            reason.Trim(), actor, clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (changed is not null) return ("updated", ToResponse(changed));
        return (await store.GetAccountAsync(accountId, cancellationToken) is null ? "not_found" : "conflict", null);
    }

    private static AdminAccountSummary ToResponse(Account account) => new(account.Id, account.DisplayName, account.DiscordId,
        account.IsBanned, account.BanReason, account.HasBetaAccess, account.Revision, Utc(account.CreatedAt), Utc(account.UpdatedAt));
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
