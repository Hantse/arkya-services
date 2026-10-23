using System.Diagnostics;
using System.Text.Json;
using Arkya.Admin.Accounts;
using Arkya.Admin.Content;
using Arkya.Admin.Inference;
using Arkya.Admin.Realms;
using Arkya.Admin.Reports;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Operations;

public sealed record ServiceObservation(string Name, string Purpose, string State, long? ElapsedMilliseconds)
{
    public string Label => State switch { "ready" => "Available", "disabled" => "Not configured", _ => "Unavailable" };
}

public sealed record DashboardSnapshot(DateTimeOffset ObservedAt, ServiceObservation[] Services,
    long? TotalAccounts, AdminCharacterOverview? Characters, RealmSummary[]? Realms);

public sealed class OperationsDashboard(IdentityAdminClient identity, CharactersAdminClient characters,
    ContentAdminClient content, IOptions<ContentApiOptions> contentOptions, RealmsAdminClient realms,
    ReportsAdminClient reports, InferenceAdminClient inference, TimeProvider clock)
{
    public async Task<DashboardSnapshot> ReadAsync(CancellationToken ct)
    {
        // Independent probes prevent one unavailable dependency from hiding the others.
        var accounts = Observe("Identity", "Accounts, sessions and access", identity.Enabled,
            token => identity.ListAsync(null, 1, 1, token), ct);
        var players = Observe("Characters", "Characters and game connections", characters.Enabled,
            token => characters.OverviewAsync(token), ct);
        var worlds = Observe("Realms", "Catalogue and k3s workloads", realms.Enabled,
            token => realms.ListAsync(token), ct);
        var quests = Observe("Content", "Quest drafts and catalogues", contentOptions.Value.Enabled,
            token => content.GetDraftAsync(token), ct);
        var bugs = Observe("Reports", "Bugs and suggestions", reports.Enabled,
            token => reports.ListAsync(null, null, 0, 1, token), ct);
        var agents = Observe("Inference", "LFM agents and workers", inference.Enabled,
            token => inference.ListAsync(token), ct);
        await Task.WhenAll(accounts, players, worlds, quests, bugs, agents);
        return new(clock.GetUtcNow(), [(await accounts).Status, (await players).Status, (await worlds).Status,
            (await quests).Status, (await bugs).Status, (await agents).Status],
            (await accounts).Value?.Total, (await players).Value, (await worlds).Value);
    }

    private static async Task<Observation<T>> Observe<T>(string name, string purpose, bool enabled,
        Func<CancellationToken, Task<T>> read, CancellationToken ct) where T : class
    {
        if (!enabled) return new(new(name, purpose, "disabled", null), null);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var watch = Stopwatch.StartNew();
        try
        {
            var value = await read(timeout.Token);
            return new(new(name, purpose, "ready", watch.ElapsedMilliseconds), value);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or ContentApiException or
            RealmsApiException or ReportsApiException or InferenceApiException or PlayerAdminApiException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new(new(name, purpose, "failed", watch.ElapsedMilliseconds), null);
        }
    }

    private sealed record Observation<T>(ServiceObservation Status, T? Value);
}
