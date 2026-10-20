using Arkya.Reports.Api.Application;
using Arkya.Reports.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Reports;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text.Json;

namespace Arkya.Reports.Tests;

public sealed class MongoReportRepositoryTests
{
    [MongoFact]
    public async Task LegacyBugsAndSuggestionsRemainCompatibleAcrossPersistenceFiltersAndRetries()
    {
        var database = $"arkya_reports_test_{Guid.NewGuid():N}";
        var client = Client();
        try
        {
            var repository = Repository(client, database);
            await repository.InitializeAsync(default);
            var service = new ReportService(repository, new FixedClock());
            var request = Fixtures.Request();
            var bug = (await service.SubmitAsync(StubSessions.Alice, request, default)).Report!;
            var suggestionRequest = Fixtures.Request() with { Kind = BugReportKinds.Suggestion };
            var suggestion = (await service.SubmitAsync(StubSessions.Alice, suggestionRequest, default)).Report!;
            var otherOwner = (await service.SubmitAsync(StubSessions.Bob, Fixtures.Request(), default)).Report!;
            var collection = client.GetDatabase(database).GetCollection<BsonDocument>("bug_reports");
            var originalFingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                title = request.Title!.Trim(), description = request.Description!.Trim(), version = request.ClientVersion!.Trim(),
                scene = request.Scene!.Trim(), platform = request.Platform!.Trim(), screenshot = bug.Screenshot.Sha256
            })));
            var initial = await collection.Find(new BsonDocument("_id", bug.Id.ToString("D"))).SingleAsync();
            Assert.Equal(originalFingerprint, initial["PayloadHash"].AsString);
            await collection.UpdateManyAsync(
                new BsonDocument("_id", new BsonDocument("$in", new BsonArray { bug.Id.ToString("D"), otherOwner.Id.ToString("D") })),
                new BsonDocument("$unset", new BsonDocument("Kind", "")));

            var restarted = Repository(client, database);
            var legacy = (await restarted.FindAsync(bug.Id, StubSessions.Alice, default))!;
            Assert.Equal(BugReportKinds.Bug, legacy.Kind);
            Assert.Equal(BugReportKinds.Suggestion, (await restarted.FindAsync(suggestion.Id, StubSessions.Alice, default))!.Kind);
            var ownBugs = await restarted.ListAsync(StubSessions.Alice, null, null, 0, 20, default, BugReportKinds.Bug);
            Assert.Equal(1, ownBugs.Total);
            Assert.Equal(bug.Id, Assert.Single(ownBugs.Items).Id);
            var ownSuggestions = await restarted.ListAsync(StubSessions.Alice, null, null, 0, 20, default, BugReportKinds.Suggestion);
            Assert.Equal(suggestion.Id, Assert.Single(ownSuggestions.Items).Id);
            Assert.Equal(2, (await restarted.ListAsync(null, null, null, 0, 20, default, BugReportKinds.Bug)).Total);
            Assert.Equal(3, (await restarted.ListAsync(null, null, null, 0, 20, default)).Total);
            var restartedService = new ReportService(restarted, new FixedClock());
            Assert.Equal(ReportChangeStatus.Existing, (await restartedService.SubmitAsync(StubSessions.Alice, request, default)).Status);
            Assert.Equal(ReportChangeStatus.Existing, (await restartedService.SubmitAsync(StubSessions.Alice, suggestionRequest, default)).Status);
            Assert.Equal(ReportChangeStatus.Conflict, (await restartedService.SubmitAsync(StubSessions.Alice,
                request with { Kind = BugReportKinds.Suggestion }, default)).Status);
            var triaged = (await restartedService.UpdateAsync(suggestion.Id, new(1, "triaged", ["urgent"], ""), "operator", default)).Report!;
            Assert.Equal(BugReportKinds.Suggestion, triaged.Kind);
            Assert.Equal(suggestion.Id, Assert.Single((await restarted.ListAsync(null, "urgent", "triaged", 0, 20,
                default, BugReportKinds.Suggestion)).Items).Id);
            Assert.Equal(Fixtures.Png(), await restarted.ScreenshotAsync(suggestion.Id, default));
            using var cursor = await collection.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            Assert.Contains(indexes, index => index["name"] == "ix_report_kind_created");
            Assert.Contains(indexes, index => index["name"] == "ix_report_owner_kind_created");
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task ReportAndScreenshotPersistAtomicallyAndIdempotencySurvivesConcurrencyAndRestart()
    {
        var database = $"arkya_reports_test_{Guid.NewGuid():N}";
        var client = Client();
        try
        {
            var first = Repository(client, database);
            var second = Repository(client, database);
            await Task.WhenAll(first.InitializeAsync(default), second.InitializeAsync(default));
            var request = Fixtures.Request();
            var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
                new ReportService(index % 2 == 0 ? first : second, new FixedClock()).SubmitAsync(StubSessions.Alice, request, default)));
            Assert.Single(attempts, item => item.Status == ReportChangeStatus.Created);
            Assert.Equal(7, attempts.Count(item => item.Status == ReportChangeStatus.Existing));
            var id = attempts[0].Report!.Id;
            Assert.All(attempts, item => Assert.Equal(id, item.Report!.Id));
            Assert.Equal(Fixtures.Png(), await second.ScreenshotAsync(id, default));
            var restarted = new ReportService(Repository(client, database), new FixedClock());
            Assert.Equal(ReportChangeStatus.Existing, (await restarted.SubmitAsync(StubSessions.Alice, request, default)).Status);
            Assert.Equal(ReportChangeStatus.Conflict,
                (await restarted.SubmitAsync(StubSessions.Alice, request with { Description = "changed" }, default)).Status);
            Assert.Equal(ReportChangeStatus.Created, (await restarted.SubmitAsync(StubSessions.Bob, request, default)).Status);
            Assert.Null(await first.FindAsync(id, StubSessions.Bob, default));
            Assert.Equal(1, (await first.ListAsync(StubSessions.Alice, null, null, 0, 20, default)).Total);
            var collection = client.GetDatabase(database).GetCollection<BsonDocument>("bug_reports");
            var document = await collection.Find(new BsonDocument("_id", id.ToString("D"))).SingleAsync();
            Assert.Equal(1, document["SchemaVersion"].AsInt32);
            Assert.Equal(BsonType.Binary, document["ScreenshotBytes"].BsonType);
            Assert.Equal(Fixtures.Png(), document["ScreenshotBytes"].AsByteArray);
            using var cursor = await collection.Indexes.ListAsync();
            Assert.True((await cursor.ToListAsync()).Single(index => index["name"] == "ux_report_account_client_id")["unique"].AsBoolean);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task TriageIsCompareAndSwapWithBoundedAuditAndQueriesDoNotLoadScreenshotBytes()
    {
        var database = $"arkya_reports_test_{Guid.NewGuid():N}";
        var client = Client();
        try
        {
            var repository = Repository(client, database);
            await repository.InitializeAsync(default);
            var service = new ReportService(repository, new FixedClock());
            var report = (await service.SubmitAsync(StubSessions.Alice, Fixtures.Request(), default)).Report!;
            var updates = await Task.WhenAll(
                service.UpdateAsync(report.Id, new(1, "triaged", [" Urgent "], "reviewing"), "operator:one", default),
                new ReportService(Repository(client, database), new FixedClock()).UpdateAsync(
                    report.Id, new(1, "triaged", [" Urgent "], "reviewing"), "operator:two", default));
            Assert.Single(updates, item => item.Status == ReportChangeStatus.Updated);
            Assert.Single(updates, item => item.Status == ReportChangeStatus.Conflict);
            var updated = updates.Single(item => item.Status == ReportChangeStatus.Updated).Report!;
            Assert.Equal(2, updated.Revision);
            Assert.Equal("urgent", Assert.Single(updated.Tags));
            Assert.Single(updated.History);
            var matches = await repository.ListAsync(null, "urgent", "triaged", 0, 20, default);
            Assert.Equal(report.Id, Assert.Single(matches.Items).Id);
            Assert.Empty(matches.Items[0].History);
            Assert.Empty((await repository.ListAsync(null, "urgent", "resolved", 0, 20, default)).Items);
            for (var revision = 2; revision < 60; revision++)
                Assert.Equal(ReportChangeStatus.Updated, (await service.UpdateAsync(report.Id,
                    new(revision, "resolved", ["urgent"], new string('é', 4000)), "operator:one", default)).Status);
            var final = (await Repository(client, database).FindAsync(report.Id, null, default))!;
            Assert.Equal(60, final.Revision);
            Assert.Equal(50, final.History.Length);
            Assert.Equal(11, final.History[0].Revision);
            Assert.Equal(Fixtures.Png(), await repository.ScreenshotAsync(report.Id, default));
            var stored = await client.GetDatabase(database).GetCollection<BsonDocument>("bug_reports")
                .Find(new BsonDocument("_id", report.Id.ToString("D"))).SingleAsync();
            Assert.True(stored.ToBson().Length < 16 * 1024 * 1024);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    private static MongoReportRepository Repository(IMongoClient client, string database) =>
        new(client, Options.Create(new MongoOptions { DatabaseName = database }));
    private static MongoClient Client()
    {
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        return new(settings);
    }
}

public sealed class MongoFactAttribute : FactAttribute
{
    public MongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")))
            Skip = "Set ARKYA_TEST_MONGO to run against a disposable MongoDB database.";
    }
}
