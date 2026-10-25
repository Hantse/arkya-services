using System.Net;
using System.Text;
using System.Text.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmNativeReleaseCatalogTests
{
    private static string Image(char value) => NativePublishedRelease.ImageRepository + "@sha256:" + new string(value, 64);
    private static NativePublishedRelease Release(long version = 62) => new($"0.1.{version}", new string('a', 40),
        (40000000000 + version).ToString(System.Globalization.CultureInfo.InvariantCulture), Image(version == 62 ? 'b' : 'c'), 9, "arkya-craft-progression-v2");
    private static readonly RealmTopology Topology = new([new("starter-island", 4, 1536, 1536, 0, 0, 2, 2)]);

    private static DeploymentProfile Profile(int protocol) => new()
    {
        Id = $"nova-native-v{protocol}", DisplayName = "Nova native", Image = Image('d'),
        EnvironmentSecretName = "nova-environment", ImagePullSecretName = "arkya-ghcr", TopologyTemplateServerId = "island",
        TopologyServerIdPrefix = $"v{protocol}", TopologyPublicEndpointTemplate = "wss://api.arkya.online/game/{realmId}/{serverId}/",
        Servers = [new() { Id = "island", Port = 17943, Transport = "WebSocket", PublicEndpoint = "wss://api.arkya.online/game/nova/island/",
            ProtocolVersion = protocol, BuildId = protocol == 5 ? "arkya-town-npcs-v1" : "arkya-craft-progression-v2", Args = ["-batchmode"] }]
    };

    private static RealmNativeReleaseOptions Settings() => new()
    {
        Enabled = true, Targets = [new() { RealmId = "nova", ProfileId = "nova-native-v9" }], InitialRelease = Release()
    };

    private static (RealmNativeReleaseCatalog Catalog, DeploymentOptions Options, RealmRecord Realm) Fixture()
    {
        var options = new DeploymentOptions
        {
            Enabled = true, Targets = [new() { Id = "realm-nova", DisplayName = "Nova", Namespace = "arkya-beta-realm", RealmId = "nova" }],
            Profiles = [Profile(5), Profile(9)],
            RuntimeReleases = [new() { RealmId = "nova", ProfileId = "nova-native-v5", Version = 1, Automatic = false,
                PreviousProfileSha256 = new string('f', 64), Topology = Topology, GameVersion = "0.1.49" }]
        };
        var realm = Fixtures.Realm(RealmStatus.Ready) with
        {
            RealmId = "nova", TargetId = "realm-nova", Namespace = "arkya-beta-realm",
            Profile = RealmTopologyBuilder.Snapshot(options.Profiles[0], "nova", Topology)
        };
        return (new(Options.Create(Settings())), options, realm);
    }

    private static RealmRecord Requested(RealmNativeReleaseCatalog catalog, DeploymentOptions options, RealmRecord realm)
    {
        var selected = catalog.Select(options, realm);
        var release = Assert.Single(selected.Options.RuntimeReleases);
        var profile = RealmRuntimeReleases.ApprovedProfile(selected.Options, realm, release)!;
        return RealmRuntimeReleases.BeginTransition(realm, release, profile, new TestClock().Now, "admin:operator", "Install approved runtime")
            with { NativeRelease = selected.Approval };
    }

    [Fact]
    public void ValidPublicationProposesManualMigrationWithoutChangingTheBaseOptionsOrRealm()
    {
        var (catalog, options, realm) = Fixture();
        var before = JsonSerializer.Serialize(options);
        var selection = catalog.Select(options, realm);
        var pin = Assert.IsType<RealmNativeReleasePin>(selection.Approval);
        Assert.Equal("RuntimeMigration", pin.Kind);
        Assert.Equal("0.1.62", pin.Release.GameVersion);
        Assert.Equal(RealmRuntimeReleases.Fingerprint(realm.Profile), pin.PreviousProfileSha256);
        var release = Assert.Single(selection.Options.RuntimeReleases);
        Assert.Equal(62, release.Version);
        Assert.False(release.Automatic);
        Assert.Equal("0.1.62", release.GameVersion);
        Assert.True(selection.Options.IsValid());
        var next = RealmRuntimeReleases.ApprovedProfile(selection.Options, realm, release)!;
        Assert.True(RealmRuntimeReleases.CompatibleTransition(realm.Profile, next));
        Assert.All(next.Servers, server => Assert.StartsWith("v9-", server.Id));
        Assert.Equal(realm.Profile.Servers.Select(server => server.Bounds), next.Servers.Select(server => server.Bounds));
        Assert.Null(realm.NativeRelease);
        Assert.Equal(RealmStatus.Ready, realm.Status);
        Assert.Equal(before, JsonSerializer.Serialize(options));
        Assert.Same(options, catalog.Select(options, realm, propose: false).Options);
    }

    [Fact]
    public void NewerPublicationAndApiRestartDoNotReplaceAnInFlightOrInstalledPin()
    {
        var (catalog, options, realm) = Fixture();
        var pending = Requested(catalog, options, realm);
        Assert.True(catalog.Accept(Release(63)));
        var selected = catalog.Select(options, pending, propose: false);
        Assert.Equal(62, selected.Approval!.Release.Version);
        Assert.True(RealmRuntimeReleases.Authorizes(selected.Options, pending));
        var afterRestart = new RealmNativeReleaseCatalog(Options.Create(withInitial(Release(63))));
        var restored = afterRestart.Select(options, pending with { Status = RealmStatus.Failed });
        Assert.Equal(pending.NativeRelease, restored.Approval);
        Assert.True(RealmRuntimeReleases.Authorizes(restored.Options, pending));
        var ready = pending with { Status = RealmStatus.Ready, RuntimeRelease = pending.RuntimeRelease! with { Completed = true } };
        Assert.Equal(62, afterRestart.Select(options, ready, propose: false).Approval!.Release.Version);
        var offered = afterRestart.Select(options, ready);
        Assert.Equal("Image", offered.Approval!.Kind);
        Assert.Equal(63, offered.Approval.Release.Version);
        Assert.False(Assert.Single(offered.Options.ImageReleases).Automatic);

        static RealmNativeReleaseOptions withInitial(NativePublishedRelease release)
        {
            var settings = Settings();
            settings.InitialRelease = release;
            return settings;
        }
    }

    [Fact]
    public void SubsequentImageUpdateKeepsIdsAndItsPinWhileLaterBuildsArePublished()
    {
        var (catalog, options, realm) = Fixture();
        var first = Requested(catalog, options, realm);
        var ready = first with { Status = RealmStatus.Ready, RuntimeRelease = first.RuntimeRelease! with { Completed = true } };
        catalog.Accept(Release(63));
        var selected = catalog.Select(options, ready);
        var pin = selected.Approval!;
        var pending = ready with
        {
            Status = RealmStatus.Pending, Profile = ready.Profile with { Image = pin.Release.Image }, NativeRelease = pin,
            ImageRelease = new(pin.Release.Version, ready.Profile.Image, pin.Release.Image)
        };
        var later = Release(64) with { Image = Image('e') };
        catalog.Accept(later);
        var restored = catalog.Select(options, pending, propose: false);
        Assert.Equal(63, restored.Approval!.Release.Version);
        Assert.True(RealmImageReleases.Authorizes(restored.Options, pending));
        Assert.Equal(ready.Profile.Servers, pending.Profile.Servers);
        Assert.Equal(ready.Profile.EnvironmentSecretName, pending.Profile.EnvironmentSecretName);
    }

    [Theory]
    [InlineData("target-hash")]
    [InlineData("previous-hash")]
    [InlineData("previous-image")]
    [InlineData("release-image")]
    [InlineData("kind")]
    [InlineData("profile")]
    [InlineData("template")]
    [InlineData("target")]
    public void InvalidPersistedPinsFailClosedInsteadOfFallingBackToStaticApproval(string change)
    {
        var (catalog, options, realm) = Fixture();
        var pending = Requested(catalog, options, realm);
        pending = change switch
        {
            "target-hash" => pending with { NativeRelease = pending.NativeRelease! with { TargetProfileSha256 = new string('e', 64) } },
            "previous-hash" => pending with { NativeRelease = pending.NativeRelease! with { PreviousProfileSha256 = new string('e', 64) } },
            "previous-image" => pending with { NativeRelease = pending.NativeRelease! with { PreviousImage = Image('e') } },
            "release-image" => pending with { NativeRelease = pending.NativeRelease! with { Release = Release() with { Image = Image('e') } } },
            "kind" => pending with { NativeRelease = pending.NativeRelease! with { Kind = "Automatic" } },
            "profile" => pending with { NativeRelease = pending.NativeRelease! with { ProfileId = "other-profile" } },
            _ => pending
        };
        if (change == "template") options.Profiles[1].EnvironmentSecretName = "other-secret";
        if (change == "target") options.Targets = [];
        var result = catalog.Select(options, pending, propose: false);
        Assert.Null(result.Approval);
        Assert.False(result.Options.Enabled);
        Assert.DoesNotContain(result.Options.RuntimeReleases, release => release.RealmId == "nova");
        Assert.False(RealmRuntimeReleases.Authorizes(result.Options, pending));
    }

    [Fact]
    public void CatalogueIsMonotoneAndRejectsConflictingVersionsAndUnsupportedReleases()
    {
        var (catalog, _, _) = Fixture();
        Assert.False(catalog.Accept(Release(61)));
        Assert.False(catalog.Accept(Release() with { SourceRunId = "9999" }));
        Assert.False(catalog.Accept(Release() with { Image = Image('f') }));
        Assert.False(catalog.Accept(Release(63) with { ProtocolVersion = 99 }));
        Assert.True(catalog.Accept(Release()));
        Assert.True(catalog.Accept(Release(63)));
        Assert.Equal(63, catalog.Current!.Version);
    }

    [Fact]
    public void UnknownRealmOrUnpreparedNetworkTemplateDoesNotReceiveACandidate()
    {
        var (catalog, options, realm) = Fixture();
        Assert.Null(catalog.Select(options, realm with { RealmId = "other-realm" }).Approval);
        options.Profiles[1].Servers[0].BuildId = "unprepared-network";
        Assert.Null(catalog.Select(options, realm).Approval);
        Assert.Same(options, catalog.Select(options, realm).Options);
    }

    [Fact]
    public void ConfigurationRejectsAmbiguousTargetsInvalidNamesIntervalsAndSeeds()
    {
        var settings = Settings();
        Assert.True(settings.IsValid());
        settings.Targets = [settings.Targets[0], settings.Targets[0]];
        Assert.False(settings.IsValid());
        settings = Settings(); settings.ConfigMapName = "../secrets"; Assert.False(settings.IsValid());
        settings = Settings(); settings.PollIntervalSeconds = 1; Assert.False(settings.IsValid());
        settings = Settings(); settings.InitialRelease = Release() with { GameVersion = "0.1.9223372036854775808" }; Assert.False(settings.IsValid());
        settings = Settings(); settings.Targets = []; Assert.False(settings.IsValid());
    }

    private static string Inventory(Action<Dictionary<string, string>>? edit = null)
    {
        var release = Release();
        var data = new Dictionary<string, string>
        {
            ["schemaVersion"] = "1", ["channel"] = "beta", ["adoption"] = "manual", ["dedicatedImage"] = release.Image,
            ["sourceRevision"] = release.SourceRevision, ["windowsClientSourceRevision"] = release.SourceRevision,
            ["protocolVersion"] = "9", ["buildId"] = release.BuildId, ["buildVersion"] = release.GameVersion, ["sourceRunId"] = release.SourceRunId
        };
        edit?.Invoke(data);
        return JsonSerializer.Serialize(new { apiVersion = "v1", kind = "ConfigMap",
            metadata = new { name = "arkya-native-runtime-release", @namespace = "arkya-beta" }, data });
    }

    [Fact]
    public void ConfigMapReaderAcceptsOnlyTheNativePublicationIdentityAndCompleteMatchingMetadata()
    {
        Assert.Equal(Release(), KubernetesNativeReleaseSource.Parse(Inventory()));
        foreach (var edit in new Action<Dictionary<string, string>>[]
        {
            data => data["adoption"] = "automatic", data => data["channel"] = "stable", data => data["schemaVersion"] = "2",
            data => data["windowsClientSourceRevision"] = new string('f', 40), data => data["dedicatedImage"] = "ghcr.io/other/image@sha256:" + new string('a', 64),
            data => data["protocolVersion"] = "09", data => data["protocolVersion"] = "99", data => data["buildVersion"] = "0.1.0",
            data => data["sourceRunId"] = "../run", data => data.Remove("buildId"), data => data["extra"] = "untrusted"
        }) Assert.Throws<JsonException>(() => KubernetesNativeReleaseSource.Parse(Inventory(edit)));
        Assert.Throws<JsonException>(() => KubernetesNativeReleaseSource.Parse(Inventory(), "other-namespace"));
        Assert.Throws<JsonException>(() => KubernetesNativeReleaseSource.Parse(Inventory().Replace("\"channel\":\"beta\"", "\"channel\":\"beta\",\"channel\":\"beta\"")));
    }

    [Fact]
    public async Task SourceUsesOnlyAnAuthenticatedNamedConfigMapGetAndHonorsCancellation()
    {
        var tokenFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tokenFile, "service-account-token");
            var handler = new SourceHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("https://kubernetes.default.svc/api/v1/namespaces/arkya-beta/configmaps/arkya-native-runtime-release", request.RequestUri!.AbsoluteUri);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal("service-account-token", request.Headers.Authorization.Parameter);
                return new(HttpStatusCode.OK) { Content = new StringContent(Inventory(), Encoding.UTF8, "application/json") };
            });
            using var client = new HttpClient(handler);
            var deployment = new DeploymentOptions { Enabled = true, TokenFile = tokenFile };
            var source = new KubernetesNativeReleaseSource(client, Options.Create(deployment), Options.Create(Settings()));
            Assert.Equal(Release(), await source.ReadAsync(default));
            deployment.Enabled = false;
            Assert.Null(await source.ReadAsync(default));
            Assert.Equal(1, handler.Calls);
            deployment.Enabled = true;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(cancelled.Token));
            Assert.Equal(1, handler.Calls);
        }
        finally { File.Delete(tokenFile); }
    }

    private sealed class SourceHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
}
