using Arkya.Admin.Content;
using Arkya.Admin.Realms;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Inference;

public static class InferenceServiceExtensions
{
    public static IServiceCollection AddInferenceAdministration(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<InferenceApiOptions>().Bind(configuration.GetSection("InferenceApi"))
            .Validate(options => !options.Enabled || options.AdminKey.Length is >= 32 and <= 512,
                "InferenceApi:AdminKey must contain 32 to 512 characters.")
            .Validate(options => !options.Enabled ||
                ContentApiOptions.IsSafe(options.BaseUrl, environment.IsDevelopment(), options.AllowClusterHttp),
                "InferenceApi:BaseUrl requires HTTPS, Development loopback HTTP, or explicitly enabled Kubernetes service HTTP.")
            .Validate(options => !options.Enabled || string.IsNullOrEmpty(options.CertificateAuthorityFile) ||
                Path.IsPathFullyQualified(options.CertificateAuthorityFile),
                "InferenceApi:CertificateAuthorityFile must be an absolute path.")
            .ValidateOnStart();
        services.AddHttpClient<InferenceAdminClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<InferenceApiOptions>>().Value;
            if (options.Enabled) client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        })
        .ConfigurePrimaryHttpMessageHandler(provider =>
        {
            var options = provider.GetRequiredService<IOptions<InferenceApiOptions>>().Value;
            return RealmsHttpTransport.Create(options.Enabled ? options.CertificateAuthorityFile : "");
        })
        .RedactLoggedHeaders(["X-Arkya-Admin-Key"]);
        return services;
    }
}
