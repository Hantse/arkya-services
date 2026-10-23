using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Arkya.Realms.Tests;

public sealed class RealmImageReleaseOptionsTests
{
    private const string NextImage = "registry.example/arkya/dedicated@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public void ExistingConfigurationHasNoImplicitImageRelease()
    {
        var disabled = new DeploymentOptions();
        Assert.Empty(disabled.ImageReleases);
        Assert.True(disabled.IsValid());
        var existing = Fixtures.Options();
        Assert.Empty(existing.ImageReleases);
        Assert.True(existing.IsValid());
    }

    [Fact]
    public void ImageReleaseAcceptsAnUnboundTargetButRejectsMismatchedOrDuplicatePins()
    {
        var options = ReleaseOptions();
        Assert.True(options.IsValid());
        options.Targets[0].RealmId = null;
        Assert.True(options.IsValid());
        options.Targets[0].RealmId = "eu-two";
        Assert.False(options.IsValid());
        options.Targets[0].RealmId = "eu-one";
        options.Targets = [options.Targets[0], new()
        {
            Id = "realm-eu-two", DisplayName = "EU two", Namespace = "arkya-realm-eu-two", RealmId = "eu-one"
        }];
        Assert.False(options.IsValid());
    }

    [Fact]
    public void ImageReleaseCanUseMultipleApprovedUnboundTargets()
    {
        var options = ReleaseOptions();
        options.Targets[0].RealmId = null;
        options.Targets = [options.Targets[0], new()
        {
            Id = "realm-eu-two", DisplayName = "EU two", Namespace = "arkya-realm-eu-two"
        }];
        Assert.True(options.IsValid());
        var realm = Fixtures.Realm() with { TargetId = "realm-eu-two", Namespace = "arkya-realm-eu-two" };
        Assert.Same(options.Profiles[0], RealmImageReleases.AllowedProfile(options, realm, options.ImageReleases[0]));
    }

    [Theory]
    [InlineData("realm")]
    [InlineData("target")]
    [InlineData("namespace")]
    [InlineData("pin")]
    public void UnboundImageReleaseStillRequiresItsRealmAndExactApprovedDestination(string change)
    {
        var options = ReleaseOptions();
        options.Targets[0].RealmId = null;
        var realm = Fixtures.Realm();
        var release = options.ImageReleases[0];
        Assert.Same(options.Profiles[0], RealmImageReleases.AllowedProfile(options, realm, release));
        switch (change)
        {
            case "realm": realm = realm with { RealmId = "another-realm" }; break;
            case "target": realm = realm with { TargetId = "another-target" }; break;
            case "namespace": realm = realm with { Namespace = "another-namespace" }; break;
            case "pin": options.Targets[0].RealmId = "another-realm"; break;
        }
        Assert.Null(RealmImageReleases.AllowedProfile(options, realm, release));
    }

    [Fact]
    public void ImageReleaseRequiresAnAllowlistedProfileAndADifferentDesiredImage()
    {
        var options = ReleaseOptions();
        options.ImageReleases[0].ProfileId = "unknown-profile";
        Assert.False(options.IsValid());
        options.ImageReleases[0].ProfileId = options.Profiles[0].Id;
        options.Profiles[0].Image = Fixtures.Image;
        Assert.False(options.IsValid());
        options.Profiles[0].Image = NextImage;
        Assert.True(options.IsValid());
    }

    [Theory]
    [InlineData("registry.example/arkya/dedicated:latest")]
    [InlineData("registry.example/arkya/dedicated@sha256:abc")]
    [InlineData("registry.example/arkya/dedicated@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\n")]
    public void ImageReleaseRejectsUnpinnedOrMalformedPreviousAndDesiredImages(string image)
    {
        var options = ReleaseOptions();
        options.ImageReleases[0].PreviousImage = image;
        Assert.False(options.IsValid());
        options.ImageReleases[0].PreviousImage = Fixtures.Image;
        options.Profiles[0].Image = image;
        Assert.False(options.IsValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ImageReleaseVersionMustBePositive(long version)
    {
        var options = ReleaseOptions();
        options.ImageReleases[0].Version = version;
        Assert.False(options.IsValid());
    }

    [Fact]
    public void ImageReleaseRejectsDuplicateRealmRequestsAndNullEntries()
    {
        var options = ReleaseOptions();
        options.ImageReleases = [options.ImageReleases[0], new()
        {
            RealmId = "eu-one", ProfileId = "island-v1", Version = 2, PreviousImage = Fixtures.Image
        }];
        Assert.False(options.IsValid());
        options.ImageReleases = [null!];
        Assert.False(options.IsValid());
        options.ImageReleases = null!;
        Assert.False(options.IsValid());
    }

    [Fact]
    public void RealmImageTransitionAndObservedImageRoundTripThroughBson()
    {
        var original = Fixtures.Realm(RealmStatus.Deploying) with
        {
            Profile = Fixtures.Realm().Profile with { Image = NextImage },
            ImageRelease = new RealmImageTransition(3, Fixtures.Image, NextImage, 2),
            Servers = [new("area-west", 1, "192.0.2.42") { ReadyImage = NextImage }]
        };
        var bson = RealmDocument.From(original).ToBsonDocument();
        var restored = BsonSerializer.Deserialize<RealmDocument>(bson).ToRecord();
        Assert.Equal(original.ImageRelease, restored.ImageRelease);
        Assert.Equal(3, bson["ImageRelease"]["Version"].AsInt64);
        Assert.Equal(2, bson["ImageRelease"]["PreviousVersion"].AsInt64);
        Assert.Equal(NextImage, restored.Profile.Image);
        Assert.Equal(original.Servers, restored.Servers);
        Assert.Equal(NextImage, Assert.Single(restored.Servers).ReadyImage);
        Assert.Equal(original.DeploymentIdentity, restored.DeploymentIdentity);
        Assert.Equal(original.Revision, restored.Revision);
        Assert.Equal(original.Version, restored.Version);
    }

    [Fact]
    public void LegacyRealmDocumentsWithoutReleaseOrObservedImageRemainReadable()
    {
        var original = Fixtures.Realm(RealmStatus.Ready) with { Servers = [new("area-west", 1, "192.0.2.42")] };
        var bson = RealmDocument.From(original).ToBsonDocument();
        bson.Remove("ImageRelease");
        bson["Servers"].AsBsonArray[0].AsBsonDocument.Remove("ReadyImage");
        var restored = BsonSerializer.Deserialize<RealmDocument>(bson).ToRecord();
        Assert.Null(restored.ImageRelease);
        Assert.Null(Assert.Single(restored.Servers).ReadyImage);
        Assert.Equal(original.Profile.Image, restored.Profile.Image);
        Assert.Equal(original.Status, restored.Status);
        Assert.Equal(original.DeploymentIdentity, restored.DeploymentIdentity);
    }

    private static DeploymentOptions ReleaseOptions()
    {
        var options = Fixtures.Options();
        options.Targets[0].RealmId = "eu-one";
        options.Profiles[0].Image = NextImage;
        options.ImageReleases = [new()
        {
            RealmId = "eu-one", ProfileId = "island-v1", Version = 1, PreviousImage = Fixtures.Image
        }];
        return options;
    }
}
