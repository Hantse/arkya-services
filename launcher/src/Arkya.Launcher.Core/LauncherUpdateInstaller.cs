using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

[assembly: InternalsVisibleTo("Arkya.Launcher.Tests")]

namespace Arkya.Launcher.Core;

public static class LauncherUpdateInstaller
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{9E8A9F67-445E-4876-B986-1C6D1E4ED1A0}_is1";

    public static bool IsInstalledLauncher(string directory)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var expected = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            using var registration = user.OpenSubKey(UninstallKey);
            if (registration?.GetValue("InstallLocation") is string installed &&
                Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(expected, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(expected, "Arkya.Launcher.exe")))
                return true;
        }
        return false;
    }

    public static void StartAfterLauncherExit(LauncherUpdateManifest manifest, string installerPath,
        string installationDirectory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!IsInstalledLauncher(installationDirectory))
            throw new InvalidOperationException("La version portable se met à jour en téléchargeant le nouvel installateur.");
        manifest.Validate();
        using (var input = File.OpenRead(installerPath))
        {
            if (input.Length != manifest.InstallerSize || !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(input), Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException("L’installateur a changé depuis son téléchargement.");
        }
        using var parent = Process.GetCurrentProcess();
        var start = CreateStartInfo(manifest, installerPath, installationDirectory,
            parent.Id, parent.StartTime.ToUniversalTime().ToFileTimeUtc());
        using var helper = Process.Start(start)
            ?? throw new IOException("Impossible de démarrer la mise à jour du launcher.");
        if (helper.WaitForExit(300))
            throw new IOException("La préparation de la mise à jour a échoué. Consultez le dossier de téléchargement.");
    }

    internal static ProcessStartInfo CreateStartInfo(LauncherUpdateManifest manifest, string installerPath,
        string installationDirectory, int parentId, long parentStartFileTime)
    {
        manifest.Validate();
        var installer = Path.GetFullPath(installerPath);
        var directory = Path.GetFullPath(installationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!installer.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            installer.IndexOfAny(['\r', '\n', '"']) >= 0 || directory.IndexOfAny(['\r', '\n', '"']) >= 0 ||
            parentId <= 0 || parentStartFileTime <= 0)
            throw new InvalidDataException("Paramètres de mise à jour invalides.");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $installer = {{Literal(installer)}}
            $directory = {{Literal(directory)}}
            $log = $installer + '.update.log'
            try {
                $parent = $null
                try { $parent = [Diagnostics.Process]::GetProcessById({{parentId}}) } catch [ArgumentException] { }
                if ($parent) {
                    try {
                        $actualStart = $null
                        try { $actualStart = $parent.StartTime.ToUniversalTime().ToFileTimeUtc() } catch [InvalidOperationException] { }
                        if ($null -ne $actualStart) {
                            if ($actualStart -ne {{parentStartFileTime}}) { throw 'Parent process identity changed.' }
                            if (-not $parent.WaitForExit(60000)) { throw 'The launcher did not exit. Update was not applied.' }
                        }
                    } finally { $parent.Dispose() }
                }
                $input = [IO.File]::OpenRead($installer)
                $sha = [Security.Cryptography.SHA256]::Create()
                try {
                    $length = $input.Length
                    $hash = [BitConverter]::ToString($sha.ComputeHash($input)).Replace('-', '')
                } finally { $sha.Dispose(); $input.Dispose() }
                if ($length -ne {{manifest.InstallerSize}} -or $hash -ne {{Literal(manifest.Sha256)}}) {
                    throw 'Installer integrity verification failed.'
                }
                $setupInfo = [Diagnostics.ProcessStartInfo]::new($installer)
                $setupInfo.UseShellExecute = $false
                $setupInfo.CreateNoWindow = $true
                $setupInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
                $setupInfo.Arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /ARKYAUPDATE /DIR="' + $directory + '" /LOG="' + $log + '"'
                $setup = [Diagnostics.Process]::Start($setupInfo)
                if (-not $setup.WaitForExit(180000)) { throw 'Installer did not finish within three minutes.' }
                if ($setup.ExitCode -ne 0) { throw ('Installer failed: ' + $setup.ExitCode) }
                $setup.Dispose()
                $restart = [Diagnostics.ProcessStartInfo]::new([IO.Path]::Combine($directory, 'Arkya.Launcher.exe'))
                $restart.UseShellExecute = $false
                $restart.WorkingDirectory = $directory
                $started = [Diagnostics.Process]::Start($restart)
                if ($started) { $started.Dispose() }
                [IO.File]::Delete($installer)
            } catch {
                [IO.File]::WriteAllText($installer + '.error.txt', $_.Exception.Message)
                exit 1
            }
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(argument);
        return start;
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
