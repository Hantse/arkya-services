using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class LauncherUpdateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "arkya-launcher-update-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("Synthetic update fixture. Never execute.");
    private static LauncherUpdateManifest Manifest(string version = "0.4.0") => new(1, version, "win-x64",
        $"https://github.com/Hantse/arkya-services/releases/download/launcher-v{version}/Arkya-Launcher-Setup.exe",
        Installer.Length, Convert.ToHexString(SHA256.HashData(Installer)));

    public LauncherUpdateTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task FindsNewLauncherVersionAtFixedOfficialEndpointWithoutCredentials()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(LauncherUpdateClient.ManifestUrl, request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            return Json(Manifest());
        }));
        Assert.Equal("0.4.0", (await new LauncherUpdateClient(http).GetUpdateAsync(new(0, 3, 1), default))!.Version);
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.4.0.0")]
    [InlineData("0.3.1")]
    public async Task DoesNotReinstallEqualVersionOrDowngrade(string offered)
    {
        using var http = new HttpClient(new Handler(_ => Json(Manifest(offered))));
        Assert.Null(await new LauncherUpdateClient(http).GetUpdateAsync(new(0, 4, 0, 0), default));
    }

    [Theory]
    [InlineData("http://github.com/Hantse/arkya-services/releases/download/launcher-v0.4.0/Arkya-Launcher-Setup.exe")]
    [InlineData("https://github.com.evil.test/Hantse/arkya-services/releases/download/launcher-v0.4.0/Arkya-Launcher-Setup.exe")]
    [InlineData("https://github.com/Hantse/other/releases/download/launcher-v0.4.0/Arkya-Launcher-Setup.exe")]
    [InlineData("https://github.com/Hantse/arkya-services/releases/download/launcher-v0.3.1/Arkya-Launcher-Setup.exe")]
    [InlineData("https://github.com:444/Hantse/arkya-services/releases/download/launcher-v0.4.0/Arkya-Launcher-Setup.exe")]
    [InlineData("https://github.com/Hantse/arkya-services/releases/download/launcher-v0.4.0/Arkya-Launcher-Setup.exe?x=1")]
    [InlineData("https://user@github.com/Hantse/arkya-services/releases/download/launcher-v0.4.0/Arkya-Launcher-Setup.exe")]
    public void RejectsInstallerOutsideItsImmutableOfficialRelease(string address) =>
        Assert.Throws<InvalidDataException>(() => (Manifest() with { InstallerUrl = address }).Validate());

    [Theory]
    [InlineData("version")]
    [InlineData("schema")]
    [InlineData("platform")]
    [InlineData("hash")]
    [InlineData("zero-size")]
    [InlineData("large-size")]
    public void RejectsMalformedUpdateContract(string change)
    {
        var manifest = change switch
        {
            "version" => Manifest() with { Version = "0.4.0-beta" },
            "schema" => Manifest() with { SchemaVersion = 2 },
            "platform" => Manifest() with { Platform = "linux-x64" },
            "hash" => Manifest() with { Sha256 = "invalid" },
            "zero-size" => Manifest() with { InstallerSize = 0 },
            _ => Manifest() with { InstallerSize = LauncherUpdateManifest.MaximumInstallerSize + 1 }
        };
        Assert.Throws<InvalidDataException>(manifest.Validate);
    }

    [Fact]
    public async Task BoundsManifestBeforeParsing()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        { Content = new StringContent(new string('x', 16385)) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new LauncherUpdateClient(http).GetUpdateAsync(new(0, 3, 1), default));
    }

    [Fact]
    public async Task DownloadsAndVerifiesInstallerThroughOfficialAssetRedirect()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            requests++;
            if (request.RequestUri!.Host == "github.com")
                return Redirect("https://release-assets.githubusercontent.com/github-production-release-asset/1?signature=value");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) };
        }));
        var destination = Path.Combine(root, "setup.exe");
        await new LauncherUpdateClient(http).DownloadAsync(Manifest(), destination, null, default);
        Assert.Equal(2, requests);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(destination));
    }

    [Theory]
    [InlineData("http://release-assets.githubusercontent.com/setup.exe")]
    [InlineData("https://example.test/setup.exe")]
    [InlineData("https://github.com/Hantse/another-project/releases/download/v1/setup.exe")]
    public async Task RefusesUntrustedRedirectBeforeFollowingIt(string redirect)
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(_ => { requests++; return Redirect(redirect); }));
        var destination = Path.Combine(root, "setup.exe");
        await Assert.ThrowsAsync<InvalidDataException>(() => new LauncherUpdateClient(http)
            .DownloadAsync(Manifest(), destination, null, default));
        Assert.Equal(1, requests);
        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("truncated")]
    [InlineData("overflow")]
    public async Task RemovesOnlyItsOwnIncompleteOrCorruptedDownload(string change)
    {
        var payload = change switch
        {
            "truncated" => Installer[..^1],
            "overflow" => Installer.Concat(new byte[] { 1 }).ToArray(),
            _ => Installer.Select(value => (byte)(value ^ 1)).ToArray()
        };
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        { Content = new StreamContent(new NonSeekableStream(payload)) }));
        var destination = Path.Combine(root, "setup.exe");
        await Assert.ThrowsAsync<InvalidDataException>(() => new LauncherUpdateClient(http)
            .DownloadAsync(Manifest(), destination, null, default));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task CancellationDuringDownloadRemovesPartialInstaller()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        { Content = new StreamContent(new NonSeekableStream(Installer)) }));
        var destination = Path.Combine(root, "setup.exe");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LauncherUpdateClient(http)
            .DownloadAsync(Manifest(), destination, new CallbackProgress(_ => cancellation.Cancel()), cancellation.Token));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task PreservesExistingFilesWhenDestinationWasAlreadyPresent()
    {
        var destination = Path.Combine(root, "setup.exe");
        await File.WriteAllTextAsync(destination, "previous download");
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }));
        await Assert.ThrowsAsync<IOException>(() => new LauncherUpdateClient(http)
            .DownloadAsync(Manifest(), destination, null, default));
        Assert.Equal("previous download", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public void UpdateHelperKeepsCustomPathAndEscapesLiteralCharacters()
    {
        var target = Path.Combine(root, "custom launcher's $folder");
        var start = LauncherUpdateInstaller.CreateStartInfo(Manifest(), Path.Combine(root, "setup.exe"), target, 123, 456);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(start.ArgumentList[^1]));
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Contains("custom launcher''s $folder", script);
        Assert.Contains("WaitForExit(60000)", script);
        Assert.Contains("/NOCLOSEAPPLICATIONS", script);
        Assert.Contains("/NORESTARTAPPLICATIONS", script);
        Assert.DoesNotContain("session", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActualDetachedHelperRefusesAlteredInstallerWithoutExecutingIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var destination = Path.Combine(root, "never-execute.exe");
        File.WriteAllText(destination, "Changed after verification.");
        var start = LauncherUpdateInstaller.CreateStartInfo(Manifest(), destination, root, int.MaxValue, 1);
        using var helper = Process.Start(start)!;
        Assert.True(helper.WaitForExit(15000));
        Assert.Equal(1, helper.ExitCode);
        Assert.Contains("integrity verification failed", File.ReadAllText(destination + ".error.txt"));
    }

    private static HttpResponseMessage Json(LauncherUpdateManifest manifest) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(manifest, GameManifest.JsonOptions)) };

    private static HttpResponseMessage Redirect(string address)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new(address);
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class CallbackProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    public void Dispose() => Directory.Delete(root, true);
}
