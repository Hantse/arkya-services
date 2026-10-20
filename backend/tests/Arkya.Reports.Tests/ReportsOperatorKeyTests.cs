using System.Net;
using Arkya.Reports.Api.Infrastructure;
using Arkya.Reports.Api.Infrastructure.Identity;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Reports.Tests;

public sealed class ReportsOperatorKeyTests
{
    [Fact]
    public async Task ReportsAcceptsTheDerivedAdminKeyAndRejectsServiceMcpAndSeedCredentials()
    {
        const string seed = "administrator-only-reports-seed-in-an-existing-secret";
        await using var factory = new ReportsFactory();
        factory.Settings["ReportsOperator:KeySeed"] = seed;
        factory.Settings["ReportsAdmin:Key"] = "";
        factory.Settings["ServiceAuthentication:Key"] = "";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ReportsOperator:KeySeed"] = seed }).Build();
        var adminKey = ReportsOperatorKeys.ResolveAdmin(configuration, "");
        var serviceKey = ReportsOperatorKeys.ResolveService(configuration, "");
        using var client = factory.Client();
        Assert.Equal(adminKey, factory.Services.GetRequiredService<IOptions<ReportsAdminOptions>>().Value.Key);
        Assert.Equal(serviceKey, factory.Services.GetRequiredService<IOptions<ServiceAuthenticationOptions>>().Value.Key);
        Assert.Equal(ReportsFactory.ServiceKey, factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.ServiceKey);
        foreach (var key in new[] { seed, serviceKey, ReportsOperatorKeys.ResolveMcp(configuration, ""), adminKey })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/bug-reports");
            request.Headers.Add(ReportsAdminKeyValidator.HeaderName, key);
            using var response = await client.SendAsync(request);
            Assert.Equal(key == adminKey ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
