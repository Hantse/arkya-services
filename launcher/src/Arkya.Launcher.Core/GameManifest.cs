using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Arkya.Launcher.Core;

public sealed record GameManifest(
    int SchemaVersion,
    string Version,
    string Platform,
    string PackageUrl,
    long PackageSize,
    long UnpackedSize,
    string Sha256,
    string Executable,
    string? ReleaseNotes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int LauncherSessionProtocol = 0)
{
    public const long MaximumPackageSize = 50L * 1024 * 1024 * 1024;
    public const long MaximumUnpackedSize = 100L * 1024 * 1024 * 1024;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public void Validate()
    {
        if (SchemaVersion != 1 || Platform != "win-x64")
            throw new InvalidDataException("Cette version du jeu n’est pas compatible avec ce launcher Windows.");
        if (LauncherSessionProtocol is not 0 and not 1)
            throw new InvalidDataException("Le protocole de connexion de cette version nécessite un autre launcher.");
        if (Version is null || !Regex.IsMatch(Version, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}\z"))
            throw new InvalidDataException("Identifiant de version invalide.");
        ValidateHttpsUrl(PackageUrl);
        if (PackageSize <= 0 || PackageSize > MaximumPackageSize || UnpackedSize <= 0 || UnpackedSize > MaximumUnpackedSize)
            throw new InvalidDataException("Taille du jeu invalide.");
        if (Sha256 is null || !Regex.IsMatch(Sha256, @"\A[a-fA-F0-9]{64}\z"))
            throw new InvalidDataException("Empreinte de téléchargement invalide.");
        SafePaths.ValidateRelative(Executable);
        if (!Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le programme de lancement doit être un exécutable Windows.");
        if (ReleaseNotes?.Length > 16000)
            throw new InvalidDataException("Notes de version trop longues.");
    }

    public static Uri ValidateHttpsUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Une adresse HTTPS sans identifiants est nécessaire.");
        return uri;
    }
}

public sealed record InstalledGame(GameManifest Manifest, string DirectoryName, string? Channel = null);
public sealed record InstallProgress(string Message, long CompletedBytes = 0, long TotalBytes = 0)
{
    public double Percentage => TotalBytes > 0 ? Math.Min(100, CompletedBytes * 100d / TotalBytes) : 0;
}
