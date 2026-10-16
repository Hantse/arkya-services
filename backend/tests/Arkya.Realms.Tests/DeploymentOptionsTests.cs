using Arkya.Realms.Api.Application;

namespace Arkya.Realms.Tests;

public sealed class DeploymentOptionsTests
{
    [Theory]
    [InlineData("realm-id\n")]
    [InlineData("realm-id\r\n")]
    [InlineData("realm-id\0")]
    public void RejectsTrailingControlCharacters(string value)
    {
        Assert.False(DeploymentOptions.ValidId(value));
        Assert.False(DeploymentOptions.ValidNamespace(value));
        Assert.False(DeploymentOptions.ValidSecret(value));
        var options = Fixtures.Options();
        options.Profiles[0].Image += "\n";
        Assert.False(options.IsValid());
    }

    [Fact]
    public void AcceptsDisabledEmptyConfigurationAndOptionalEmptySecrets()
    {
        Assert.True(new DeploymentOptions().IsValid());
        var options = Fixtures.Options();
        options.Profiles[0].EnvironmentSecretName = "";
        options.Profiles[0].ImagePullSecretName = "";
        Assert.True(options.IsValid());
        Assert.Null(options.Profiles[0].Snapshot().EnvironmentSecretName);
    }

    [Fact]
    public void RejectsMutableImagesReservedNamespacesDuplicatePortsAndInvalidUrls()
    {
        var options = Fixtures.Options();
        Assert.True(options.IsValid());
        options.Profiles[0].Image = "ghcr.io/example/dedicated:latest";
        Assert.False(options.IsValid());
        options.Profiles[0].Image = Fixtures.Image;
        options.Targets[0].Namespace = "kube-system";
        Assert.False(options.IsValid());
        options.Targets[0].Namespace = "arkya-realm-test";
        options.ApiServer = "https://user:password@cluster.example";
        Assert.False(options.IsValid());
        options.ApiServer = "http://cluster.example";
        Assert.False(options.IsValid());
        options.ApiServer = "https://cluster.example";
        options.Profiles[0].Servers = [new() { Id = "area-west", Port = 17843 }, new() { Id = "area-east", Port = 17843 }];
        Assert.False(options.IsValid());
    }
}
