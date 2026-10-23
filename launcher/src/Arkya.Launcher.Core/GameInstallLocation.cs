namespace Arkya.Launcher.Core;

public static class GameInstallLocation
{
    public static string Validate(string path, params string[] protectedDirectories)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Choisissez un dossier local avec un chemin absolu.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.GetPathRoot(full)!;
        if (string.Equals(full, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choisissez un dossier pour Arkya, plutôt que la racine du disque.");
        SafePaths.ValidateRelative(Path.GetRelativePath(root, full).Replace('\\', '/'));
        var systemDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (var blocked in systemDirectories.Concat(protectedDirectories).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(blocked));
            if (Within(full, directory) || Within(directory, full))
                throw new InvalidDataException("Choisissez un dossier distinct de Windows et du launcher pour installer le jeu.");
        }
        if (OperatingSystem.IsWindows() && new DriveInfo(root).DriveType == DriveType.Network)
            throw new InvalidDataException("Choisissez un disque local pour installer le jeu.");
        SafePaths.RejectReparsePoints(full);
        if (File.Exists(full)) throw new InvalidDataException("Le dossier choisi est un fichier.");
        return full;
    }

    public static string Prepare(string path, params string[] protectedDirectories)
    {
        var full = Validate(path, protectedDirectories);
        Directory.CreateDirectory(full);
        SafePaths.RejectReparsePoints(full);
        var probe = SafePaths.Resolve(full, ".arkya-write-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        return full;
    }

    private static bool Within(string path, string directory) =>
        string.Equals(path, directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
