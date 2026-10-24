using Arkya.ServiceDefaults;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Application;

public sealed class RealmStorageOptions
{
    public bool Enabled { get; set; }
    public RealmStorageRoute[] Realms { get; set; } = [];

    public bool IsValid(string directoryDatabase)
    {
        if (Realms is null || Realms.Length > 100) return false;
        if (Enabled && Realms.Length == 0) return false;
        if (Realms.Any(route => route is null || !GameProtocol.ValidId(route.RealmId) ||
            !ValidDatabaseName(route.DatabaseName) || string.Equals(route.DatabaseName, directoryDatabase, StringComparison.OrdinalIgnoreCase) ||
            !ValidConnection(route.ConnectionString))) return false;
        // Distinct names also prevent accidental aliasing through different Mongo credentials.
        return Realms.Select(route => route.RealmId).Distinct(StringComparer.Ordinal).Count() == Realms.Length &&
            Realms.Select(route => route.DatabaseName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Realms.Length;
    }

    private static bool ValidDatabaseName(string? value) => value is { Length: >= 1 and <= 63 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool ValidConnection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(char.IsControl)) return false;
        try { _ = MongoClientSettings.FromConnectionString(value); return true; }
        catch (Exception error) when (error is ArgumentException or FormatException or MongoConfigurationException) { return false; }
    }
}

public sealed class RealmStorageRoute
{
    public string RealmId { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string ConnectionString { get; set; } = "";
}

public interface IRealmStorageRouting
{
    bool Enabled { get; }
    bool IsConfigured(string realmId);
}

public sealed class RealmStorageUnavailableException : HttpRequestException
{
    public RealmStorageUnavailableException() : base("The character realm storage is not available.") { }
}
