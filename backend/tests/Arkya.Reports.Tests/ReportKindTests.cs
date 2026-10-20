using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arkya.ServiceDefaults.Reports;

namespace Arkya.Reports.Tests;

public sealed class ReportKindTests
{
    [Fact]
    public async Task LegacySubmissionAndResponseWithoutKindDefaultToBug()
    {
        using var factory = new ReportsFactory();
        using var player = factory.Client("alice");
        var request = Fixtures.Request();
        var legacy = JsonSerializer.SerializeToNode(request, JsonSerializerOptions.Web)!.AsObject();
        legacy.Remove("kind");
        var created = await player.PostAsJsonAsync("/v1/bug-reports", legacy);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var report = (await created.Content.ReadFromJsonAsync<BugReportResponse>())!;
        Assert.Equal(BugReportKinds.Bug, report.Kind);
        var retry = await player.PostAsJsonAsync("/v1/bug-reports", request with { Kind = BugReportKinds.Bug });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(report.Id, (await retry.Content.ReadFromJsonAsync<BugReportResponse>())!.Id);
        var oldResponse = JsonSerializer.SerializeToNode(report, JsonSerializerOptions.Web)!.AsObject();
        oldResponse.Remove("kind");
        Assert.Equal(BugReportKinds.Bug, oldResponse.Deserialize<BugReportResponse>(JsonSerializerOptions.Web)!.Kind);
    }

    [Fact]
    public async Task SuggestionsUseTheSameScreenshotAndProtectedPlayerOwnership()
    {
        using var factory = new ReportsFactory();
        using var alice = factory.Client("alice");
        using var bob = factory.Client("bob");
        using var admin = factory.Client(admin: true);
        var created = await alice.PostAsJsonAsync("/v1/bug-reports",
            Fixtures.Request() with { Kind = BugReportKinds.Suggestion, Title = "Ajouter un pont" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var suggestion = (await created.Content.ReadFromJsonAsync<BugReportResponse>())!;
        Assert.Equal(BugReportKinds.Suggestion, suggestion.Kind);
        Assert.Equal(BugReportStatuses.New, suggestion.Status);
        Assert.Equal("image/png", suggestion.Screenshot.ContentType);
        Assert.Equal(Fixtures.Png(), await admin.GetByteArrayAsync($"/internal/bug-reports/{suggestion.Id}/screenshot"));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/v1/bug-reports/{suggestion.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/v1/bug-reports",
            Fixtures.Request() with { Kind = BugReportKinds.Suggestion, ScreenshotBase64 = null })).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("feedback")]
    [InlineData("Suggestion")]
    [InlineData("BUG")]
    [InlineData(" bug ")]
    public async Task ExplicitInvalidKindsAreRejected(string? kind)
    {
        using var factory = new ReportsFactory();
        using var player = factory.Client("alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/v1/bug-reports",
            Fixtures.Request() with { Kind = kind })).StatusCode);
        Assert.Empty((await player.GetFromJsonAsync<BugReportListResponse>("/v1/bug-reports"))!.Items);
    }

    [Theory]
    [InlineData(BugReportKinds.Bug, BugReportKinds.Suggestion)]
    [InlineData(BugReportKinds.Suggestion, BugReportKinds.Bug)]
    public async Task IdempotencyIncludesTheImmutableKind(string firstKind, string otherKind)
    {
        using var factory = new ReportsFactory();
        using var player = factory.Client("alice");
        var request = Fixtures.Request() with { Kind = firstKind };
        var created = await player.PostAsJsonAsync("/v1/bug-reports", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = (await created.Content.ReadFromJsonAsync<BugReportResponse>())!;
        var replay = await player.PostAsJsonAsync("/v1/bug-reports", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first.Id, (await replay.Content.ReadFromJsonAsync<BugReportResponse>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await player.PostAsJsonAsync("/v1/bug-reports",
            request with { Kind = otherKind })).StatusCode);
    }

    [Fact]
    public async Task KindFiltersComposeWithOwnershipAndAdminTriageFilters()
    {
        using var factory = new ReportsFactory();
        using var alice = factory.Client("alice");
        using var bob = factory.Client("bob");
        using var admin = factory.Client(admin: true);
        await alice.PostAsJsonAsync("/v1/bug-reports", Fixtures.Request());
        var submitted = await alice.PostAsJsonAsync("/v1/bug-reports", Fixtures.Request() with { Kind = BugReportKinds.Suggestion });
        await bob.PostAsJsonAsync("/v1/bug-reports", Fixtures.Request() with { Kind = BugReportKinds.Suggestion });
        var suggestion = (await submitted.Content.ReadFromJsonAsync<BugReportResponse>())!;
        var triaged = await admin.PatchAsJsonAsync($"/internal/bug-reports/{suggestion.Id}",
            new UpdateBugReportRequest(1, BugReportStatuses.Triaged, ["urgent"], "À étudier"));
        Assert.Equal(BugReportKinds.Suggestion, (await triaged.Content.ReadFromJsonAsync<BugReportResponse>())!.Kind);

        Assert.Equal(2, (await alice.GetFromJsonAsync<BugReportListResponse>("/v1/bug-reports"))!.Total);
        var ownSuggestions = (await alice.GetFromJsonAsync<BugReportListResponse>("/v1/bug-reports?kind=suggestion"))!;
        Assert.Equal(1, ownSuggestions.Total);
        Assert.Equal(suggestion.Id, Assert.Single(ownSuggestions.Items).Id);
        Assert.Equal(BugReportKinds.Bug, Assert.Single((await alice.GetFromJsonAsync<BugReportListResponse>("/v1/bug-reports?kind=bug"))!.Items).Kind);
        Assert.Equal(2, (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?kind=suggestion"))!.Total);
        var filtered = await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?kind=suggestion&status=triaged&tag=urgent");
        Assert.Equal(suggestion.Id, Assert.Single(filtered!.Items).Id);
        Assert.Equal(3, (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports"))!.Total);
    }

    [Theory]
    [InlineData("kind=")]
    [InlineData("kind=unknown")]
    [InlineData("kind=Suggestion")]
    [InlineData("kind=bug&kind=suggestion")]
    public async Task BothListEndpointsRejectInvalidKindFilters(string query)
    {
        using var factory = new ReportsFactory();
        using var player = factory.Client("alice");
        using var admin = factory.Client(admin: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await player.GetAsync("/v1/bug-reports?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/internal/bug-reports?" + query)).StatusCode);
    }

    [Fact]
    public async Task AdministratorCannotChangeKindThroughPatch()
    {
        using var factory = new ReportsFactory();
        using var player = factory.Client("alice");
        using var admin = factory.Client(admin: true);
        var response = await player.PostAsJsonAsync("/v1/bug-reports", Fixtures.Request() with { Kind = BugReportKinds.Suggestion });
        var suggestion = (await response.Content.ReadFromJsonAsync<BugReportResponse>())!;
        var patch = new JsonObject
        {
            ["expectedRevision"] = 1, ["status"] = "triaged", ["tags"] = new JsonArray(),
            ["resolution"] = "", ["kind"] = "bug"
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/internal/bug-reports/{suggestion.Id}", patch)).StatusCode);
        var unchanged = (await admin.GetFromJsonAsync<BugReportResponse>($"/internal/bug-reports/{suggestion.Id}"))!;
        Assert.Equal(BugReportKinds.Suggestion, unchanged.Kind);
        Assert.Equal(1, unchanged.Revision);
    }
}
