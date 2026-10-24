using System.Text.RegularExpressions;
using Arkya.Identity.Api.Domain;
using Arkya.ServiceDefaults.Accounts;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Identity.Api.Infrastructure;

public sealed partial class MongoIdentityStore
{
    private readonly IMongoCollection<BetaGrantOperationDocument> betaGrantOperations;

    private async Task InitializeBetaAsync(CancellationToken ct)
    {
        await accounts.Indexes.CreateOneAsync(new CreateIndexModel<AccountDocument>(
            Builders<AccountDocument>.IndexKeys.Ascending(x => x.HasBetaAccess).Ascending(x => x.IsBanned)
                .Ascending(x => x.BetaRegisteredAt).Ascending(x => x.Id),
            new CreateIndexOptions { Name = "beta_registrations" }), cancellationToken: ct);
        await betaGrantOperations.Indexes.CreateOneAsync(new CreateIndexModel<BetaGrantOperationDocument>(
            Builders<BetaGrantOperationDocument>.IndexKeys.Ascending(x => x.Completed).Ascending(x => x.CreatedAt),
            new CreateIndexOptions { Name = "pending_beta_grants" }), cancellationToken: ct);
    }

    public async Task<Account?> RegisterBetaAsync(string accountId, DateTime now, CancellationToken ct)
    {
        await accounts.UpdateOneAsync(x => x.Id == accountId && !x.IsBanned && x.BetaRegisteredAt == null,
            Builders<AccountDocument>.Update.Set(x => x.BetaRegisteredAt, now), cancellationToken: ct);
        return (await accounts.Find(x => x.Id == accountId && !x.IsBanned).FirstOrDefaultAsync(ct))?.ToDomain();
    }

    public async Task<(IReadOnlyList<Account> Items, long Total)> ListBetaRegistrationsAsync(string? search, string status,
        int page, int pageSize, CancellationToken ct)
    {
        var filters = Builders<AccountDocument>.Filter;
        var filter = filters.Ne(x => x.BetaRegisteredAt, null);
        filter &= status switch
        {
            "pending" => filters.Ne(x => x.IsBanned, true) & filters.Ne(x => x.HasBetaAccess, true),
            "granted" => filters.Eq(x => x.HasBetaAccess, true),
            "banned" => filters.Eq(x => x.IsBanned, true),
            _ => filters.Empty
        };
        if (!string.IsNullOrWhiteSpace(search))
            filter &= filters.Or(filters.Eq(x => x.Id, search), filters.Eq(x => x.DiscordId, search),
                filters.Regex(x => x.DisplayName, new BsonRegularExpression(Regex.Escape(search), "i")));
        var total = await accounts.CountDocumentsAsync(filter, cancellationToken: ct);
        var result = await accounts.Find(filter).SortBy(x => x.BetaRegisteredAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
        return (result.Select(account => account.ToDomain()).ToArray(), total);
    }

    public async Task<Account?> GrantBetaAsync(string accountId, long expectedRevision, string reason, string actor,
        DateTime now, CancellationToken ct)
    {
        var account = await accounts.FindOneAndUpdateAsync(EligibleBeta() & RevisionFilter(expectedRevision) &
            Builders<AccountDocument>.Filter.Eq(x => x.Id, accountId),
            BetaGrantUpdate(expectedRevision, reason, actor, "beta-access", now),
            new() { ReturnDocument = ReturnDocument.After }, ct);
        return account?.ToDomain();
    }

    public async Task<BetaRandomGrantResponse?> GrantRandomBetaAsync(Guid requestId, int count, string reason, string actor,
        DateTime now, CancellationToken ct)
    {
        var id = requestId.ToString("D");
        var operation = await betaGrantOperations.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
        if (operation is null)
        {
            var selected = await accounts.Aggregate().Match(EligibleBeta() &
                Builders<AccountDocument>.Filter.Ne(x => x.Revision, long.MaxValue)).Sample(count).ToListAsync(ct);
            var proposed = new BetaGrantOperationDocument
            {
                Id = id, Count = count, Reason = reason, Actor = actor, CreatedAt = now,
                Selections = selected.Select(account => new BetaGrantSelection { AccountId = account.Id, Revision = account.Revision }).ToArray()
            };
            try
            {
                await betaGrantOperations.InsertOneAsync(proposed, cancellationToken: ct);
                operation = proposed;
            }
            catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                operation = await betaGrantOperations.Find(x => x.Id == id).SingleAsync(ct);
            }
        }
        if (operation.Count != count || operation.Reason != reason || operation.Actor != actor) return null;
        return await FinishBetaGrantAsync(operation, ct);
    }

