using System.Security.Cryptography;
using System.Text;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Contracts;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed record RealmGameCommand(string Id, string ServerId, string Type, string Message, string CharacterId,
    string Status, string Actor, string Reason, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt = null, string? ResultCode = null, int? AffectedPlayers = null, string? InstanceId = null,
    long RealmRevision = 0);
public sealed record RealmCommandServer(string RealmId, string ServerId, long Version, string InstanceId,
    DateTimeOffset LastSeenAt, string[] SupportedCommands, RealmGameCommand[] Commands)
{
    public int ConfigurationVersion { get; init; }
    public long? AppliedConfigurationRevision { get; init; }
}
public sealed record RealmCommandCapability(string ServerId, DateTimeOffset? LastSeenAt, bool Available, string[] SupportedCommands,
    int ConfigurationVersion = 0, long? AppliedConfigurationRevision = null);
public sealed record RealmCommandsOverview(RealmCommandCapability[] Servers, RealmGameCommand[] Commands);
public sealed record EnqueueRealmCommand(string RequestId, long ExpectedRevision, string ServerId, string Type,
    string Message, string CharacterId, string Reason);
public sealed record PollRealmCommands(string InstanceId, int ProtocolVersion, string[] SupportedCommands,
    int ConfigurationVersion = 0, long? AppliedConfigurationRevision = null, bool AcceptCommands = true);
public sealed record CompleteRealmCommand(string InstanceId, bool Success, string ResultCode, int AffectedPlayers);
public sealed record RealmCommandDelivery(string Id, string Type, string Message, string CharacterId, DateTimeOffset ExpiresAt);
public sealed record RealmCommandPollResponse(RealmCommandDelivery[] Commands, RealmConfigurationSnapshot? RuntimeConfiguration = null);
public sealed record RealmCommandResult<T>(T? Value, string? Error) where T : class;

public interface IRealmCommandRepository
{
    Task InitializeAsync(CancellationToken ct);
    Task<RealmCommandServer?> FindAsync(string realmId, string serverId, CancellationToken ct);
    Task<bool> TryWriteAsync(RealmCommandServer server, long? expectedVersion, CancellationToken ct);
}

