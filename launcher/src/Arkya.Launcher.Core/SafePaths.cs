namespace Arkya.Launcher.Core;

public static class SafePaths
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"
    };

    public static void ValidateRelative(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 220 || relative.Contains('\\') || relative.StartsWith('/'))
            throw new InvalidDataException("Chemin de fichier invalide dans le package.");
        foreach (var part in relative.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || ReservedNames.Contains(part.Split('.')[0]))
                throw new InvalidDataException("Chemin de fichier interdit dans le package.");
        }
    }

    public static string Resolve(string root, string relative)
    {
        ValidateRelative(relative);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le fichier sort du dossier du jeu.");
        RejectReparsePoints(path);
        return path;
    }

    public static void RejectReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Les liens de fichiers ne sont pas acceptés dans l’installation.");
        }
    }
}
