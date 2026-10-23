using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;

namespace Arkya.Characters.Api.Application;

public sealed record CharacterAdminQuery(Guid? AccountId, string Search, bool OnlineOnly, int Page, int PageSize, string? RealmId = null)
{
    public bool IsValid() => (AccountId is null || AccountId != Guid.Empty) && Search.Length <= 100 &&
        !Search.Any(char.IsControl) && Page is >= 1 and <= 100000 && PageSize is >= 1 and <= 100 &&
        (RealmId is null || GameProtocol.ValidId(RealmId));
}

public interface ICharacterAdminRepository
{
    Task<AdminCharacterPage> ListAsync(CharacterAdminQuery query, DateTimeOffset now, CancellationToken cancellationToken);
    Task<AdminCharacterOverview> OverviewAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