    public async Task RecoverBetaGrantsAsync(CancellationToken ct)
    {
        var pending = await betaGrantOperations.Find(x => !x.Completed).SortBy(x => x.CreatedAt).Limit(25).ToListAsync(ct);
        foreach (var operation in pending) await FinishBetaGrantAsync(operation, ct);
    }

    private async Task<BetaRandomGrantResponse> FinishBetaGrantAsync(BetaGrantOperationDocument operation, CancellationToken ct)
    {
        if (!operation.Completed)
        {
            for (var index = 0; index < operation.Selections.Length; index++)
            {
                var selection = operation.Selections[index];
                if (!selection.Granted.HasValue)
                {
                    var filters = Builders<AccountDocument>.Filter;
                    // Persist the draw before granting, then hold account administration until its outcome is durable.
                    // This also recovers a crash between the account update and recording the operation outcome.
                    var granted = await accounts.FindOneAndUpdateAsync(EligibleBeta() & RevisionFilter(selection.Revision) &
                        filters.Eq(x => x.Id, selection.AccountId),
                        BetaGrantUpdate(selection.Revision, operation.Reason, operation.Actor, "beta-random-access:" + operation.Id, operation.CreatedAt)
                            .Set(x => x.PendingBetaGrantOperationId, operation.Id),
                        new() { ReturnDocument = ReturnDocument.After }, ct);
                    var succeeded = granted is not null || await accounts.Find(x => x.Id == selection.AccountId &&
                        x.PendingBetaGrantOperationId == operation.Id).AnyAsync(ct);
                    // Keep the null check within this element; a dotted numeric/null path can match missing object fields.
                    await betaGrantOperations.UpdateOneAsync(Builders<BetaGrantOperationDocument>.Filter.Eq(x => x.Id, operation.Id) &
                        Builders<BetaGrantOperationDocument>.Filter.ElemMatch(x => x.Selections,
                            item => item.AccountId == selection.AccountId && item.Granted == null),
                        Builders<BetaGrantOperationDocument>.Update.Set("Selections.$.Granted", succeeded), cancellationToken: ct);
                }
                // Outcomes are written first, so a replay can never regrant access following a later revocation.
                await accounts.UpdateOneAsync(x => x.Id == selection.AccountId && x.PendingBetaGrantOperationId == operation.Id,
                    Builders<AccountDocument>.Update.Unset(x => x.PendingBetaGrantOperationId), cancellationToken: ct);
            }
            await betaGrantOperations.UpdateOneAsync(x => x.Id == operation.Id,
                Builders<BetaGrantOperationDocument>.Update.Set(x => x.Completed, true), cancellationToken: ct);
            operation = await betaGrantOperations.Find(x => x.Id == operation.Id).SingleAsync(ct);
        }
        var grantedIds = operation.Selections.Where(x => x.Granted == true).Select(x => x.AccountId).ToArray();
        return new(Guid.Parse(operation.Id), operation.Count, grantedIds.Length, grantedIds,
            new DateTimeOffset(DateTime.SpecifyKind(operation.CreatedAt, DateTimeKind.Utc)));
    }

    private static FilterDefinition<AccountDocument> EligibleBeta()
    {
        var filters = Builders<AccountDocument>.Filter;
        return filters.Ne(x => x.BetaRegisteredAt, null) & filters.Ne(x => x.IsBanned, true) &
            filters.Ne(x => x.HasBetaAccess, true) & filters.Eq(x => x.PendingBetaGrantOperationId, null);
    }

    private static FilterDefinition<AccountDocument> RevisionFilter(long expectedRevision)
    {
        var filters = Builders<AccountDocument>.Filter;
        return expectedRevision == 0
            ? filters.Eq(x => x.Revision, 0) | filters.Exists(x => x.Revision, false)
            : filters.Eq(x => x.Revision, expectedRevision);
    }

    private static UpdateDefinition<AccountDocument> BetaGrantUpdate(long revision, string reason, string actor, string action, DateTime now) =>
        Builders<AccountDocument>.Update.Set(x => x.HasBetaAccess, true).Inc(x => x.Revision, 1)
            .Set(x => x.SchemaVersion, 2).Set(x => x.UpdatedAt, now)
            .PushEach(x => x.AdministrationHistory, [new AccountAdministrationChange(revision + 1, now, actor, action, reason)], slice: -100);

    private sealed class BetaGrantOperationDocument
    {
        [BsonId] public string Id { get; set; } = "";
        public int SchemaVersion { get; set; } = 1;
        public int Count { get; set; }
        public string Reason { get; set; } = "";
        public string Actor { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public BetaGrantSelection[] Selections { get; set; } = [];
        public bool Completed { get; set; }
    }

    private sealed class BetaGrantSelection
    {
        public string AccountId { get; set; } = "";
        public long Revision { get; set; }
        public bool? Granted { get; set; }
    }
}