public sealed class RealmCommandService(IRealmRepository realms, IRealmCommandRepository repository,
    IOptions<RealmRuntimeOptions> runtime, TimeProvider clock)
{
    public static readonly string[] SupportedTypes = ["announce", "kick", "save"];
    public static bool ValidActor(string? value) => Printable(value, 128);
    private static bool Printable(string? value, int max) => value is { Length: > 0 } && value.Length <= max &&
        value == value.Trim() && !value.Any(char.IsControl);
    private static bool ValidUuid(string? value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && value == id.ToString("D");
    private static bool Active(RealmCommandServer state, DateTimeOffset now) =>
        state.LastSeenAt <= now && now - state.LastSeenAt <= TimeSpan.FromSeconds(30);

    public async Task<bool> AuthorizeAsync(string realmId, string serverId, string? key, CancellationToken ct)
    {
        if (!DeploymentOptions.ValidId(realmId) || !DeploymentOptions.ValidId(serverId) ||
            runtime.Value.DedicatedKeySeed.Length == 0 || key is not { Length: >= 32 and <= 512 }) return false;
        var expected = RealmRuntimeKeys.DeriveDedicatedKey(runtime.Value.DedicatedKeySeed, realmId, serverId);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(key)))) return false;
        var realm = await realms.FindAsync(realmId, ct);
        return realm is not null && !RealmStatus.IsDeleting(realm.Status) && realm.Profile.Servers.Any(server => server.Id == serverId);
    }

    public async Task<RealmCommandsOverview?> GetAsync(string realmId, CancellationToken ct)
    {
        var realm = await realms.FindAsync(realmId, ct);
        if (realm is null || realm.Status == RealmStatus.Deleted) return null;
        var now = clock.GetUtcNow();
        var capabilities = new List<RealmCommandCapability>();
        var commands = new List<RealmGameCommand>();
        foreach (var definition in realm.Profile.Servers)
        {
            var state = await repository.FindAsync(realmId, definition.Id, ct);
            capabilities.Add(new(definition.Id, state?.LastSeenAt,
                state is not null && Active(state, now) && realm.Status == RealmStatus.Ready, state?.SupportedCommands ?? [],
                state?.ConfigurationVersion ?? 0, state?.AppliedConfigurationRevision));
            if (state is not null) commands.AddRange(Normalize(state.Commands, now, realm.Revision).Select(Public));
        }
        return new(capabilities.ToArray(), commands.OrderByDescending(command => command.CreatedAt).Take(50).ToArray());
    }

    public async Task<RealmCommandResult<RealmGameCommand>> EnqueueAsync(string realmId, EnqueueRealmCommand request,
        string? actor, CancellationToken ct)
    {
        if (!ValidActor(actor) || !ValidUuid(request.RequestId) || request.ExpectedRevision < 0 ||
            !DeploymentOptions.ValidId(request.ServerId) || !SupportedTypes.Contains(request.Type) ||
            !Printable(request.Reason, 500) || request.Message is null || request.CharacterId is null ||
            (request.Type == "announce" ? !Printable(request.Message, 400) || Encoding.UTF8.GetByteCount(request.Message) > 400 : request.Message.Length != 0) ||
            (request.Type == "kick" ? !ValidUuid(request.CharacterId) : request.CharacterId.Length != 0))
            return new(null, "invalid_command");
        var realm = await realms.FindAsync(realmId, ct);
        if (realm is null || realm.Status == RealmStatus.Deleted) return new(null, "realm_not_found");
        if (RealmStatus.IsDeleting(realm.Status)) return new(null, "realm_changed");
        if (!realm.Profile.Servers.Any(server => server.Id == request.ServerId)) return new(null, "invalid_server");
        var now = clock.GetUtcNow();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var state = await repository.FindAsync(realmId, request.ServerId, ct);
            var commands = Normalize(state?.Commands ?? [], now, realm.Revision);
            if (commands.FirstOrDefault(command => command.Id == request.RequestId) is { } previous)
                return previous.Type == request.Type && previous.Message == request.Message && previous.CharacterId == request.CharacterId &&
                    previous.Actor == actor && previous.Reason == request.Reason
                    ? new(Public(previous), null) : new(null, "request_conflict");
            if (realm.Revision != request.ExpectedRevision || realm.Status != RealmStatus.Ready) return new(null, "realm_changed");
            if (state is null || !Active(state, now) || !state.SupportedCommands.Contains(request.Type)) return new(null, "runtime_unavailable");
            if (commands.Count(command => command.Status is "Pending" or "Dispatched") >= 20) return new(null, "command_queue_full");
            var command = new RealmGameCommand(request.RequestId, request.ServerId, request.Type, request.Message, request.CharacterId,
                "Pending", actor!, request.Reason, now, now.AddMinutes(2), RealmRevision: realm.Revision);
            // Keep active requests even when rapidly completed commands fill the audit window.
            var history = commands.Where(item => item.Status is "Pending" or "Dispatched")
                .Concat(commands.Where(item => item.Status is not ("Pending" or "Dispatched")).OrderByDescending(item => item.CreatedAt)
                    .Take(99 - commands.Count(item => item.Status is "Pending" or "Dispatched")))
                .Append(command).OrderBy(item => item.CreatedAt).ToArray();
            if (await repository.TryWriteAsync(state with { Version = state.Version + 1, Commands = history }, state.Version, ct))
                return new(Public(command), null);
        }
        return new(null, "command_conflict");
    }

    public async Task<RealmCommandResult<RealmCommandPollResponse>> PollAsync(string realmId, string serverId,
        PollRealmCommands request, CancellationToken ct)
    {
        if (!ValidUuid(request.InstanceId) || request.ProtocolVersion != 1 || request.SupportedCommands is not { Length: >= 1 and <= 3 } ||
            request.SupportedCommands.Distinct().Count() != request.SupportedCommands.Length ||
            request.SupportedCommands.Any(type => !SupportedTypes.Contains(type)) ||
            request.ConfigurationVersion is < 0 or > 1 || request.AppliedConfigurationRevision is < -1 ||
            request.ConfigurationVersion == 0 && request.AppliedConfigurationRevision is not null) return new(null, "invalid_poll");
        var realm = await realms.FindAsync(realmId, ct);
        if (realm is null || RealmStatus.IsDeleting(realm.Status) || !realm.Profile.Servers.Any(server => server.Id == serverId)) return new(null, "realm_not_found");
        var configuration = RealmConfigurationSnapshot.From(realm.RuntimeConfiguration);
        if (request.AppliedConfigurationRevision > configuration.Revision) return new(null, "invalid_poll");
        var now = clock.GetUtcNow();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var previous = await repository.FindAsync(realmId, serverId, ct);
            var commands = Normalize(previous?.Commands ?? [], now, realm.Revision);
            // Delivery is claimed once. A lost HTTP response must never repeat a gameplay mutation.
            var selected = request.AcceptCommands && realm.Status == RealmStatus.Ready ? commands.FirstOrDefault(command => command.Status == "Pending" &&
                request.SupportedCommands.Contains(command.Type)) : null;
            var state = new RealmCommandServer(realmId, serverId, (previous?.Version ?? 0) + 1, request.InstanceId,
                now, request.SupportedCommands, commands.Select(command => command == selected
                    ? command with { Status = "Dispatched", InstanceId = request.InstanceId } : command).ToArray())
            {
                ConfigurationVersion = request.ConfigurationVersion,
                AppliedConfigurationRevision = request.ConfigurationVersion == 1 && request.AppliedConfigurationRevision is >= 0
                    ? request.AppliedConfigurationRevision : null
            };
            if (await repository.TryWriteAsync(state, previous?.Version, ct))
                return new(new(selected is null ? [] : [new(selected.Id, selected.Type, selected.Message, selected.CharacterId, selected.ExpiresAt)],
                    request.ConfigurationVersion == 1 ? configuration : null), null);
        }
        return new(null, "command_conflict");
    }

    public async Task<RealmCommandResult<RealmGameCommand>> CompleteAsync(string realmId, string serverId, string commandId,
        CompleteRealmCommand request, CancellationToken ct)
    {
        if (!ValidUuid(request.InstanceId) || !ValidUuid(commandId) || request.AffectedPlayers is < 0 or > 100000 ||
            (request.Success ? request.ResultCode is not ("announced" or "kicked" or "saved") :
                request.ResultCode is not ("player_not_found" or "failed" or "unsupported"))) return new(null, "invalid_completion");
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var state = await repository.FindAsync(realmId, serverId, ct);
            var command = state?.Commands.FirstOrDefault(item => item.Id == commandId);
            if (state is null || command is null) return new(null, "command_not_found");
            if (command.InstanceId != request.InstanceId || command.Status is not ("Dispatched" or "Unknown" or "Succeeded" or "Failed"))
                return new(null, "completion_conflict");
            var status = request.Success ? "Succeeded" : "Failed";
            var expectedResult = command.Type switch { "announce" => "announced", "kick" => "kicked", _ => "saved" };
            if (request.Success && request.ResultCode != expectedResult) return new(null, "invalid_completion");
            if (command.CompletedAt is not null) return command.Status == status && command.ResultCode == request.ResultCode &&
                command.AffectedPlayers == request.AffectedPlayers ? new(Public(command), null) : new(null, "completion_conflict");
            var completed = command with { Status = status, CompletedAt = clock.GetUtcNow(), ResultCode = request.ResultCode, AffectedPlayers = request.AffectedPlayers };
            if (await repository.TryWriteAsync(state with { Version = state.Version + 1,
                Commands = state.Commands.Select(item => item.Id == commandId ? completed : item).ToArray() }, state.Version, ct))
                return new(Public(completed), null);
        }
        return new(null, "command_conflict");
    }

    private static RealmGameCommand[] Normalize(RealmGameCommand[] commands, DateTimeOffset now, long revision) => commands.Select(command =>
        command.Status == "Pending" && command.RealmRevision != revision ? command with { Status = "Expired", ResultCode = "realm_changed" } :
        command.ExpiresAt <= now && command.Status is ("Pending" or "Dispatched")
            ? command with { Status = command.Status == "Pending" ? "Expired" : "Unknown" } : command).ToArray();
    private static RealmGameCommand Public(RealmGameCommand command) => command with { InstanceId = null };
}
