using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Arkya.Realms.Api.Application;

namespace Arkya.Realms.Api.Infrastructure;

public static class KubernetesTransport
{
    public static SocketsHttpHandler CreateHandler(DeploymentOptions options)
    {
        var roots = new X509Certificate2Collection();
        roots.ImportFromPem(File.ReadAllText(options.CertificateAuthorityFile));
        if (roots.Count == 0) throw new InvalidOperationException("Kubernetes CA file contains no certificates.");
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true
        };
        policy.CustomTrustStore.AddRange(roots);
        policy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // Chain trust changes; SslStream still performs its normal hostname validation.
            SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy }
        };
    }
}
