using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Arkya.Launcher.Core;

public sealed class LauncherSessionHandoff : IDisposable
{
    public const string EnvironmentVariable = "ARKYA_LAUNCHER_PIPE";
    public const string PipePrefix = "arkya-game-session-";
    public const int MaximumPayloadBytes = 8192;
    private readonly NamedPipeServerStream pipe;
    private int used;
    public string PipeName { get; } = PipePrefix + Guid.NewGuid().ToString("N");

    public LauncherSessionHandoff()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Le transfert de session nécessite Windows.");
        pipe = new NamedPipeServerStream(PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, MaximumPayloadBytes, MaximumPayloadBytes);
    }

    public async Task SendAsync(int launchedProcessId, LauncherSession session, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref used, 1) != 0 || launchedProcessId <= 0)
            throw new InvalidOperationException("Ce transfert de session a déjà été utilisé.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        await pipe.WaitForConnectionAsync(timeout.Token);
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcessId) || clientProcessId != (uint)launchedProcessId)
            throw new IOException("Le processus connecté n’est pas le jeu lancé par ce launcher.");
        if (!session.IsUsableAt(DateTimeOffset.UtcNow))
            throw new LauncherAuthenticationException("Votre session a expiré. Reconnectez-vous avec Discord.", true);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            sessionToken = session.SessionToken,
            expiresAt = session.ExpiresAt,
            accountId = session.AccountId,
            displayName = session.DisplayName
        });
        if (payload.Length + 1 > MaximumPayloadBytes)
            throw new InvalidDataException("Le transfert de session dépasse la taille autorisée.");
        await pipe.WriteAsync(payload, timeout.Token);
        await pipe.WriteAsync(new byte[] { (byte)'\n' }, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
    }

    public static bool IsValidPipeName(string? name) => name is not null && name.StartsWith(PipePrefix, StringComparison.Ordinal) &&
        name.Length == PipePrefix.Length + 32 && name[PipePrefix.Length..].All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f');

    public void Dispose() => pipe.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
}
