using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class InstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "arkya-launcher-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallsCompleteReleaseAndAtomicallySwitchesPointerWhileKeepingPreviousRelease()
    {
        var fixture = Package();
        var installer = Installer(fixture.Bytes);
        var first = await installer.InstallAsync(fixture.Manifest, null, CancellationToken.None);
        Assert.Equal(first, installer.ReadInstalled());
        Assert.Equal("fixture", File.ReadAllText(Path.Combine(root, first.DirectoryName, "Arkya_Data", "data.bin")));
        var second = await installer.InstallAsync(fixture.Manifest with { Version = "0.2.0" }, null, CancellationToken.None);
        Assert.Equal(second, installer.ReadInstalled());
        Assert.True(File.Exists(Path.Combine(root, first.DirectoryName, "Arkya.exe")));
        Assert.Empty(Directory.GetFiles(root, "*.zip"));
        Assert.Empty(Directory.GetDirectories(root, "stage-*"));
    }

    [Fact]
    public async Task HashMismatchPreservesInstalledGame()
    {
        var fixture = Package();
        var installer = Installer(fixture.Bytes);
        var first = await installer.InstallAsync(fixture.Manifest, null, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            fixture.Manifest with { Sha256 = new string('0', 64) }, null, CancellationToken.None));
        Assert.Equal(first, installer.ReadInstalled());
        Assert.Single(Directory.GetDirectories(Path.Combine(root, "releases")));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task RejectsIncorrectDownloadSize(int delta)
    {
        var fixture = Package();
        var installer = Installer(fixture.Bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            fixture.Manifest with { PackageSize = fixture.Bytes.Length + delta }, null, CancellationToken.None));
        Assert.Null(installer.ReadInstalled());
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/escaped.txt")]
    [InlineData("Arkya_Data/../../escaped.txt")]
    [InlineData("Arkya_Data\\escaped.txt")]
    [InlineData("Arkya.exe:malware")]
    [InlineData("NUL.txt")]
    [InlineData("folder./file.txt")]
    [InlineData("folder /file.txt")]
    [InlineData("CONIN$")]
    [InlineData("COM¹.txt")]
    public async Task RejectsUnsafeArchivePaths(string maliciousPath)
    {
        var fixture = Package(maliciousPath);
        var installer = Installer(fixture.Bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(fixture.Manifest, null, CancellationToken.None));
        Assert.Null(installer.ReadInstalled());
        Assert.Empty(Directory.GetDirectories(root, "stage-*"));
    }

    [Fact]
    public async Task RejectsArchiveSymlinks()
    {
        var fixture = Package("redirect", 0xa1ff << 16);
        await Assert.ThrowsAsync<InvalidDataException>(() => Installer(fixture.Bytes).InstallAsync(fixture.Manifest, null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsCaseInsensitiveDuplicateFiles()
    {
        var fixture = Package("arkya.exe");
        await Assert.ThrowsAsync<InvalidDataException>(() => Installer(fixture.Bytes).InstallAsync(fixture.Manifest, null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsUnexpectedExpandedSize()
    {
        var fixture = Package();
        await Assert.ThrowsAsync<InvalidDataException>(() => Installer(fixture.Bytes).InstallAsync(
            fixture.Manifest with { UnpackedSize = fixture.Manifest.UnpackedSize - 1 }, null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsIncompleteUnityBuild()
    {
        var fixture = Package();
        await Assert.ThrowsAsync<InvalidDataException>(() => Installer(fixture.Bytes).InstallAsync(
            fixture.Manifest with { Executable = "Missing.exe" }, null, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationLeavesPriorReleaseUsableAndCleansTemporaryFiles()
    {
        var fixture = Package();
        var installer = Installer(fixture.Bytes);
        var first = await installer.InstallAsync(fixture.Manifest, null, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(fixture.Manifest, progress, cancellation.Token));
        Assert.Equal(first, installer.ReadInstalled());
        Assert.Empty(Directory.GetFiles(root, "*.zip"));
        Assert.Empty(Directory.GetDirectories(root, "stage-*"));
    }

    [Fact]
    public async Task SecondInstallerCannotMutateAnInstallationAlreadyLocked()
    {
        var fixture = Package();
        var installer = Installer(fixture.Bytes);
        using var held = new FileStream(Path.Combine(root, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(fixture.Manifest, null, CancellationToken.None));
        Assert.Null(installer.ReadInstalled());
    }

    [Fact]
    public void RejectsTamperedInstalledPointer()
    {
        var fixture = Package();
        var installer = Installer(fixture.Bytes);
        File.WriteAllText(Path.Combine(root, "current.json"), JsonSerializer.Serialize(new InstalledGame(fixture.Manifest, "../elsewhere"), GameManifest.JsonOptions));
        Assert.Throws<InvalidDataException>(() => installer.ReadInstalled());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"manifest\":null,\"directoryName\":\"releases/test\"}")]
    [InlineData("{\"manifest\":null,\"directoryName\":null}")]
    public void RejectsMissingInstalledStateFields(string json)
    {
        var installer = Installer(Package().Bytes);
        File.WriteAllText(Path.Combine(root, "current.json"), json);
        Assert.Throws<InvalidDataException>(() => installer.ReadInstalled());
    }

    [Fact]
    public async Task ReadsManifestThroughHttpsAndRejectsDowngradeRedirect()
    {
        var fixture = Package();
        using var client = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(fixture.Manifest, GameManifest.JsonOptions))
        }));
        var releases = new ReleaseClient(client);
        Assert.Equal(fixture.Manifest, await releases.GetManifestAsync("https://releases.example.test/latest.json", CancellationToken.None));
        using var redirect = new HttpClient(new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://releases.example.test/latest.json");
            return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseClient(redirect).GetManifestAsync("https://releases.example.test/latest.json", CancellationToken.None));
    }

    [Fact]
    public async Task BoundsManifestSizeWithoutContentLength()
    {
        using var client = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(new byte[65537]))
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseClient(client).GetManifestAsync("https://releases.example.test/latest.json", CancellationToken.None));
    }

    [Theory]
    [InlineData("http://example.test/game.zip")]
    [InlineData("file:///C:/game.zip")]
    [InlineData("https://user:password@example.test/game.zip")]
    public void RejectsUntrustedDownloadTransport(string url)
    {
        Assert.Throws<InvalidDataException>(() => (Package().Manifest with { PackageUrl = url }).Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task RejectsIncorrectStreamingDownloadSizeWithoutLengthHeader(int delta)
    {
        var fixture = Package();
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(fixture.Bytes))
        }));
        var installer = new GameInstaller(root, new ReleaseClient(http));
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            fixture.Manifest with { PackageSize = fixture.Bytes.Length + delta }, null, CancellationToken.None));
        Assert.Null(installer.ReadInstalled());
    }

    [Fact]
    public async Task PackageRedirectCannotDowngradeHttps()
    {
        var fixture = Package();
        using var http = new HttpClient(new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("http://releases.example.test/game.zip");
            return response;
        }));
        var installer = new GameInstaller(root, new ReleaseClient(http));
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(fixture.Manifest, null, CancellationToken.None));
        Assert.Null(installer.ReadInstalled());
    }

    [Fact]
    public void RejectsNullDirectoryInOtherwiseValidInstalledState()
    {
        var fixture = Package();
        var installer = Installer(fixture.Bytes);
        File.WriteAllText(Path.Combine(root, "current.json"), JsonSerializer.Serialize(new InstalledGame(fixture.Manifest, null!), GameManifest.JsonOptions));
        Assert.Throws<InvalidDataException>(() => installer.ReadInstalled());
    }

    private GameInstaller Installer(byte[] package)
    {
        var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) }));
        return new GameInstaller(root, new ReleaseClient(http));
    }

    private static (byte[] Bytes, GameManifest Manifest) Package(string? extra = null, int attributes = 0)
    {
        using var stream = new MemoryStream();
        long unpacked = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var name in new[] { "Arkya.exe", "UnityPlayer.dll", "Arkya_Data/data.bin" }.Concat(extra is null ? [] : new[] { extra }))
            {
                var entry = archive.CreateEntry(name);
                if (name == extra) entry.ExternalAttributes = attributes;
                using var output = entry.Open();
                var bytes = Encoding.UTF8.GetBytes("fixture");
                output.Write(bytes);
                unpacked += bytes.Length;
            }
        }
        var content = stream.ToArray();
        return (content, new(1, "0.1.0", "win-x64", "https://releases.example.test/game.zip", content.Length,
            unpacked, Convert.ToHexString(SHA256.HashData(content)), "Arkya.exe", "Version de test synthétique, jamais lancée."));
    }

    public void Dispose()
    {
        var verified = Path.GetFullPath(root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), verified, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(verified)) Directory.Delete(verified, true);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ImmediateProgress(Action<InstallProgress> callback) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => callback(value);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
