using System.Net;
using System.Text.Json;
using Arkya.ServiceDefaults.Accounts;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Accounts;

public abstract class PlayerAdminApiOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "";
    public string AdminKey { get; init; } = "";
    public bool AllowClusterHttp { get; init; }
    public string CertificateAuthorityFile { get; init; } = "";
}

public sealed class IdentityAdminApiOptions : PlayerAdminApiOptions;
public sealed class CharactersAdminApiOptions : PlayerAdminApiOptions;

public sealed class PlayerAdminApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class IdentityAdminClient(HttpClient client, IOptions<IdentityAdminApiOptions> options)
{
    public bool Enabled => options.Value.Enabled;
    public Task<AdminAccountPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct) =>
        SendAsync<AdminAccountPage>(HttpMethod.Get,
            $"admin/accounts?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? "")}", null, null, ct);
    public Task<AdminAccountSummary> GetAsync(Guid id, CancellationToken ct) =>
        SendAsync<AdminAccountSummary>(HttpMethod.Get, $"admin/accounts/{id:D}", null, null, ct);
    public Task<AdminAccountSummary> BanAsync(Guid id, long revision, string reason, string actor, CancellationToken ct) =>
        SendAsync<AdminAccountSummary>(HttpMethod.Post, $"admin/accounts/{id:D}/ban", new AccountBanRequest(revision, reason), actor, ct);
    public Task<AdminAccountSummary> UnbanAsync(Guid id, long revision, string reason, string actor, CancellationToken ct) =>
        SendAsync<AdminAccountSummary>(HttpMethod.Post, $"admin/accounts/{id:D}/unban", new AccountUnbanRequest(revision, reason), actor, ct);
    public Task<AdminAccountSummary> SetBetaAccessAsync(Guid id, long revision, bool hasBetaAccess, string reason, string actor, CancellationToken ct) =>
        SendAsync<AdminAccountSummary>(HttpMethod.Post, $"admin/accounts/{id:D}/beta-access",
            new AccountBetaAccessRequest(revision, hasBetaAccess, reason), actor, ct);

    private Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? actor, CancellationToken ct) =>
        PlayerAdminTransport.SendAsync<T>(client, options.Value, "comptes", method, path, body, actor, ct);
}

public sealed class CharactersAdminClient(HttpClient client, IOptions<CharactersAdminApiOptions> options)
{
    public bool Enabled => options.Value.Enabled;
    public Task<AdminCharacterPage> ListAsync(string? accountId, string? search, int page, int pageSize, bool onlineOnly, CancellationToken ct) =>
        PlayerAdminTransport.SendAsync<AdminCharacterPage>(client, options.Value, "personnages", HttpMethod.Get,
            $"admin/characters?page={page}&pageSize={pageSize}&accountId={Uri.EscapeDataString(accountId ?? "")}&search={Uri.EscapeDataString(search ?? "")}&onlineOnly={onlineOnly.ToString().ToLowerInvariant()}", null, null, ct);
    public Task<AdminCharacterOverview> OverviewAsync(CancellationToken ct) =>
        PlayerAdminTransport.SendAsync<AdminCharacterOverview>(client, options.Value, "personnages", HttpMethod.Get,
            "admin/characters/overview", null, null, ct);
}

internal static class PlayerAdminTransport
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<T> SendAsync<T>(HttpClient client, PlayerAdminApiOptions options, string resource,
        HttpMethod method, string path, object? body, string? actor, CancellationToken ct)
    {
        if (!options.Enabled)
            throw new PlayerAdminApiException(HttpStatusCode.ServiceUnavailable, $"La gestion des {resource} est désactivée sur cette installation.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Admin-Key", options.AdminKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (actor is not null) request.Headers.Add("X-Arkya-Admin-Actor", actor);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(client.Timeout);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode)
            throw new PlayerAdminApiException(response.StatusCode, response.StatusCode switch
            {
                HttpStatusCode.Conflict => "Ce compte a changé. La version actuelle a été rechargée ; vérifiez-la avant de réessayer.",
                HttpStatusCode.BadRequest => "Vérifiez les champs et renseignez un motif pour cette action.",
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "L'accès au service a été refusé. Vérifiez la configuration de l'administration.",
                HttpStatusCode.NotFound => "Ce compte ou personnage n'existe pas ou n'est plus disponible.",
                _ => $"Le service des {resource} est indisponible. Réessayez dans un instant."
            });
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes) throw InvalidResponse();
        await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (output.Length + count > MaximumResponseBytes) throw InvalidResponse();
            output.Write(buffer, 0, count);
        }
        try
        {
            var value = JsonSerializer.Deserialize<T>(output.GetBuffer().AsSpan(0, checked((int)output.Length)), JsonOptions) ?? throw InvalidResponse();
            if (!ValidResponse(value)) throw InvalidResponse();
            return value;
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static bool ValidResponse<T>(T value) => value switch
    {
        AdminAccountPage page => page.Items is not null && page.Items.Count <= 100 && page.Total >= 0 &&
            page.Page is >= 1 and <= 10000 && page.PageSize is >= 1 and <= 100 && page.Items.All(ValidAccount),
        AdminAccountSummary account => ValidAccount(account),
        AdminCharacterPage page => page.Items is not null && page.Items.Length <= 100 && page.Total >= 0 &&
            page.Page is >= 1 and <= 10000 && page.PageSize is >= 1 and <= 100 && page.Items.All(character => character is not null &&
                Guid.TryParse(character.CharacterId, out _) && Guid.TryParse(character.AccountId, out _) && character.DisplayName is not null),
        AdminCharacterOverview overview => overview.Realms is not null && overview.TotalCharacters >= 0 &&
            overview.OnlineCharacters >= 0 && overview.OnlineCharacters <= overview.TotalCharacters &&
            overview.Realms.All(realm => realm is not null && realm.RealmId is not null && realm.OnlineCharacters >= 0),
        _ => false
    };

    private static bool ValidAccount(AdminAccountSummary account) => account is not null &&
        Guid.TryParse(account.AccountId, out _) && account.DisplayName is not null && account.DiscordUserId is not null && account.Revision >= 0;

    private static PlayerAdminApiException InvalidResponse() =>
        new(HttpStatusCode.BadGateway, "Le service a renvoyé une réponse invalide. Réessayez dans un instant.");
}

public static class PlayerAdminPresentation
{
    public static bool IsDependencyError(Exception ex, CancellationToken ct) => ex is PlayerAdminApiException or HttpRequestException or IOException
        || ex is OperationCanceledException && !ct.IsCancellationRequested;
    public static string ErrorMessage(Exception ex) => ex is PlayerAdminApiException ? ex.Message
        : "Le service est indisponible. Réessayez dans un instant.";
    public static bool ValidSearch(string? search) => search is null || search.Length <= 100 && !search.Any(char.IsControl);
    public static bool ValidPagination(int page, int pageSize) => page is >= 1 and <= 10000 && pageSize is >= 1 and <= 100;
}
