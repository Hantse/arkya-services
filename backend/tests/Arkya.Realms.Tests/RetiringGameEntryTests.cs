using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RetiringGameEntryTests
{
    [Fact]
    public async Task PreviousServerIdentityExistsOnlyWhileItsApprovedRuntimeIsRetiring()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        var oldServer = original.Profile.Servers[0] with
            { Transport = "WebSocket", PublicEndpoint = "wss://game.example/old/" };
        var previous = original.Profile with { Servers = [oldServer] };
        var realm = original with
        {
            Profile = previous with { Servers = [oldServer with { Id = "new-island", PublicEndpoint = "wss://game.example/new/" }] },
            RuntimeRelease = new(1, previous, null, null), Status = RealmStatus.Pending
        };
        Assert.True(await repository.TryCreateAsync(realm, default));
        var service = new GameEntryService(repository, Options.Create(new GameEntryOptions()));
        var catalogue = await service.ListAsync(default);
        Assert.Equal("new-island", Assert.Single(catalogue.Realms).ServerId);
        Assert.False(catalogue.Realms[0].Ready);
        Assert.Equal(new RetiringGameServer(realm.RealmId, oldServer.Id), Assert.Single(catalogue.RetiringServers!));
        Assert.True(RetiringGameServers.IsValid(catalogue));

        var completed = realm with { Status = RealmStatus.Ready, Version = realm.Version + 1,
            RuntimeRelease = realm.RuntimeRelease! with { Completed = true } };
        Assert.True(await repository.TryReplaceAsync(completed, realm.Version, default));
        Assert.Null((await service.ListAsync(default)).RetiringServers);

        var deleting = realm with { Status = RealmStatus.Deleting, Version = completed.Version + 1 };
        Assert.True(await repository.TryReplaceAsync(deleting, completed.Version, default));
        Assert.Empty((await service.ListAsync(default)).Realms);
        Assert.Null((await service.ListAsync(default)).RetiringServers);
    }
}
