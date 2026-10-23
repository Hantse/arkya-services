using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Arkya.Launcher.Core;

public sealed class GameInstaller
{
    private readonly ReleaseClient client;
    public string Root { get; }

    public GameInstaller(string root, ReleaseClient client)
    {
        Root = Path.GetFullPath(root);
        SafePaths.RejectReparsePoints(Root);
        Directory.CreateDirectory(Root);
        this.client = client;
    }

    public InstalledGame? ReadInstalled()
    {
        var path = SafePaths.Resolve(Root, "current.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Installation locale invalide.");
        var installed = JsonSerializer.Deserialize<InstalledGame>(File.ReadAllText(path), GameManifest.JsonOptions)
            ?? throw new InvalidDataException("Installation locale invalide.");
        if (installed.Manifest is null || installed.DirectoryName is null)
            throw new InvalidDataException("Installation locale invalide.");
        installed.Manifest.Validate();
        if (!installed.DirectoryName.StartsWith("releases/", StringComparison.Ordinal) || installed.DirectoryName.Split('/').Length != 2)
            throw new InvalidDataException("Dossier de version invalide.");
        ValidatePayload(SafePaths.Resolve(Root, installed.DirectoryName), installed.Manifest);
        return installed;
    }

    public async Task<InstalledGame> InstallAsync(GameManifest manifest, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        manifest.Validate();
        var lockPath = SafePaths.Resolve(Root, "install.lock");
        await using var installationLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var id = Guid.NewGuid().ToString("N");
        var archivePath = SafePaths.Resolve(Root, $"download-{id}.zip");
        var relativeRelease = $"releases/{manifest.Version}-{id}";
        var release = SafePaths.Resolve(Root, relativeRelease);
        var temporaryState = SafePaths.Resolve(Root, $"current-{id}.tmp");
        var committed = false;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Root)!);
            if (drive.AvailableFreeSpace < manifest.PackageSize + manifest.UnpackedSize + 256L * 1024 * 1024)
                throw new IOException("Espace disque insuffisant pour installer cette version du jeu.");
            // Only the atomic current.json switch publishes this unique directory.
            Directory.CreateDirectory(release);
            await client.DownloadAsync(manifest, archivePath, progress, cancellationToken);
            progress?.Report(new("Vérification et installation du jeu"));
            await ExtractAsync(archivePath, release, manifest.UnpackedSize, progress, cancellationToken);
            ValidatePayload(release, manifest);
            cancellationToken.ThrowIfCancellationRequested();
            var installed = new InstalledGame(manifest, relativeRelease);
            await File.WriteAllTextAsync(temporaryState, JsonSerializer.Serialize(installed, GameManifest.JsonOptions), cancellationToken);
            // The active pointer changes only after the complete release is on disk.
            using (var stateFile = new FileStream(temporaryState, FileMode.Open, FileAccess.Write, FileShare.None))
                stateFile.Flush(true);
            cancellationToken.ThrowIfCancellationRequested();
            var current = SafePaths.Resolve(Root, "current.json");
            if (File.Exists(current)) File.Replace(temporaryState, current, null);
            else File.Move(temporaryState, current);
            committed = true;
            progress?.Report(new("Le jeu est prêt", 1, 1));
            return installed;
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteFile(temporaryState);
            if (!committed) TryDeleteDirectory(release);
        }
    }

    public Process Launch(InstalledGame installed)
    {
        installed.Manifest.Validate();
        var directory = SafePaths.Resolve(Root, installed.DirectoryName);
        ValidatePayload(directory, installed.Manifest);
        var executable = SafePaths.Resolve(directory, installed.Manifest.Executable);
        return Process.Start(new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        }) ?? throw new IOException("Le jeu n’a pas pu démarrer.");
    }

    private static void ValidatePayload(string directory, GameManifest manifest)
    {
        var executable = SafePaths.Resolve(directory, manifest.Executable);
        var relativeDirectory = manifest.Executable.Contains('/') ? manifest.Executable[..(manifest.Executable.LastIndexOf('/') + 1)] : "";
        var data = SafePaths.Resolve(directory, relativeDirectory + Path.GetFileNameWithoutExtension(manifest.Executable) + "_Data");
        var player = SafePaths.Resolve(directory, relativeDirectory + "UnityPlayer.dll");
        if (!File.Exists(executable) || !Directory.Exists(data) || !File.Exists(player))
            throw new InvalidDataException("Le package ne contient pas un build Windows Unity complet.");
    }

    private static async Task ExtractAsync(string archivePath, string destination, long expectedBytes,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is 0 or > 200000) throw new InvalidDataException("Nombre de fichiers invalide.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.TrimEnd('/');
            SafePaths.ValidateRelative(name);
            if (!names.Add(name)) throw new InvalidDataException("Le package contient des fichiers en double.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if ((unixType != 0 && unixType != 0x8000 && unixType != 0x4000) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Le package contient un lien ou un fichier spécial interdit.");
            declaredBytes = checked(declaredBytes + entry.Length);
            if (declaredBytes > expectedBytes) throw new InvalidDataException("La taille décompressée dépasse le manifeste.");
        }
        if (declaredBytes != expectedBytes) throw new InvalidDataException("La taille décompressée ne correspond pas au manifeste.");
        long extracted = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = SafePaths.Resolve(destination, entry.FullName.TrimEnd('/'));
            if (entry.FullName.EndsWith('/'))
            {
                if (entry.Length != 0) throw new InvalidDataException("Dossier ZIP invalide.");
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            var count = await ReleaseClient.CopyBoundedAsync(input, output, entry.Length, cancellationToken);
            if (count != entry.Length) throw new InvalidDataException("Fichier ZIP incomplet.");
            extracted += count;
            progress?.Report(new("Installation du jeu", extracted, expectedBytes));
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            var relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            var checkedPath = SafePaths.Resolve(Root, relative);
            if (Directory.Exists(checkedPath)) Directory.Delete(checkedPath, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
