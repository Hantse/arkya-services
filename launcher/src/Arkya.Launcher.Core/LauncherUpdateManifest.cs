using System.Text.RegularExpressions;

namespace Arkya.Launcher.Core;

public sealed record LauncherUpdateManifest(int SchemaVersion, string Version, string Platform,
    string InstallerUrl, long InstallerSize, string Sha256)
{
    public const long MaximumInstallerSize = 512L * 1024 * 1024;

    public System.Version GetVersion()
    {
        if (Version is null || !Regex.IsMatch(Version, @"\A\d{1,5}\.\d{1,5}\.\d{1,5}(\.\d{1,5})?\z") ||
            !System.Version.TryParse(Version, out var value) ||
            value.Major > 65535 || value.Minor > 65535 || value.Build > 65535 || value.Revision > 65535)
            throw new InvalidDataException("Version du launcher invalide.");
        return new(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || Platform != "win-x64")
            throw new InvalidDataException("Cette mise à jour du launcher n’est pas compatible.");
        _ = GetVersion();
        if (InstallerSize <= 0 || InstallerSize > MaximumInstallerSize)
            throw new InvalidDataException("Taille de l’installateur invalide.");
        if (Sha256 is null || !Regex.IsMatch(Sha256, @"\A[a-fA-F0-9]{64}\z"))
            throw new InvalidDataException("Empreinte de l’installateur invalide.");
        var address = ValidateDownloadUrl(InstallerUrl);
        if (address.Host != "github.com" || address.AbsolutePath !=
            $"/Hantse/arkya-services/releases/download/launcher-v{Version}/Arkya-Launcher-Setup.exe" ||
            address.Query.Length != 0)
            throw new InvalidDataException("L’installateur doit appartenir à la publication officielle du launcher.");
    }

    internal static Uri ValidateDownloadUrl(string? value)
    {
        var address = GameManifest.ValidateHttpsUrl(value);
        if (!address.IsDefaultPort || address.Host is not ("github.com" or "release-assets.githubusercontent.com"))
            throw new InvalidDataException("Adresse de téléchargement du launcher non autorisée.");
        if (address.Host == "github.com" && !address.AbsolutePath.StartsWith(
            "/Hantse/arkya-services/releases/download/launcher-v", StringComparison.Ordinal))
            throw new InvalidDataException("Publication du launcher non autorisée.");
        return address;
    }
}
