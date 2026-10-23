using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class SessionHandoffTests
{
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task WindowsPipeAuthenticatesTheActualClientPidAndWritesOnlyTheBoundedContract()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handoff = new LauncherSessionHandoff();
        Assert.Matches("^arkya-game-session-[a-f0-9]{32}$", handoff.PipeName);
        var send = handoff.SendAsync(Environment.ProcessId, Session(), timeout.Token);
        using var client = Client(handoff.PipeName);
        await client.ConnectAsync(timeout.Token);
        using var bytes = new MemoryStream();
        var buffer = new byte[1];
        while (await client.ReadAsync(buffer, timeout.Token) != 0 && buffer[0] != '\n') bytes.WriteByte(buffer[0]);
        await send;
        var payload = bytes.ToArray();
        Assert.InRange(payload.Length + 1, 1, LauncherSessionHandoff.MaximumPayloadBytes);
        Assert.Equal((byte)'{', payload[0]);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(new[] { "schemaVersion", "sessionToken", "expiresAt", "accountId", "displayName" },
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Token, json.RootElement.GetProperty("sessionToken").GetString());
        Assert.Equal("Aélyra", json.RootElement.GetProperty("displayName").GetString());
        Assert.DoesNotContain("https:", Encoding.UTF8.GetString(payload));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.SendAsync(Environment.ProcessId, Session(), timeout.Token));
    }

    [Fact]
    public async Task WindowsPipeRejectsAnotherPidBeforeWritingAnySecret()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handoff = new LauncherSessionHandoff();
        var send = handoff.SendAsync(int.MaxValue, Session(), timeout.Token);
        using var client = Client(handoff.PipeName);
        await client.ConnectAsync(timeout.Token);
        await Assert.ThrowsAsync<IOException>(() => send);
        handoff.Dispose();
        Assert.Equal(0, await client.ReadAsync(new byte[1], timeout.Token));
    }

    [Fact]
    public async Task WindowsPipeWaitCanBeCancelledWithoutAClient()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var handoff = new LauncherSessionHandoff();
        using var cancelled = new CancellationTokenSource();
        var send = handoff.SendAsync(Environment.ProcessId, Session(), cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    [Fact]
    public async Task WindowsPipeRechecksExpiryAfterConnection()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handoff = new LauncherSessionHandoff();
        var expired = new LauncherSession(Token, "11111111111111111111111111111111", "Aelyra", DateTimeOffset.UtcNow.AddMinutes(-1));
        var send = handoff.SendAsync(Environment.ProcessId, expired, timeout.Token);
        using var client = Client(handoff.PipeName);
        await client.ConnectAsync(timeout.Token);
        await Assert.ThrowsAsync<LauncherAuthenticationException>(() => send);
        handoff.Dispose();
        Assert.Equal(0, await client.ReadAsync(new byte[1], timeout.Token));
    }

    [Theory]
    [InlineData("arkya-game-session-../../invalid")]
    [InlineData("arkya-game-session-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("\\\\other-host\\pipe\\session")]
    public void RejectsUnexpectedPipeNames(string value) => Assert.False(LauncherSessionHandoff.IsValidPipeName(value));

    private static LauncherSession Session() => new(Token, "11111111111111111111111111111111", "Aélyra", DateTimeOffset.UtcNow.AddHours(1));
    private static NamedPipeClientStream Client(string name) => new(".", name, PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
}
