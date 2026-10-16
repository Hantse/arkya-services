using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class AdminRuntimeTests
{
    [Theory]
    [InlineData("10.42.1.9", true)]
    [InlineData("::ffff:10.42.1.9", true)]
    [InlineData("192.168.100.9", false)]
    public async Task OnlyTrustedGatewayCanRestoreProductionHttps(string source, bool accepted)
    {
        await using var factory = new AdminFactory { EnvironmentName = "Production" };
        factory.Settings["ProxyTrust:KnownNetworks:0"] = "10.42.0.0/16";
        var result = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(source);
            context.Request.Host = new HostString("localhost");
            context.Request.Method = "GET";
            context.Request.Path = "/Login";
            context.Request.Scheme = "http";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.5";
            context.Request.Headers["X-Forwarded-Host"] = "attacker.invalid";
        });
        Assert.Equal(accepted ? 200 : 400, result.Response.StatusCode);
        Assert.Equal("localhost", result.Request.Host.Host);
        Assert.Equal(accepted ? "203.0.113.5" : source, result.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task ProductionDoesNotTrustForwardedHttpsWithoutExplicitProxies()
    {
        await using var factory = new AdminFactory { EnvironmentName = "Production" };
        var result = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Host = new HostString("localhost");
            context.Request.Method = "GET";
            context.Request.Path = "/health";
            context.Request.Scheme = "http";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        Assert.Equal(400, result.Response.StatusCode);
    }

    [Theory]
    [InlineData("DataProtection:KeysPath", "relative/keys")]
    [InlineData("ProxyTrust:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("RealmsApi:CertificateAuthorityFile", "relative/ca.pem")]
    public void InvalidDeploymentConfigurationFailsStartup(string key, string value)
    {
        using var factory = new AdminFactory { EnvironmentName = "Production" };
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Settings[key] = value;
        Assert.Throws<OptionsValidationException>(() => factory.Browser());
    }

    [Fact]
    public async Task ProtectionKeysSurviveAReplacementAdminProcess()
    {
        var keysPath = Path.Combine(Path.GetTempPath(), "arkya-admin-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string ticket;
            await using (var first = new AdminFactory())
            {
                first.Settings["DataProtection:KeysPath"] = keysPath;
                ticket = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("deployment-test").Protect("admin-cookie-state");
            }
            await using var replacement = new AdminFactory();
            replacement.Settings["DataProtection:KeysPath"] = keysPath;
            Assert.Equal("admin-cookie-state", replacement.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("deployment-test").Unprotect(ticket));
        }
        finally
        {
            if (Directory.Exists(keysPath))
            {
                foreach (var file in Directory.GetFiles(keysPath, "key-*.xml")) File.Delete(file);
                Directory.Delete(keysPath);
            }
        }
    }
}
