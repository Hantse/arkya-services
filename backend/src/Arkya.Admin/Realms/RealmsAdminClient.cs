using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Realms;

public sealed class RealmsApiOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "https://localhost:7144";
    public string AdminKey { get; init; } = "";
    public bool AllowClusterHttp { get; init; }
    public string CertificateAuthorityFile { get; init; } = "";
}

public sealed record RealmServer(string Id, int Port, int ReadyReplicas, int DesiredReplicas, string? Address,
    string Transport = "Udp", string? PublicEndpoint = null);
public sealed record RealmSummary(string RealmId, string DisplayName, string TargetId, string Namespace,
    string ProfileId, string Image, long Revision, string Status, string? LastError, DateTimeOffset UpdatedAt, RealmServer[] Servers);
public sealed record RealmTarget(string Id, string DisplayName, string Namespace);
public sealed record RealmProfileServer(string Id, int Port, string Transport = "Udp", string? PublicEndpoint = null);
public sealed record RealmProfile(string Id, string DisplayName, string Image, RealmProfileServer[] Servers);
public sealed record RealmDeploymentOptions(RealmTarget[] Targets, RealmProfile[] Profiles, bool DeploymentEnabled);
public sealed record CreateRealmRequest(string RealmId, string DisplayName, string TargetId, string ProfileId);

public sealed class RealmsApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class RealmsAdminClient(HttpClient client, IOptions<RealmsApiOptions> options)
{
    public bool Enabled => options.Value.Enabled;
    public Task<RealmSummary[]> ListAsync(CancellationToken ct) => SendAsync<RealmSummary[]>(HttpMethod.Get, "admin/realms", null, ct);
    public Task<RealmDeploymentOptions> OptionsAsync(CancellationToken ct) => SendAsync<RealmDeploymentOptions>(HttpMethod.Get, "admin/realms/options", null, ct);
    public Task<RealmSummary> GetAsync(string realmId, CancellationToken ct) => SendAsync<RealmSummary>(HttpMethod.Get,
        "admin/realms/" + Uri.EscapeDataString(realmId), null, ct);
    public Task<RealmSummary> CreateAsync(CreateRealmRequest realm, CancellationToken ct) => SendAsync<RealmSummary>(HttpMethod.Post, "admin/realms", realm, ct);
    public Task<RealmSummary> DeployAsync(string realmId, long expectedRevision, CancellationToken ct) => SendAsync<RealmSummary>(HttpMethod.Post,
        "admin/realms/" + Uri.EscapeDataString(realmId) + "/deploy", new { expectedRevision }, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Enabled) throw new RealmsApiException(HttpStatusCode.ServiceUnavailable, "La gestion des realms est désactivée sur cette installation.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Conflict => "Ce realm existe déjà, a changé ou possède un déploiement en cours. Actualisez avant de réessayer.",
                HttpStatusCode.BadRequest => "Vérifiez les champs et choisissez une cible et un profil disponibles.",
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "L'accès aux realms a été refusé. Vérifiez la configuration de l'administration.",
                HttpStatusCode.NotFound => "Ce realm n'existe pas ou n'est plus disponible.",
                _ => "Le service des realms est indisponible. Réessayez dans un instant."
            };
            throw new RealmsApiException(response.StatusCode, message);
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new RealmsApiException(HttpStatusCode.BadGateway, "Le service des realms a renvoyé une réponse vide.");
        }
        catch (JsonException)
        {
            throw new RealmsApiException(HttpStatusCode.BadGateway, "Le service des realms a renvoyé une réponse invalide.");
        }
    }
}

public static class RealmsHttpTransport
{
    public static SocketsHttpHandler Create(string certificateAuthorityFile)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        if (!string.IsNullOrEmpty(certificateAuthorityFile))
        {
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                // The private cluster CA does not publish an OCSP or CRL endpoint.
                RevocationMode = X509RevocationMode.NoCheck
            };
            policy.CustomTrustStore.Add(X509Certificate2.CreateFromPem(File.ReadAllText(certificateAuthorityFile)));
            handler.SslOptions.CertificateChainPolicy = policy;
        }
        // The platform still validates the server name and the full certificate chain.
        return handler;
    }
}

public static class RealmPresentation
{
    public static string FailureMessage(string? code) => code switch
    {
        "target_unavailable" => "La cible de déploiement n'est plus configurée. Rétablissez cette cible dans le service Realms.",
        "deployment_progress_failed" => "Le cluster signale un échec de démarrage. Vérifiez les événements et les journaux des serveurs dédiés.",
        "resource_conflict" => "Une ressource a changé pendant sa création. Vérifiez les ressources du realm avant de réessayer.",
        "resource_deleting" => "Une ressource du realm est en cours de suppression. Attendez la fin de cette opération avant de réessayer.",
        "resource_ownership_conflict" => "Une ressource existante appartient à un autre propriétaire. Vérifiez les noms et les étiquettes dans le namespace cible.",
        "resource_configuration_drift" => "Les ressources du cluster diffèrent de la configuration enregistrée. Rétablissez la configuration attendue avant de réessayer.",
        "cluster_credentials_invalid" => "Les identifiants du cluster sont invalides. Vérifiez le jeton du compte de service.",
        "cluster_access_denied" => "Le cluster a refusé l'accès. Vérifiez le compte de service et ses droits dans le namespace cible.",
        "target_namespace_missing" => "Le namespace cible n'existe pas. Créez-le et configurez ses droits avant de réessayer.",
        "cluster_resource_rejected" => "Le cluster a refusé les ressources demandées. Vérifiez le profil et les contraintes du namespace cible.",
        "cluster_unavailable" => "Le cluster ne répond pas. Vérifiez sa disponibilité et la connexion du service Realms.",
        "cluster_response_invalid" => "Le cluster a renvoyé une réponse invalide. Vérifiez sa disponibilité avant de réessayer.",
        "deployment_timeout" => "Les serveurs ne sont pas devenus prêts dans le délai prévu. Vérifiez leur image, leurs ressources et les événements du cluster.",
        _ => "Le déploiement a échoué. Vérifiez les événements du cluster et la configuration du service Realms avant de réessayer."
    };
    public static bool CanDeploy(RealmSummary realm) => realm.Status is "Draft" or "Failed";
    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "Brouillon",
        "Pending" => "En attente",
        "Deploying" => "Déploiement en cours",
        "Ready" => "Déploiement prêt",
        "Failed" => "Déploiement en échec",
        _ => "État inconnu"
    };
    public static string StatusClass(string status) => status switch
    {
        "Ready" => "ready", "Failed" => "failed", "Pending" or "Deploying" => "pending", _ => "draft"
    };
    public static bool IsDependencyError(Exception ex, CancellationToken ct) => ex is RealmsApiException or HttpRequestException
        || ex is OperationCanceledException && !ct.IsCancellationRequested;
    public static string ErrorMessage(Exception ex) => ex is RealmsApiException ? ex.Message
        : "Le service des realms est indisponible. Réessayez dans un instant.";
}
