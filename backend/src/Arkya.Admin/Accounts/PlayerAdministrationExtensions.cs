using Arkya.Admin.Content;
using Arkya.Admin.Realms;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Accounts;

public static class PlayerAdministrationExtensions
{
    public static IServiceCollection AddPlayerAdministration(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        Configure<IdentityAdminApiOptions>(services, configuration, environment, "IdentityAdminApi");
        Configure<CharactersAdminApiOptions>(services, configuration, environment, "CharactersAdminApi");
        AddClient<IdentityAdminClient, IdentityAdminApiOptions>(services);
        AddClient<CharactersAdminClient, CharactersAdminApiOptions>(services);
        return services;
    }

    private static void Configure<TOptions>(IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment, string section) where TOptions : PlayerAdminApiOptions
    {
        services.AddOptions<TOptions>().Bind(configuration.GetSection(section))
            .Validate(options => !options.Enabled || options.AdminKey.Length is >= 32 and <= 512,
                $"{section}:AdminKey must contain 32 to 512 characters.")
            .Validate(options => !options.Enabled || ContentApiOptions.IsSafe(options.BaseUrl, environment.IsDevelopment(), options.AllowClusterHttp),
                $"{section}:BaseUrl requires HTTPS, Development loopback HTTP, or explicitly enabled Kubernetes service HTTP.")
            .Validate(options => !options.Enabled || string.IsNullOrEmpty(options.CertificateAuthorityFile) || Path.IsPathFullyQualified(options.CertificateAuthorityFile),
                $"{section}:CertificateAuthorityFile must be an absolute path.")
            .ValidateOnStart();
    }

    private static void AddClient<TClient, TOptions>(IServiceCollection services)
        where TClient : class where TOptions : PlayerAdminApiOptions
    {
        services.AddHttpClient<TClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TOptions>>().Value;
            if (options.Enabled) client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        })
        .ConfigurePrimaryHttpMessageHandler(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TOptions>>().Value;
            return RealmsHttpTransport.Create(options.Enabled ? options.CertificateAuthorityFile : "");
        })
        .RedactLoggedHeaders(["X-Arkya-Admin-Key"]);
    }
}
