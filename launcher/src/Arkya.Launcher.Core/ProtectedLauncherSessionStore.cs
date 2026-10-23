using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Arkya.Launcher.Core;

public sealed class LauncherRefreshCredential
{
    internal string RefreshToken { get; }
    public string AccountId { get; }
    public DateTimeOffset ExpiresAt { get; }

    public LauncherRefreshCredential(string refreshToken, string accountId, DateTimeOffset expiresAt)
    {
        if (refreshToken is not { Length: 76 } || refreshToken[32] != '.' ||
            refreshToken.AsSpan(0, 32).ContainsAnyExcept("0123456789abcdef") ||
            refreshToken.Skip(33).Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_') ||
            !(accountId is { Length: 36 } && Guid.TryParseExact(accountId, "D", out _) ||
              accountId is { Length: 32 } && Guid.TryParseExact(accountId, "N", out _)))
            throw new LauncherAuthenticationException("La réponse du service de connexion est invalide.", true);
        RefreshToken = refreshToken;
        AccountId = accountId;
        ExpiresAt = expiresAt;
    }

    public override string ToString() => nameof(LauncherRefreshCredential);
}

public interface ILauncherSessionStore
{
    Task<IDisposable> AcquireAsync(CancellationToken cancellationToken);
    LauncherRefreshCredential? Load();
    void Save(LauncherRefreshCredential credential);
    void Clear();
}

public sealed class ProtectedLauncherSessionStore(string directory) : ILauncherSessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Arkya.Launcher.Session.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    private string SessionPath => SafePaths.Resolve(DirectoryPath, "launcher-session.dat");

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        SafePaths.RejectReparsePoints(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        var path = SafePaths.Resolve(DirectoryPath, "launcher-session.lock");
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (TimeProvider.System.GetUtcNow() < deadline)
            { await Task.Delay(50, cancellationToken); }
        }
    }

    public LauncherRefreshCredential? Load()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La session persistante requiert la protection Windows.");
        SafePaths.RejectReparsePoints(DirectoryPath);
        if (!File.Exists(SessionPath)) return null;
        byte[]? plaintext = null;
        try
        {
            using var input = new FileStream(SessionPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > 16384) throw new InvalidDataException();
            var encrypted = new byte[checked((int)input.Length)];
            input.ReadExactly(encrypted);
            plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            var saved = JsonSerializer.Deserialize<StoredCredential>(plaintext, JsonOptions);
            if (saved is null || saved.SchemaVersion != 1) throw new InvalidDataException();
            return new LauncherRefreshCredential(saved.RefreshToken, saved.AccountId, saved.ExpiresAt);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException or LauncherAuthenticationException)
        {
            Clear();
            return null;
        }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Save(LauncherRefreshCredential credential)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La session persistante requiert la protection Windows.");
        SafePaths.RejectReparsePoints(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new StoredCredential(1, credential.RefreshToken, credential.AccountId, credential.ExpiresAt), JsonOptions);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var temporary = SafePaths.Resolve(DirectoryPath, "session-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(encrypted);
                output.Flush(true);
            }
            if (File.Exists(SessionPath)) File.Replace(temporary, SessionPath, null);
            else File.Move(temporary, SessionPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Clear()
    {
        SafePaths.RejectReparsePoints(DirectoryPath);
        File.Delete(SessionPath);
    }

    private sealed record StoredCredential(int SchemaVersion, string RefreshToken, string AccountId, DateTimeOffset ExpiresAt);
}
